using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OutBuild
{
    [InitializeOnLoad]
    public sealed class BuildController
    {
        private static BuildController instance;
        public static BuildController Instance { get { return instance ?? (instance = new BuildController()); } }
        static BuildController() { EditorApplication.delayCall += () => { if (!Application.isBatchMode) { var service = Instance; } }; }
        internal PresetFile presets;
        internal LocalState local;
        internal MachineSettings machine = new MachineSettings();
        internal MachineSettingsStore sharedStore;
        internal readonly Dictionary<string, string> connectionChoices = new Dictionary<string, string>();
        internal Func<string, CancellationToken, int, string[], Task<AdbResult>> RunAdb = Adb.Run;
        private readonly Dictionary<string, double> serviceAttempts = new Dictionary<string, double>();
        private readonly WirelessReconnect wirelessReconnect = new WirelessReconnect();
        private readonly Dictionary<string, int> wirelessProbeFailures = new Dictionary<string, int>();
        internal readonly OperationScheduler scheduler = new OperationScheduler();
        internal List<Device> devices = new List<Device>();
        internal List<WirelessService> services = new List<WirelessService>();
        internal string adb = "", adbVersion = "", mdnsStatus = "", message = "Ready", discoveryMessage = "", logs = "", version;
        internal int code;
        private BuildTarget lastTarget;
        private double nextMachineRefresh, nextRefresh;
        internal bool adbChecked;
        internal MessageType messageType = MessageType.Info;
        public event Action Changed;
        internal IEnumerable<WirelessService> AvailableServices
        {
            get { return services.Where(s => s.Pairing || (!WirelessServiceReady(s) && !(MachineSettingsStore.FindByMdns(machine, s.Name)?.pairedWireless ?? false)))
                .GroupBy(s => s.ConnectTarget, StringComparer.OrdinalIgnoreCase).Select(g => g.First()); }
        }
        internal bool Working { get { return scheduler.Busy || BuildJobs.Busy || BuildPipeline.isBuildingPlayer; } }
        internal bool Conflicting { get { return scheduler.ForegroundBusy || BuildJobs.Busy || BuildPipeline.isBuildingPlayer; } }
        internal BuildPreset Preset { get { return presets.presets[Mathf.Clamp(local.presetIndex, 0, presets.presets.Count - 1)]; } }
        internal List<Device> Selected { get { return devices.Where(d => d.Ready && IsSelected(d.Id)).GroupBy(d => MachineSettingsStore.Find(machine, d.Id)?.key ?? d.Id).Select(g => g.First()).ToList(); } }
        internal BuildRecord LastApk { get { return local.history.FirstOrDefault(h => h.IsApk && h.package == BuildJobs.Package && File.Exists(h.path)); } }

        private BuildController()
        {
            presets = BuildStorage.Read<PresetFile>(BuildStorage.PresetPath);
            if (BuildStorage.ValidatePresets(presets).Length > 0) presets = new PresetFile();
            sharedStore = BuildStorage.MachineStore;
            LoadLocal();
            if (!string.IsNullOrEmpty(local.statusMessage)) { message = local.statusMessage; messageType = local.statusWarning ? MessageType.Warning : MessageType.Info; }
            if (local.activeDeployment != "" && local.activeDeployment != null)
            {
                var record = local.history.FirstOrDefault(h => h.id == local.activeDeployment);
                if (record != null) record.deployment += "\nEditor was interrupted. Check the device before explicitly retrying deployment.";
                local.activeDeployment = "";
                SetMessage("Deployment was interrupted by an Editor restart or script reload. Check the device; no deployment was retried.", MessageType.Warning);
            }
            string recovery = BuildPipeline.isBuildingPlayer || BuildJobs.Busy ? "" : BuildRecovery.Recover();
            if (recovery.Length > 0) SetMessage(recovery, MessageType.Warning);
            SaveLocal();
            ReadVersion();
            adb = BuildStorage.FindAdb(machine.adbOverride);
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
        }
        private void Shutdown() { scheduler.Shutdown(); SaveLocal(); EditorApplication.update -= Tick; }
        private void Repaint() { Changed?.Invoke(); }
        internal void ChoosePreset(int index)
        {
            local.presetIndex = Mathf.Clamp(index, 0, presets.presets.Count - 1);
            local.selectedPreset = Preset.name;
            local.presetChosen = true;
            ReadVersion(); SaveLocal(); Repaint();
        }
        internal void LoadLocal()
        {
            string storedJson = File.Exists(BuildStorage.LocalPath) ? File.ReadAllText(BuildStorage.LocalPath) : "";
            bool legacyChoice = storedJson.Contains("\"presetIndex\"") && !storedJson.Contains("\"presetChosen\"");
            local = BuildStorage.Read<LocalState>(BuildStorage.LocalPath) ?? new LocalState();
            local.devices = (local.devices ?? new List<SavedDevice>()).Where(d => d != null && !string.IsNullOrEmpty(d.id)).ToList();
            local.history = (local.history ?? new List<BuildRecord>()).Where(h => h != null).ToList();
            local.selectedDevices = local.selectedDevices ?? new List<string>();
            int remembered = presets.presets.FindIndex(p => p.name == local.selectedPreset);
            int initial = presets.presets.FindIndex(p => p.name == (string.IsNullOrEmpty(presets.defaultPreset) ? "Release" : presets.defaultPreset));
            if (remembered >= 0) local.presetIndex = remembered;
            else if (!local.presetChosen && !legacyChoice) local.presetIndex = initial >= 0 ? initial : 0;
            local.presetIndex = Mathf.Clamp(local.presetIndex, 0, presets.presets.Count - 1);
            local.selectedPreset = presets.presets[local.presetIndex].name;
            local.presetChosen = true;
            local.selectedDevices = local.selectedDevices.Select(MachineSettingsStore.NormalizeTransport).Where(s => s.Length > 0).Distinct().ToList();
            try { ApplyMachine(sharedStore.Migrate(BuildStorage.Root, local)); }
            catch (Exception e) { SetMessage(e.Message, MessageType.Error); }
        }

        internal bool IsSelected(string transport)
        {
            string address = MachineSettingsStore.NormalizeTransport(transport);
            return local.selectedDevices.Any(s => MachineSettingsStore.NormalizeTransport(s) == address);
        }

        internal void ApplyMachine(MachineSettings state)
        {
            if (scheduler.Busy && machine.adbOverride != state.adbOverride) state.adbOverride = machine.adbOverride;
            bool changedAdb = machine.adbOverride != state.adbOverride;
            machine = state;
            int previousCount = local.selectedDevices.Count;
            if (MachineSettingsStore.ReconcileSelection(local, state)) SaveLocal();
            if (local.selectedDevices.Count < previousCount)
                SetMessage("A selected address changed device identity or was forgotten in another project. Select the intended device again.", MessageType.Warning);
            if (changedAdb)
            {
                adb = BuildStorage.FindAdb(machine.adbOverride);
                adbChecked = false;
                wirelessReconnect.Clear();
                wirelessProbeFailures.Clear();
                devices.Clear();
                services.Clear();
            }
        }

        internal void ChangeMachine(Func<MachineSettings> change)
        {
            try { ApplyMachine(change()); Repaint(); }
            catch (Exception e) { SetMessage("Shared settings: " + e.Message, MessageType.Error); }
        }

        internal void SelectTransport(SavedDevice saved, string transport, bool selected)
        {
            local.selectedDevices.RemoveAll(id => saved.aliases.Contains(MachineSettingsStore.NormalizeTransport(id)));
            local.selectionIdentities.RemoveAll(s => saved.aliases.Contains(MachineSettingsStore.NormalizeTransport(s.transport)));
            local.requireDeviceSelection = true;
            if (selected)
            {
                local.selectedDevices.Add(transport);
                local.selectionIdentities.Add(new DeviceSelection { transport = transport, hardwareId = saved.hardwareId });
            }
            SaveLocal();
        }

        internal void ReadVersion()
        {
            lastTarget = BuildJobs.Target;
            version = PlayerSettings.bundleVersion;
            int current = lastTarget == BuildTarget.iOS && int.TryParse(PlayerSettings.iOS.buildNumber, out int ios) ? ios : PlayerSettings.Android.bundleVersionCode;
            code = current < int.MaxValue && Preset.incrementVersionCode ? current + 1 : current;
        }


        private void Tick()
        {
            if (scheduler.Busy || BuildJobs.Busy || BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (lastTarget != BuildJobs.Target) ReadVersion();
            if (EditorApplication.timeSinceStartup >= nextMachineRefresh)
            {
                nextMachineRefresh = EditorApplication.timeSinceStartup + 2;
                try { ApplyMachine(sharedStore.Read()); }
                catch (Exception e) { discoveryMessage = e.Message; }
                Repaint();
            }
            if (machine.autoRefresh && EditorApplication.timeSinceStartup >= nextRefresh && !string.IsNullOrEmpty(adb))
            {
                nextRefresh = EditorApplication.timeSinceStartup + RefreshInterval;
                StartOperation("Discovering devices", token => RefreshDevices(token, false), true);
            }
        }

        internal double RefreshInterval { get { return Math.Max(2, Math.Min(30, machine.refreshSeconds == 0 ? 3 : machine.refreshSeconds)); } }
        internal void Stop() { scheduler.Cancel(); }
        internal async void StartOperation(string title, Func<CancellationToken, Task> action, bool quiet = false)
        {
            if (quiet && scheduler.Busy) return;
            if (!quiet) { message = title; messageType = MessageType.Info; Repaint(); }
            try { await scheduler.Run(action, quiet); }
            catch (OperationCanceledException)
            {
                if (!quiet) SetMessage("Operation cancelled. Work already handed to Unity or a device may still finish.", MessageType.Warning);
            }
            catch (Exception e)
            {
                if (quiet) discoveryMessage = e.Message;
                else SetMessage(e.Message, MessageType.Error);
            }
            finally { nextRefresh = EditorApplication.timeSinceStartup + RefreshInterval; if (!quiet) SaveLocal(); Repaint(); }
        }

        internal void SetMessage(string value, MessageType type)
        {
            if (scheduler.Quiet) { discoveryMessage = value; Repaint(); return; }
            message = value;
            messageType = type;
            local.statusMessage = value;
            local.statusWarning = type == MessageType.Error || type == MessageType.Warning;
            Repaint();
        }
        internal void SaveLocal()
        {
            try { BuildStorage.Write(BuildStorage.LocalPath, local); }
            catch (Exception e) { message = "Could not save local preferences: " + e.Message; messageType = MessageType.Warning; Repaint(); }
        }
        internal async Task RefreshDevices(CancellationToken token)
        {
            await RefreshDevices(token, true);
        }

        internal async Task RefreshDevices(CancellationToken token, bool probeSavedFallbacks)
        {
            if (string.IsNullOrEmpty(adb)) throw new InvalidOperationException("ADB not found. Install Android Build Support with SDK tools, or browse to adb under Advanced.");
            if (!adbChecked)
            {
                var versionResult = await RunAdb(adb, token, 10000, new[] { "version" });
                token.ThrowIfCancellationRequested();
                if (!versionResult.Success || !versionResult.Output.Contains("Android Debug Bridge"))
                    throw new InvalidOperationException("The configured executable did not return a valid ADB version. " + versionResult.Detail);
                adbVersion = versionResult.Output.Split('\n').FirstOrDefault() ?? "ADB";
                var serverStatus = await RunAdb(adb, token, 10000, new[] { "server-status" });
                mdnsStatus = serverStatus.Success
                    ? string.Join(" | ", serverStatus.Output.Split('\n').Select(line => line.Trim())
                        .Where(line => line.StartsWith("mdns_", StringComparison.OrdinalIgnoreCase)))
                    : "";
                if (string.IsNullOrEmpty(mdnsStatus))
                    mdnsStatus = "mDNS status unavailable (older Platform Tools may not support server-status).";
                adbChecked = true;
            }
            discoveryMessage = "";
            await ReadConnectedDevices(token);
            ApplyMachine(sharedStore.Observe(devices));
            int recovered = await RecoverWirelessDevices(token, probeSavedFallbacks);
            if (recovered > 0)
                SetMessage("Connected " + recovered + " wireless device" + (recovered == 1 ? "." : "s."), MessageType.Info);
            RememberDiscoveredMdnsIdentities();
            ReconcileSelectedTransports();
        }

        internal async Task ReadConnectedDevices(CancellationToken token)
        {
            var result = await RunAdb(adb, token, scheduler.Quiet ? 3000 : 10000, new[] { "devices", "-l" });
            token.ThrowIfCancellationRequested();
            if (!result.Success)
            {
                devices.Clear(); // Do not leave stale devices labelled Ready after a failed refresh.
                throw new InvalidOperationException(result.Detail);
            }
            var observed = Adb.ParseDevices(result.Output);
            using (var slots = new SemaphoreSlim(4))
            {
                await Task.WhenAll(observed.Where(d => d.Ready && d.Transport != "Emulator").Select(async device =>
                {
                    await slots.WaitAsync(token);
                    try
                    {
                        bool responsive = false;
                        foreach (string property in new[] { "ro.serialno", "ro.boot.serialno" })
                        {
                            var identity = await RunAdb(adb, token, scheduler.Quiet ? 1200 : 3000, new[] { "-s", device.Id, "shell", "getprop", property });
                            token.ThrowIfCancellationRequested();
                            if (identity.Success)
                            {
                                responsive = true;
                                device.HardwareId = MachineSettingsStore.ValidHardwareId(identity.Output);
                            }
                            if (!string.IsNullOrEmpty(device.HardwareId)) break;
                        }
                        if (device.Transport == "Wi-Fi")
                        {
                            int failures;
                            wirelessProbeFailures.TryGetValue(device.Id, out failures);
                            wirelessProbeFailures[device.Id] = responsive ? 0 : failures + 1;
                            // A stale "device" listing is not proof that the wireless transport responds.
                            // Require two failed device checks before attempting targeted recovery.
                            if (!responsive) device.State = failures >= 1 ? "offline" : "connecting";
                        }
                    }
                    finally { slots.Release(); }
                }));
            }
            token.ThrowIfCancellationRequested();
            devices = observed;
        }

        internal async Task<int> RecoverWirelessDevices(CancellationToken token, bool probeSavedFallbacks)
        {
            var discovery = await RunAdb(adb, token, 5000, new[] { "mdns", "services" });
            token.ThrowIfCancellationRequested();
            services = discovery.Success ? Adb.ParseServices(discovery.Output) : new List<WirelessService>();
            RememberDiscoveredMdnsIdentities();
            int recovered = 0;

            // A TLS connection advertisement can only become usable when this ADB host has
            // already been paired/authorized. Try every advertised connection service so a
            // new device reaches the checkbox list without first saving its current address.
            double roundStarted = EditorApplication.timeSinceStartup;
            foreach (var service in services.Where(item => !item.Pairing)
                .GroupBy(item => item.ConnectTarget, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
                .OrderByDescending(item => MachineSettingsStore.FindByMdns(machine, item.Name)?.aliases.Any(IsSelected) ?? false)
                .ThenByDescending(item => MachineSettingsStore.FindByMdns(machine, item.Name)?.pairedWireless ?? false)
                .ThenBy(item => serviceAttempts.TryGetValue(item.ConnectTarget, out double attempted) ? attempted : -1))
            {
                var targets = WirelessReconnect.Targets(new[] { service }, Enumerable.Empty<string>());
                if (WirelessServiceReady(service)) { wirelessReconnect.Reset(targets); continue; }
                if (!targets.Any(target => wirelessReconnect.CanAttempt(target, EditorApplication.timeSinceStartup))) continue;
                if (!probeSavedFallbacks && EditorApplication.timeSinceStartup - roundStarted > 6) break;
                serviceAttempts[service.ConnectTarget] = EditorApplication.timeSinceStartup;
                bool ready = await wirelessReconnect.TryConnect(targets,
                    target => RunAdb(adb, token, 4000, new[] { "connect", target }),
                    async () =>
                    {
                        await ReadConnectedDevices(token);
                        ApplyMachine(sharedStore.Observe(devices));
                        return WirelessServiceReady(service);
                    }, () => EditorApplication.timeSinceStartup, token);
                if (ready)
                {
                    recovered++;
                    RememberDiscoveredMdnsIdentities();
                }
            }

            if (!probeSavedFallbacks) return recovered;
            int fallbackAttempts = 0;
            // Selected targets go first so unavailable saved devices do not delay the device being used.
            foreach (var saved in machine.devices.OrderByDescending(item => item.aliases.Any(IsSelected)).ToArray())
            {
                var advertised = services.Where(service => !service.Pairing &&
                    MachineSettingsStore.FindByMdns(machine, service.Name)?.key == saved.key).ToArray();
                var known = new[] { saved.id }.Concat(saved.aliases.AsEnumerable().Reverse())
                    .Concat(saved.mdnsInstances.Select(instance => instance + "._adb-tls-connect._tcp"));
                var targets = WirelessReconnect.Targets(advertised, known);
                if (DeviceReady(saved.key)) { wirelessReconnect.Reset(targets); continue; }
                if (!probeSavedFallbacks && advertised.Length == 0) continue;
                if (!targets.Any(target => wirelessReconnect.CanAttempt(target, EditorApplication.timeSinceStartup))) continue;
                // Probe one undiscovered saved device per refresh, keeping background work bounded.
                if (advertised.Length == 0 && fallbackAttempts++ >= 1) continue;

                foreach (var stale in devices.Where(device => device.Transport == "Wi-Fi" && device.State == "offline" &&
                    MachineSettingsStore.Find(machine, device.Id)?.key == saved.key).ToArray())
                {
                    // Check again before resetting this one transport; another Editor may already have recovered it.
                    var probe = await RunAdb(adb, token, 2000, new[] { "-s", stale.Id, "shell", "echo", "build-automator" });
                    token.ThrowIfCancellationRequested();
                    if (!probe.Success)
                        await RunAdb(adb, token, 2000, new[] { "-s", stale.Id, "reconnect" });
                    token.ThrowIfCancellationRequested();
                }
                bool ready = await wirelessReconnect.TryConnect(targets,
                    target => RunAdb(adb, token, 4000, new[] { "connect", target }),
                    async () =>
                    {
                        await ReadConnectedDevices(token);
                        ApplyMachine(sharedStore.Observe(devices));
                        return DeviceReady(saved.key);
                    }, () => EditorApplication.timeSinceStartup, token);
                if (ready) recovered++;
            }
            return recovered;
        }

        internal bool WirelessServiceReady(WirelessService service)
        {
            var known = MachineSettingsStore.FindByMdns(machine, service.Name);
            return devices.Any(device => device.Ready && service.MatchesTransport(device.Id) &&
                (known == null || string.IsNullOrEmpty(known.hardwareId) || known.hardwareId == device.HardwareId));
        }

        internal bool DeviceReady(string key)
        {
            return devices.Any(device => device.Ready && (MachineSettingsStore.Find(machine, device.Id)?.key ?? "") == key);
        }

        internal void RememberDiscoveredMdnsIdentities()
        {
            foreach (var device in devices.Where(d => d.Ready && !string.IsNullOrEmpty(d.HardwareId) && d.Id.Contains("._adb-tls-connect._tcp")))
            {
                var known = MachineSettingsStore.Find(machine, device.Id);
                if (known != null && !known.pairedWireless) ApplyMachine(sharedStore.MarkPaired(known.key));
            }
            foreach (var service in services.Where(item => !item.Pairing))
            {
                var live = devices.FirstOrDefault(device => device.Ready && service.MatchesTransport(device.Id));
                var saved = live == null ? null : MachineSettingsStore.Find(machine, live.Id);
                var advertised = MachineSettingsStore.FindByMdns(machine, service.Name);
                if (saved != null && !string.IsNullOrEmpty(live.HardwareId) && !saved.pairedWireless) ApplyMachine(sharedStore.MarkPaired(saved.key));
                if (saved == null || string.IsNullOrEmpty(live.HardwareId) ||
                    (advertised != null && advertised.key != saved.key) || saved.mdnsInstances.Any(value =>
                    string.Equals(MachineSettingsStore.NormalizeMdnsInstance(value), MachineSettingsStore.NormalizeMdnsInstance(service.Name), StringComparison.OrdinalIgnoreCase))) continue;
                ApplyMachine(sharedStore.RememberMdns(saved.key, service.Name));
            }
        }

        internal void ReconcileSelectedTransports()
        {
            bool changed = MachineSettingsStore.FollowReadyTransports(local, machine, devices);
            if (!local.requireDeviceSelection && local.selectedDevices.Count == 0 && devices.Where(d => d.Ready).GroupBy(d => MachineSettingsStore.Find(machine, d.Id)?.key ?? d.Id).Count() == 1)
            {
                local.selectedDevices.Add(devices.First(d => d.Ready).Id);
                MachineSettingsStore.ReconcileSelection(local, machine);
                changed = true;
            }
            if (changed) SaveLocal();
        }
        internal async Task Discover(CancellationToken token)
        {
            await RefreshDevices(token);
            SetMessage(services.Count > 0 ? "Wireless services found. Previously paired devices were connected automatically."
                : "No ADB mDNS services were advertised. Automatic random-port discovery is unavailable; this is a known limitation on some Fire OS 8 devices. Use the current IP:port, USB, or legacy ADB port 5555.", MessageType.Warning);
        }

        internal async Task Connect(CancellationToken token, string input)
        {
            string address;
            if (!Adb.TryEndpoint(input, out address)) throw new InvalidOperationException("Enter a host/IP and port, e.g. 192.168.1.20:5555 or [IPv6]:port.");
            ApplyMachine(sharedStore.AddEndpoint(address));
            var result = await RunAdb(adb, token, 15000, new[] { "connect", address });
            token.ThrowIfCancellationRequested();
            await RefreshDevices(token);
            var live = devices.FirstOrDefault(device => device.Ready &&
                (MachineSettingsStore.NormalizeTransport(device.Id) == MachineSettingsStore.NormalizeTransport(address) ||
                 services.Any(service => MachineSettingsStore.NormalizeTransport(service.Endpoint) == MachineSettingsStore.NormalizeTransport(address) &&
                     service.MatchesTransport(device.Id))));
            if (live == null)
                throw new InvalidOperationException("Connection not ready. Approve the prompt on your device, then Refresh.\n" + result.Detail);
            var saved = MachineSettingsStore.Find(machine, live.Id);
            if (saved != null) SelectTransport(saved, live.Id, true);
            SetMessage("Connected: " + address, MessageType.Info);
        }

        internal async Task ReadLogs(CancellationToken token)
        {
            string serial = Selected[0].Id, package = BuildJobs.Package;
            if (!Regex.IsMatch(package, @"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$")) throw new InvalidOperationException("Invalid app identifier.");
            var pid = await RunAdb(adb, token, 10000, new[] { "-s", serial, "shell", "pidof", "-s", package });
            token.ThrowIfCancellationRequested();
            if (!pid.Success || !Regex.IsMatch(pid.Output.Trim(), @"^\d+$")) throw new InvalidOperationException("The app is not running, or this device does not support pidof. Launch the app before reading logs.");
            var result = await RunAdb(adb, token, 15000, new[] { "-s", serial, "logcat", "-d", "--pid=" + pid.Output.Trim(), "-t", "300" });
            token.ThrowIfCancellationRequested();
            if (!result.Success) throw new InvalidOperationException(result.Detail);
            logs = result.Output;
            SetMessage("Captured up to 300 app log lines. Review logs for sensitive information before sharing.", MessageType.Info);
        }

        internal void SavePresets()
        {
            string error = BuildStorage.ValidatePresets(presets);
            if (error.Length > 0) { SetMessage(error, MessageType.Warning); return; }
            try { BuildStorage.Write(BuildStorage.PresetPath, presets); }
            catch (Exception e) { SetMessage(e.Message, MessageType.Error); }
        }
        internal void QueueBuild(bool run)
        {
            // Snapshot user intent before preempting discovery or waiting for the next Editor tick.
            var preset = JsonUtility.FromJson<BuildPreset>(JsonUtility.ToJson(Preset));
            string requestedVersion = version, output;
            string[] copyFolders;
            int requestedCode = code;
            BuildTarget requestedTarget = BuildJobs.Target;
            var targets = Selected.Select(d => d.Id).ToArray();
            if (run && requestedTarget == BuildTarget.Android && targets.Length != local.selectedDevices.Count)
            { SetMessage("Reconnect or deselect unavailable devices before Build & Run.", MessageType.Warning); return; }
            try
            {
                output = BuildStorage.BuildOutputRoot(preset, shared: machine);
                copyFolders = BuildStorage.CopyOutputRoots(output, machine.additionalOutputFolders);
            }
            catch (Exception e) { SetMessage(e.Message, MessageType.Error); return; }
            StartOperation("Preparing build", async token =>
            {
                var nextTick = new TaskCompletionSource<bool>();
                EditorApplication.delayCall += () => nextTick.TrySetResult(true);
                await nextTick.Task;
                token.ThrowIfCancellationRequested();
                if (requestedTarget != BuildJobs.Target) throw new InvalidOperationException("Build target changed. Review settings and start again.");
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) throw new OperationCanceledException();
                var errors = BuildJobs.Validate(preset, requestedVersion, requestedCode, run, targets.Length > 0, adb, output);
                if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
                SaveLocal();
                SetMessage("Building. Unity controls build progress.", MessageType.Info);
                BuildRecord record;
                try { record = BuildJobs.Build(preset, requestedVersion, requestedCode, run && requestedTarget != BuildTarget.Android, output, copyFolders); }
                finally { LoadLocal(); ReadVersion(); }
                if (record.result != "Succeeded") throw new InvalidOperationException("Build " + record.result + ". Settings were restored. See Unity Console.");
                SetMessage("Build succeeded: " + record.path + (run && !record.IsApk ? "\n" + record.deployment : "") +
                    (record.copiedLocations.Count > 0 ? "\nCopied to " + record.copiedLocations.Count + " additional location(s)." : "") +
                    (record.copyErrors.Count > 0 ? "\nCopy failed:\n" + string.Join("\n", record.copyErrors) : ""),
                    record.copyErrors.Count > 0 ? MessageType.Warning : MessageType.Info);
                SaveLocal();
                if (machine.revealBuild) EditorUtility.RevealInFinder(record.path);
                if (run && record.IsApk) await Deploy(record, targets, token);
            });
        }

        internal void Install(BuildRecord record)
        {
            if (record == null || !record.IsApk) return;
            var targets = Selected.Select(d => d.Id).ToArray();
            if (targets.Length == 0) { SetMessage("Select a ready device.", MessageType.Warning); return; }
            StartOperation("Checking saved APK", token => Deploy(record, targets, token));
        }

        private async Task Deploy(BuildRecord record, string[] targets, CancellationToken token)
        {
            local.activeDeployment = record.id;
            record.deployment = "Checking APK; deployment requested for " + string.Join(", ", targets);
            SaveDeployment(record);
            var outcomes = new List<string>();
            try
            {
                string actual = await Task.Run(() => BuildStorage.HashFile(record.path), token);
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(record.sha256) || actual != record.sha256) throw new InvalidOperationException("APK changed or has no recorded checksum. Build again before deploying.");
                foreach (string serial in targets)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        await Adb.Deploy(adb, serial, record.path, record.package, record.minimumSdk, record.abis, token,
                            stage => { SetMessage(stage, MessageType.Info); record.deployment = string.Join("\n", outcomes) + "\n" + serial + ": " + stage; SaveDeployment(record); });
                        outcomes.Add(serial + ": installed and launched");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { outcomes.Add(serial + ": " + e.Message); }
                    record.deployment = string.Join("\n", outcomes);
                    SaveDeployment(record);
                }
                var copyErrors = record.copyErrors ?? new List<string>();
                SetMessage("Build succeeded. Deployment results:\n" + string.Join("\n", outcomes) +
                    (copyErrors.Count > 0 ? "\nCopy failed:\n" + string.Join("\n", copyErrors) : ""),
                    copyErrors.Count == 0 && outcomes.All(o => o.EndsWith(": installed and launched", StringComparison.Ordinal)) ? MessageType.Info : MessageType.Warning);
            }
            catch (OperationCanceledException) { outcomes.Add("Deployment interrupted. No automatic retry; check the device before retrying."); throw; }
            catch (Exception e) { outcomes.Add(e.Message); throw; }
            finally
            {
                record.deployment = string.Join("\n", outcomes);
                local.activeDeployment = "";
                SaveDeployment(record);
            }
        }
        private void SaveDeployment(BuildRecord record)
        {
            var stored = local.history.FirstOrDefault(h => h.id == record.id);
            if (stored != null) stored.deployment = record.deployment;
            SaveLocal(); Repaint();
        }

        internal void Pair(string address, string secret)
        {
            StartOperation("Pairing device", async token =>
            {
                AdbResult result;
                try { result = await RunAdb(adb, token, 30000, new[] { "pair", address, secret }); }
                finally { secret = ""; }
                token.ThrowIfCancellationRequested();
                if (!result.Success || !result.Output.Contains("Successfully paired")) throw new InvalidOperationException("Pairing failed. Check the current pairing port/code and network.");
                wirelessReconnect.Clear();
                await RefreshDevices(token, false);
                SetMessage("Pairing succeeded. Advertised connections reconnect automatically; select the device when ready.", MessageType.Info);
            });
        }
    }
}
