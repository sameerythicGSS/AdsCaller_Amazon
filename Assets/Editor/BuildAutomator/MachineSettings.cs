using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace OutBuild
{
    [Serializable]
    public sealed class MachineSettings
    {
        public int schemaVersion = 1;
        public string outputFolder = "", adbOverride = "";
        public bool autoRefresh = true, revealBuild = true;
        public int refreshSeconds = 3;
        public List<SavedDevice> devices = new List<SavedDevice>();
        public List<string> migratedProjects = new List<string>();
        public List<string> forgottenDevices = new List<string>();
    }

    /// <summary>
    /// Shared by all projects for the current OS user. Every write reloads the latest state
    /// under an interprocess lock and applies only its own change, never an old UI snapshot.
    /// A custom path allows tests to stay entirely inside their disposable project.
    /// </summary>
    public sealed class MachineSettingsStore
    {
        public string FilePath { get; private set; }
        public static string DefaultPath
        {
            get
            {
                string overridePath = Environment.GetEnvironmentVariable("OUT_BUILD_SETTINGS_PATH");
                return string.IsNullOrWhiteSpace(overridePath)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OutTools", "BuildAutomator", "settings.json")
                    : Path.GetFullPath(overridePath);
            }
        }

        public MachineSettingsStore(string filePath) { FilePath = Path.GetFullPath(filePath); }

        public MachineSettings Read()
        {
            if (!File.Exists(FilePath)) return new MachineSettings();
            MachineSettings state;
            try { state = JsonUtility.FromJson<MachineSettings>(File.ReadAllText(FilePath)); }
            catch (Exception e) { throw new IOException("Cannot read shared Build Automator settings. Restore settings.json.bak or repair " + FilePath, e); }
            if (state == null || state.schemaVersion != 1)
                throw new IOException("Unsupported shared Build Automator settings: " + FilePath);
            state.devices = (state.devices ?? new List<SavedDevice>()).Where(d => d != null).ToList();
            state.migratedProjects = state.migratedProjects ?? new List<string>();
            state.forgottenDevices = state.forgottenDevices ?? new List<string>();
            foreach (var device in state.devices) Normalize(device);
            return state;
        }

        private MachineSettings Change(Action<MachineSettings> update)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            using (AcquireLock())
            {
                var state = Read();
                string before = JsonUtility.ToJson(state);
                update(state);
                if (before != JsonUtility.ToJson(state)) BuildStorage.Write(FilePath, state);
                return state;
            }
        }

        private FileStream AcquireLock()
        {
            var timer = Stopwatch.StartNew();
            while (true)
            {
                try { return new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException)
                {
                    if (timer.ElapsedMilliseconds >= 500) throw new IOException("Another Editor is saving shared Build Automator settings. Please retry.");
                    Thread.Sleep(20);
                }
            }
        }

        public MachineSettings SetOutputFolder(string folder)
        {
            if (!string.IsNullOrWhiteSpace(folder) && !Path.IsPathRooted(folder))
                throw new ArgumentException("The shared build location must be an absolute path. Use Browse to choose a folder.");
            string path = string.IsNullOrWhiteSpace(folder) ? "" : BuildStorage.OutputRoot(folder);
            return Change(s => s.outputFolder = path);
        }

        public MachineSettings SetAdbOverride(string path) { return Change(s => s.adbOverride = path ?? ""); }
        public MachineSettings SetAutoRefresh(bool enabled) { return Change(s => s.autoRefresh = enabled); }
        public MachineSettings SetRevealBuild(bool enabled) { return Change(s => s.revealBuild = enabled); }

        public MachineSettings SetRefreshSeconds(int seconds) { return Change(s => s.refreshSeconds = Math.Max(2, Math.Min(30, seconds))); }
        public MachineSettings MarkPaired(string key) { return Change(s => { var device = s.devices.FirstOrDefault(d => d.key == key); if (device != null) device.pairedWireless = true; }); }

        public MachineSettings Migrate(string projectKey, LocalState legacy)
        {
            return Change(state =>
            {
                if (state.migratedProjects.Contains(projectKey)) return;
                foreach (var entry in legacy.devices ?? new List<SavedDevice>())
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.id)) continue;
                    string address = NormalizeTransport(entry.id);
                    if (state.forgottenDevices.Contains(address) || (!string.IsNullOrEmpty(entry.hardwareId) && state.forgottenDevices.Contains("serial:" + entry.hardwareId))) continue;
                    var saved = Merge(state, address, entry.hardwareId, entry.model, entry.lastSeenUtc);
                    if (string.IsNullOrEmpty(saved.nickname)) saved.nickname = entry.nickname ?? "";
                }
                // Only the first legacy project seeds machine preferences. Later projects cannot
                // overwrite an intentional choice made in another Editor (including an empty override).
                if (state.migratedProjects.Count == 0)
                {
                    if (string.IsNullOrEmpty(state.adbOverride)) state.adbOverride = legacy.adbOverride ?? "";
                    state.autoRefresh = legacy.autoRefresh;
                    state.revealBuild = legacy.revealBuild;
                }
                state.migratedProjects.Add(projectKey);
            });
        }

        public MachineSettings Observe(IEnumerable<Device> devices)
        {
            var observations = devices.ToArray();
            return Change(state =>
            {
                foreach (var device in observations)
                {
                    string address = NormalizeTransport(device.Id);
                    if (string.IsNullOrEmpty(address)) continue;
                    Merge(state, address, device.HardwareId, device.Name, DateTime.UtcNow.ToString("yyyy-MM-dd"));
                }
            });
        }

        public MachineSettings AddEndpoint(string endpoint)
        {
            string address;
            if (!Adb.TryEndpoint(endpoint, out address)) throw new ArgumentException("Enter an IP/hostname and port.");
            address = NormalizeTransport(address);
            return Change(state => Merge(state, address, "", address, ""));
        }

        public MachineSettings Rename(string key, string nickname)
        {
            return Change(state =>
            {
                var device = state.devices.FirstOrDefault(d => d.key == key);
                if (device != null) device.nickname = (nickname ?? "").Trim();
            });
        }

        public MachineSettings Forget(string key)
        {
            return Change(state =>
            {
                var device = state.devices.FirstOrDefault(d => d.key == key);
                if (device == null) return;
                state.forgottenDevices.AddRange(device.aliases);
                if (!string.IsNullOrEmpty(device.hardwareId)) state.forgottenDevices.Add("serial:" + device.hardwareId);
                state.forgottenDevices = state.forgottenDevices.Distinct().ToList();
                state.devices.Remove(device);
            });
        }

        public static SavedDevice Find(MachineSettings state, string transport)
        {
            string address = NormalizeTransport(transport);
            return state.devices.FirstOrDefault(d => d.aliases.Contains(address));
        }

        public MachineSettings RememberMdns(string key, string instance)
        {
            instance = NormalizeMdnsInstance(instance);
            if (instance.Length == 0) return Read();
            return Change(state =>
            {
                var device = state.devices.FirstOrDefault(d => d.key == key);
                if (device == null) return;
                device.mdnsInstances.Add(instance);
                device.mdnsInstances = device.mdnsInstances.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            });
        }

        public static SavedDevice FindByMdns(MachineSettings state, string instance)
        {
            instance = NormalizeMdnsInstance(instance);
            if (instance.Length == 0) return null;
            return state.devices.FirstOrDefault(device =>
                (device.mdnsInstances ?? new List<string>()).Any(value =>
                    string.Equals(NormalizeMdnsInstance(value), instance, StringComparison.OrdinalIgnoreCase)) ||
                MdnsContainsHardwareIdentity(instance, device.hardwareId));
        }

        public static string NormalizeMdnsInstance(string value)
        {
            value = (value ?? "").Trim().TrimEnd('.');
            const string suffix = "._adb-tls-connect._tcp";
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                value = value.Substring(0, value.Length - suffix.Length);
            return Regex.IsMatch(value, @"^[a-zA-Z0-9][a-zA-Z0-9_.@+-]{0,254}$") ? value.ToLowerInvariant() : "";
        }

        private static bool MdnsContainsHardwareIdentity(string instance, string hardwareId)
        {
            hardwareId = ValidHardwareId(hardwareId);
            if (hardwareId.Length == 0) return false;
            string prefix = "adb-" + hardwareId;
            return string.Equals(instance, prefix, StringComparison.OrdinalIgnoreCase) ||
                instance.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase);
        }

        private static string MdnsFromTransport(string transport)
        {
            string value = (transport ?? "").Trim();
            int suffix = value.IndexOf("._adb-tls-connect._tcp", StringComparison.OrdinalIgnoreCase);
            return suffix > 0 ? NormalizeMdnsInstance(value.Substring(0, suffix)) : "";
        }

        public static bool ReconcileSelection(LocalState local, MachineSettings state)
        {
            bool changed = false;
            local.selectionIdentities = local.selectionIdentities ?? new List<DeviceSelection>();
            foreach (string transport in local.selectedDevices.ToArray())
            {
                string address = NormalizeTransport(transport);
                var previous = local.selectionIdentities.FirstOrDefault(s => NormalizeTransport(s.transport) == address);
                var current = Find(state, address);
                if (previous != null && !string.IsNullOrEmpty(previous.hardwareId) &&
                    (current == null || (!string.IsNullOrEmpty(current.hardwareId) && previous.hardwareId != current.hardwareId)))
                {
                    local.selectedDevices.Remove(transport);
                    local.selectionIdentities.Remove(previous);
                    local.requireDeviceSelection = true;
                    changed = true;
                }
                else if (current != null && !string.IsNullOrEmpty(current.hardwareId) && (previous == null || string.IsNullOrEmpty(previous.hardwareId)))
                {
                    if (previous == null) local.selectionIdentities.Add(new DeviceSelection { transport = address, hardwareId = current.hardwareId });
                    else previous.hardwareId = current.hardwareId;
                    changed = true;
                }
            }
            local.selectionIdentities.RemoveAll(s => !local.selectedDevices.Any(id => NormalizeTransport(id) == NormalizeTransport(s.transport)));
            return changed;
        }

        public static bool FollowReadyTransports(LocalState local, MachineSettings state, IEnumerable<Device> available)
        {
            var devices = (available ?? Enumerable.Empty<Device>()).Where(device => device.Ready).ToArray();
            var previous = local.selectedDevices.ToArray();
            local.selectedDevices = local.selectedDevices.GroupBy(id => Find(state, id)?.key ?? id)
                .Select(group =>
                {
                    string key = Find(state, group.First())?.key;
                    var ready = string.IsNullOrEmpty(key) ? null : devices.FirstOrDefault(device =>
                        (Find(state, device.Id)?.key ?? "") == key &&
                        group.Any(id => NormalizeTransport(id) == NormalizeTransport(device.Id)));
                    if (ready == null && !string.IsNullOrEmpty(key))
                        ready = devices.Where(device => (Find(state, device.Id)?.key ?? "") == key)
                            .OrderBy(device => device.Transport == "Wi-Fi" ? 0 : 1).FirstOrDefault();
                    return ready == null ? group.First() : ready.Id;
                }).Distinct().ToList();
            return ReconcileSelection(local, state) || !previous.SequenceEqual(local.selectedDevices);
        }

        private static SavedDevice Merge(MachineSettings state, string address, string hardwareId, string model, string seen)
        {
            hardwareId = ValidHardwareId(hardwareId);
            var matches = state.devices.Where(d => d.aliases.Contains(address) ||
                (hardwareId.Length > 0 && d.hardwareId == hardwareId)).ToList();
            // An address can be reassigned to another physical device. Never merge conflicting
            // known hardware identities just because they shared an IP or emulator transport.
            foreach (var conflict in matches.Where(d => hardwareId.Length > 0 && d.hardwareId.Length > 0 && d.hardwareId != hardwareId).ToArray())
            {
                conflict.aliases.Remove(address);
                if (conflict.id == address) conflict.id = conflict.aliases.FirstOrDefault() ?? "";
                matches.Remove(conflict);
            }
            var saved = matches.OrderByDescending(d => !string.IsNullOrEmpty(d.nickname)).FirstOrDefault();
            if (saved == null)
            {
                saved = new SavedDevice { key = Guid.NewGuid().ToString("N"), id = address };
                state.devices.Add(saved);
            }
            foreach (var duplicate in matches.Where(d => d != saved))
            {
                saved.aliases.AddRange(duplicate.aliases);
                saved.mdnsInstances.AddRange(duplicate.mdnsInstances);
                saved.pairedWireless |= duplicate.pairedWireless;
                if (string.IsNullOrEmpty(saved.hardwareId)) saved.hardwareId = duplicate.hardwareId;
                if (string.IsNullOrEmpty(saved.nickname)) saved.nickname = duplicate.nickname;
                state.devices.Remove(duplicate);
            }
            saved.id = address;
            saved.aliases.Add(address);
            saved.aliases = saved.aliases.Distinct().ToList();
            string mdns = MdnsFromTransport(address);
            if (mdns.Length > 0) saved.mdnsInstances.Add(mdns);
            saved.mdnsInstances = saved.mdnsInstances.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (hardwareId.Length > 0) saved.hardwareId = hardwareId;
            if (!string.IsNullOrWhiteSpace(model) && (model != address || string.IsNullOrEmpty(saved.model))) saved.model = model;
            if (string.CompareOrdinal(seen, saved.lastSeenUtc) > 0) saved.lastSeenUtc = seen;
            return saved;
        }

        private static void Normalize(SavedDevice device)
        {
            device.id = NormalizeTransport(device.id);
            device.hardwareId = ValidHardwareId(device.hardwareId);
            device.aliases = (device.aliases ?? new List<string>()).Append(device.id).Select(NormalizeTransport)
                .Where(a => a.Length > 0).Distinct().ToList();
            device.mdnsInstances = (device.mdnsInstances ?? new List<string>()).Concat(device.aliases.Select(MdnsFromTransport))
                .Select(NormalizeMdnsInstance).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (string.IsNullOrEmpty(device.key)) device.key = "legacy:" + device.id;
        }

        public static string ValidHardwareId(string value)
        {
            value = (value ?? "").Trim();
            if (value.Length < 4 || value.Length > 128 || value.Any(char.IsWhiteSpace) ||
                new[] { "unknown", "null", "none", "0123456789abcdef" }.Contains(value.ToLowerInvariant()) || value.All(c => c == '0')) return "";
            return value;
        }

        public static string NormalizeTransport(string value)
        {
            value = (value ?? "").Trim();
            string endpoint;
            if (Adb.TryEndpoint(value, out endpoint, 0))
            {
                int colon = endpoint.LastIndexOf(':');
                string host = endpoint.Substring(0, colon).Trim('[', ']');
                IPAddress ip;
                if (IPAddress.TryParse(host, out ip)) host = ip.ToString();
                host = host.TrimEnd('.').ToLowerInvariant();
                return (host.Contains(":") ? "[" + host + "]" : host) + endpoint.Substring(colon);
            }
            return value.Contains("._tcp") ? value.ToLowerInvariant() : value;
        }
    }
}
