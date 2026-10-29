using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace OutBuild
{
    [Serializable]
    public sealed class BuildPreset
    {
        public string name = "Local Test";
        public bool development = true, appBundle, incrementVersionCode, overrideMachineOutput;
        public string outputFolder = "Builds";
        public string namingTemplate = "{package}_v{version}_{code}";
        public WebBuildSettings web = new WebBuildSettings();
    }

    [Serializable]
    public sealed class PresetFile
    {
        public int schemaVersion = 1;
        public string defaultPreset = "Release";
        public List<BuildPreset> presets = new List<BuildPreset>
        {
            new BuildPreset(),
            new BuildPreset { name = "QA", development = true, incrementVersionCode = true },
            new BuildPreset { name = "Release", development = false, incrementVersionCode = true }
        };
    }

    [Serializable]
    public sealed class SavedDevice
    {
        public bool pairedWireless;
        public string id = "", key = "", hardwareId = "", nickname = "", model = "", lastSeenUtc = "";
        public List<string> aliases = new List<string>();
        public List<string> mdnsInstances = new List<string>();
    }

    [Serializable]
    public sealed class BuildRecord
    {
        public string id = "", utc = "", path = "", package = "", version = "", target = "", preset = "", result = "", deployment = "Not requested", sha256 = "";
        public int code, minimumSdk;
        public string[] abis = new string[0];
        public double seconds;
        public long bytes;
        public WebBuildSettings web;
        public bool IsApk { get { return result == "Succeeded" && string.Equals(Path.GetExtension(path), ".apk", StringComparison.OrdinalIgnoreCase); } }
    }

    [Serializable]
    public sealed class DeviceSelection
    {
        public string transport = "", hardwareId = "";
    }

    [Serializable]
    public sealed class LocalState
    {
        public int schemaVersion = 1, presetIndex;
        public bool presetChosen, statusWarning;
        public string selectedPreset = "", activeDeployment = "", statusMessage = "";
        public string adbOverride = "";
        public bool autoRefresh = true, revealBuild = true;
        public List<string> selectedDevices = new List<string>();
        public bool requireDeviceSelection;
        public List<DeviceSelection> selectionIdentities = new List<DeviceSelection>();
        public List<SavedDevice> devices = new List<SavedDevice>();
        public List<BuildRecord> history = new List<BuildRecord>();
    }

    public sealed class KeystoreStatus
    {
        public string ConfiguredPath = "", ResolvedPath = "", Error = "", Note = "";
    }

    public static class BuildStorage
    {
        private static readonly MethodInfo KeystoreFullPath = typeof(PlayerSettings.Android).GetMethod(
            "GetAndroidKeystoreFullPath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null);

        public static KeystoreStatus InspectKeystore()
        {
            if (!PlayerSettings.Android.useCustomKeystore) return new KeystoreStatus();
            string resolved = "";
            // This read-only Unity helper uses the same effective Player Settings as the build.
            // The displayed keystoreName alone cannot distinguish project and dedicated locations.
            try { resolved = KeystoreFullPath?.Invoke(null, null) as string ?? ""; }
            catch (Exception) { /* Internal API may change: use the non-blocking fallback below. */ }
            return InspectKeystorePath(PlayerSettings.Android.keystoreName, resolved);
        }

        public static KeystoreStatus InspectKeystorePath(string configuredPath, string unityResolvedPath)
        {
            var status = new KeystoreStatus { ConfiguredPath = configuredPath ?? "" };
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                status.Error = "Select a custom keystore in the active Player Settings / Build Profile > Publishing Settings.";
                return status;
            }
            try
            {
                // Prefer Unity's authoritative path, even if a same-named file exists in the project.
                if (!string.IsNullOrEmpty(unityResolvedPath) && Path.IsPathRooted(unityResolvedPath))
                    status.ResolvedPath = Path.GetFullPath(unityResolvedPath);
                else if (Path.IsPathRooted(configuredPath))
                    status.ResolvedPath = Path.GetFullPath(configuredPath);
                else
                    status.Note = "Unity's relative keystore path could not be resolved for preflight. Unity will validate it when building; no project-relative path was guessed.";
                if (!string.IsNullOrEmpty(status.ResolvedPath) && !File.Exists(status.ResolvedPath))
                    status.Error = "The active signing keystore could not be found or accessed:\n" + status.ResolvedPath +
                        "\nCheck Publishing Settings in the active Player Settings / Build Profile.";
            }
            catch (Exception)
            {
                status.ResolvedPath = "";
                status.Note = "Keystore path could not be checked in preflight. Unity will validate it when building.";
            }
            return status;
        }
        public static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        public static string PresetPath { get { return Path.Combine(Root, "ProjectSettings", "OutBuildAutomator.json"); } }
        public static string LocalPath { get { return Path.Combine(Root, "UserSettings", "OutBuildAutomator.json"); } }
        public static MachineSettingsStore MachineStore { get { return new MachineSettingsStore(MachineSettingsStore.DefaultPath); } }

        public static string BuildOutputRoot(BuildPreset preset, string explicitOverride = null, MachineSettings shared = null)
        {
            if (!string.IsNullOrWhiteSpace(explicitOverride)) return OutputRoot(explicitOverride);
            if (preset.overrideMachineOutput) return OutputRoot(preset.outputFolder);
            shared = shared ?? MachineStore.Read();
            return OutputRoot(string.IsNullOrWhiteSpace(shared.outputFolder) ? preset.outputFolder : shared.outputFolder);
        }

        public static T Read<T>(string path) where T : new()
        {
            try { return File.Exists(path) ? JsonUtility.FromJson<T>(File.ReadAllText(path)) : new T(); }
            catch (Exception e)
            {
                Debug.LogWarning("[Build Automator] Could not read " + path + " (" + e.GetType().Name + "). Defaults loaded; original file was not deleted.");
                return new T();
            }
        }

        public static void Write(string path, object value)
        {
            path = FilePath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(value, true));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }

        public static string ValidatePresets(PresetFile file)
        {
            if (file == null || file.schemaVersion != 1 || file.presets == null || file.presets.Count == 0 || file.presets.Count > 30)
                return "Unsupported or empty preset file.";
            if (file.presets.Any(p => p == null || string.IsNullOrWhiteSpace(p.name) || string.IsNullOrWhiteSpace(p.outputFolder) || string.IsNullOrWhiteSpace(p.namingTemplate)))
                return "Each preset needs a name, output folder, and naming template.";
            if (file.presets.Select(p => p.name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != file.presets.Count)
                return "Preset names must be unique.";
            return "";
        }

        public static string FilePath(string path)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor) return path;
            string full = Path.GetFullPath(path).Replace('/', '\\');
            if (full.StartsWith(@"\\?\")) return full;
            return full.StartsWith(@"\\") ? @"\\?\UNC\" + full.Substring(2) : @"\\?\" + full;
        }

        public static string HashFile(string path)
        {
            using (var stream = File.OpenRead(FilePath(path)))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        public static string FindAdb(string manualPath)
        {
            if (!string.IsNullOrWhiteSpace(manualPath)) return File.Exists(manualPath) ? Path.GetFullPath(manualPath) : "";
            var roots = new List<string>();
            // Reflection allows the tool to import even when Unity's Android module is absent.
            try
            {
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.Android.AndroidExternalToolsSettings")).FirstOrDefault(t => t != null);
                if (type != null) roots.Add(type.GetProperty("sdkRootPath", BindingFlags.Static | BindingFlags.Public)?.GetValue(null, null) as string);
            }
            catch { /* Continue to installed SDK/environment fallbacks. */ }
            roots.Add(EditorPrefs.GetString("AndroidSdkRoot", ""));
            roots.Add(Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer", "SDK"));
            roots.Add(Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"));
            roots.Add(Environment.GetEnvironmentVariable("ANDROID_HOME"));
            string executable = Application.platform == RuntimePlatform.WindowsEditor ? "adb.exe" : "adb";
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                try
                {
                    var contents = new DirectoryInfo(EditorApplication.applicationContentsPath);
                    var hubDirectory = contents.Parent == null || contents.Parent.Parent == null
                        ? null : contents.Parent.Parent.Parent;
                    string hub = hubDirectory != null && string.Equals(hubDirectory.Name, "Editor", StringComparison.OrdinalIgnoreCase)
                        ? hubDirectory.FullName
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor");
                    if (Directory.Exists(hub))
                        roots.AddRange(Directory.GetDirectories(hub).Select(version =>
                            Path.Combine(version, "Editor", "Data", "PlaybackEngines", "AndroidPlayer", "SDK")));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            var candidates = roots.Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(root => Path.Combine(root, "platform-tools", executable)).Where(File.Exists).ToList();
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(directory)) continue;
                    string path = Path.Combine(directory.Trim('"'), executable);
                    if (File.Exists(path)) candidates.Add(path);
                }
                catch (ArgumentException) { }
            }
            return SelectNewestAdb(candidates);
        }

        public static string SelectNewestAdb(IEnumerable<string> candidates)
        {
            return candidates.Where(File.Exists).Select((path, index) => new
                {
                    path = Path.GetFullPath(path), index, revision = AdbRevision(path)
                }).GroupBy(item => item.path, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
                .OrderByDescending(item => item.revision).ThenBy(item => item.index)
                .Select(item => item.path).FirstOrDefault() ?? "";
        }

        private static Version AdbRevision(string executable)
        {
            try
            {
                string metadata = Path.Combine(Path.GetDirectoryName(executable), "source.properties");
                string line = File.ReadLines(metadata).FirstOrDefault(value => value.TrimStart().StartsWith("Pkg.Revision=", StringComparison.OrdinalIgnoreCase));
                Version version;
                return line != null && Version.TryParse(line.Substring(line.IndexOf('=') + 1).Trim(), out version) ? version : new Version(0, 0);
            }
            catch (IOException) { return new Version(0, 0); }
            catch (UnauthorizedAccessException) { return new Version(0, 0); }
        }

        public static string SafeName(string name)
        {
            string invalid = new string(Path.GetInvalidFileNameChars()) + "<>:\"/\\|?*";
            string result = new string((name ?? "").Select(c => char.IsControl(c) || invalid.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
            return string.IsNullOrWhiteSpace(result) ? "Build" : result.Substring(0, Math.Min(result.Length, 120));
        }

        public static string OutputRoot(string input)
        {
            string path = Path.GetFullPath(Path.IsPathRooted(input) ? input : Path.Combine(Root, input));
            // Output must never pollute source directories or Unity's generated import/cache folders.
            foreach (string folder in new[] { "Assets", "Packages", "ProjectSettings", "UserSettings", "Library", "Temp", ".git", ".agents", ".codex" })
            {
                string forbidden = Path.Combine(Root, folder);
                if (string.Equals(path, forbidden, StringComparison.OrdinalIgnoreCase) || path.StartsWith(forbidden + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Choose a build output folder outside " + folder + ".");
            }
            return path;
        }
    }
}
