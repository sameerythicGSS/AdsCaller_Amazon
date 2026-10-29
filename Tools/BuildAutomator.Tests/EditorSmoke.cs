using System;
using System.IO;
using System.Linq;
using OutBuild;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// Copied only into disposable verification projects, never into the product assembly.
public sealed class BuildAutomatorIntentionalFailure : IPreprocessBuildWithReport
{
    public static bool FailNext;
    public int callbackOrder { get { return -1000; } }
    public void OnPreprocessBuild(BuildReport report)
    {
        if (FailNext) throw new BuildFailedException("BUILD_AUTOMATOR_EXPECTED_TEST_FAILURE");
    }
}

public static class BuildAutomatorEditorSmoke
{
    private static int checks;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("SMOKE FAIL: " + label);
        checks++;
        Debug.Log("SMOKE PASS: " + label);
    }

    public static void RunWirelessIdentityChecks()
    {
        if (!BuildStorage.Root.Contains(".buildautomator-verification"))
            throw new InvalidOperationException("Run wireless checks only in an isolated verification project.");
        checks = 0;
        string fixture = Path.Combine(BuildStorage.Root, "WirelessTests", Guid.NewGuid().ToString("N"));
        var store = new MachineSettingsStore(Path.Combine(fixture, "settings.json"));
        var live = new Device { Id = "192.168.1.20:39001", State = "device", Transport = "Wi-Fi", HardwareId = "SERIAL-1234", Name = "Phone" };
        var state = store.Observe(new[] { live });
        var service = new WirelessService { Name = "random-phone-instance", Endpoint = live.Id, ServiceType = "_adb-tls-connect._tcp" };
        Check(MachineSettingsStore.FindByMdns(state, service.Name) == null, "reproduce manually connected device with an unknown mDNS identity");
        var window = ScriptableObject.CreateInstance<BuildDashboard>();
        var controller = BuildController.Instance;
        var type = typeof(BuildController);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        Action<string, object> set = (name, value) => type.GetField(name, flags).SetValue(controller, value);
        Action remember = () => type.GetMethod("RememberDiscoveredMdnsIdentities", flags).Invoke(controller, null);
        var fields = new[] { "local", "sharedStore", "machine", "devices", "services" }.ToDictionary(n => n, n => type.GetField(n, flags).GetValue(controller));
        try
        {
            set("local", new LocalState());
            set("sharedStore", store);
            set("machine", state);
            set("devices", new System.Collections.Generic.List<Device> { live });
            set("services", new System.Collections.Generic.List<WirelessService> { service });
            remember();
            state = store.Read();
            Check(MachineSettingsStore.FindByMdns(state, service.Name)?.hardwareId == live.HardwareId,
                "learn previously unknown service from a verified live endpoint");
            service.Endpoint = "192.168.1.20:49002";
            Check(MachineSettingsStore.FindByMdns(state, service.Name)?.hardwareId == live.HardwareId,
                "remembered identity survives wireless port changes");
            var targets = WirelessReconnect.Targets(new[] { service }, state.devices.Single().aliases);
            Check(targets[0] == service.ConnectTarget && targets[1] == service.Endpoint,
                "stable advertised identity wins and the new endpoint remains a fallback");
            service.Endpoint = live.Id;
            service.Name = "unverified-instance";
            live.HardwareId = "";
            remember();
            Check(MachineSettingsStore.FindByMdns(store.Read(), service.Name) == null,
                "unknown hardware cannot attach an advertised identity to a saved device");
            live.HardwareId = "SERIAL-1234";
            var other = new Device { Id = "192.168.1.30:39003", HardwareId = "OTHER-5678", Name = "Other" };
            state = store.Observe(new[] { other });
            state = store.RememberMdns(MachineSettingsStore.Find(state, other.Id).key, "other-instance");
            set("machine", state);
            service.Name = "other-instance";
            remember();
            Check(MachineSettingsStore.FindByMdns(store.Read(), service.Name)?.hardwareId == other.HardwareId,
                "conflicting advertised identity is never stolen from another device");
            Check(type.GetField("presets", flags).GetValue(controller) is PresetFile, "dashboard initializes normally in the Editor");
        }
        finally { foreach (var field in fields) set(field.Key, field.Value); UnityEngine.Object.DestroyImmediate(window); }
        RunMachineSettingsChecks();
        File.WriteAllText(Path.Combine(BuildStorage.Root, "wireless-result.txt"), "PASS: wireless identity checks and shared-settings regressions; " + Application.unityVersion);
        Debug.Log("BUILD_AUTOMATOR_WIRELESS_COMPLETE");
    }

    public static void RunKeystoreChecks()
    {
        if (!BuildStorage.Root.Contains(".buildautomator-verification"))
            throw new InvalidOperationException("Run these checks only in an isolated verification project.");
        string fixture = Path.Combine(BuildStorage.Root, "Keystore Fixtures");
        Directory.CreateDirectory(fixture);
        string projectKey = Path.Combine(fixture, "project signing.keystore");
        string dedicatedKey = Path.Combine(fixture, "dedicated signing.keystore");
        File.WriteAllText(projectKey, "Path validation fixture, not a real signing key.");
        File.WriteAllText(dedicatedKey, "Path validation fixture, not a real signing key.");
        Check(BuildStorage.InspectKeystorePath("", "").Error.Length > 0, "missing keystore selection blocked");
        Check(BuildStorage.InspectKeystorePath(projectKey, "").Error.Length == 0, "absolute keystore with spaces accepted");
        Check(BuildStorage.InspectKeystorePath(projectKey + ".missing", "").Error.Length > 0, "missing absolute keystore blocked");
        string display = Path.GetFileName(dedicatedKey);
        Check(!File.Exists(Path.Combine(BuildStorage.Root, display)), "reproduce old dedicated-folder false negative");
        var dedicated = BuildStorage.InspectKeystorePath(display, dedicatedKey);
        Check(dedicated.Error.Length == 0 && dedicated.ResolvedPath == dedicatedKey, "Unity-resolved dedicated-folder path accepted");
        Check(BuildStorage.InspectKeystorePath(projectKey, dedicatedKey + ".missing").Error.Length > 0, "Unity-resolved path takes precedence over a different existing file");
        var unresolved = BuildStorage.InspectKeystorePath(display, "");
        Check(unresolved.Error.Length == 0 && unresolved.Note.Length > 0 && unresolved.ResolvedPath.Length == 0, "ambiguous relative path is not falsely rejected if native API unavailable");
        bool oldCustom = PlayerSettings.Android.useCustomKeystore;
        string oldKey = PlayerSettings.Android.keystoreName;
        try
        {
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = projectKey;
            var actual = BuildStorage.InspectKeystore();
            Check(!Path.IsPathRooted(PlayerSettings.Android.keystoreName), "Unity exposes an in-project key as a relative display path");
            Check(actual.Error.Length == 0 && actual.Note.Length == 0 && actual.ResolvedPath == projectKey, "native Unity keystore resolver verified");
            PlayerSettings.Android.keystoreName = projectKey + ".missing";
            actual = BuildStorage.InspectKeystore();
            Check(actual.Error.Length > 0 && actual.ResolvedPath.EndsWith(".missing"), "native resolver still blocks genuinely missing keystores");
            PlayerSettings.Android.useCustomKeystore = false;
            Check(BuildStorage.InspectKeystore().Error.Length == 0, "debug signing needs no custom keystore");
        }
        finally
        {
            PlayerSettings.Android.keystoreName = oldKey;
            PlayerSettings.Android.useCustomKeystore = oldCustom;
        }
        File.WriteAllText(Path.Combine(BuildStorage.Root, "keystore-result.txt"), "PASS: " + checks + " keystore checks; " + Application.unityVersion);
        Debug.Log("BUILD_AUTOMATOR_KEYSTORE_COMPLETE: " + checks + " checks");
    }
    public static void RunMachineSettingsChecks()
    {
        if (!BuildStorage.Root.Contains(".buildautomator-verification"))
            throw new InvalidOperationException("Run shared-settings tests only in an isolated verification project.");
        checks = 0;
        string fixture = Path.Combine(BuildStorage.Root, "SharedSettingsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        string oldTools = Path.Combine(fixture, "old", "platform-tools");
        string newTools = Path.Combine(fixture, "new", "platform-tools");
        Directory.CreateDirectory(oldTools);
        Directory.CreateDirectory(newTools);
        string executable = Application.platform == RuntimePlatform.WindowsEditor ? "adb.exe" : "adb";
        File.WriteAllText(Path.Combine(oldTools, executable), "fixture");
        File.WriteAllText(Path.Combine(newTools, executable), "fixture");
        File.WriteAllText(Path.Combine(oldTools, "source.properties"), "Pkg.Revision=32.0.0\n");
        File.WriteAllText(Path.Combine(newTools, "source.properties"), "Pkg.Revision=36.0.0\n");
        Check(BuildStorage.SelectNewestAdb(new[] { Path.Combine(oldTools, executable), Path.Combine(newTools, executable) }) == Path.Combine(newTools, executable),
            "all Editors select the newest installed ADB server version");
        string path = Path.Combine(fixture, "settings.json");
        var firstEditor = new MachineSettingsStore(path);
        var secondEditor = new MachineSettingsStore(path);
        var legacy = new LocalState { adbOverride = "custom-adb", autoRefresh = false, revealBuild = false };
        legacy.devices.Add(new SavedDevice { id = " TV.local:05555 ", nickname = "Office TV", model = "Fire TV" });
        legacy.devices.Add(new SavedDevice { id = "tv.local:5555", model = "Fire TV" });
        var state = firstEditor.Migrate("project-A", legacy);
        Check(state.devices.Count == 1 && state.devices[0].nickname == "Office TV", "legacy duplicate endpoints merge and preserve nickname");
        Check(state.adbOverride == "custom-adb" && !state.autoRefresh && !state.revealBuild, "legacy machine preferences migrate once");
        state = secondEditor.Migrate("project-A", legacy);
        Check(state.devices.Count == 1 && state.migratedProjects.Count == 1, "migration is idempotent");
        state = secondEditor.Migrate("project-B", legacy);
        Check(state.devices.Count == 1, "same device from another project is not duplicated");
        firstEditor.SetAdbOverride("new-adb");
        secondEditor.Migrate("project-C", legacy);
        Check(firstEditor.Read().adbOverride == "new-adb", "later migration cannot overwrite a shared preference");
        string sharedFolder = Path.Combine(fixture, "Shared Builds With Spaces");
        firstEditor.SetOutputFolder(sharedFolder);
        secondEditor.SetAutoRefresh(true);
        Check(firstEditor.Read().outputFolder == sharedFolder && secondEditor.Read().autoRefresh, "independent Editor changes preserve each other's fields");
        var preset = new BuildPreset { outputFolder = "ProjectBuilds" };
        state = secondEditor.Read();
        Check(BuildStorage.BuildOutputRoot(preset, shared: state) == sharedFolder, "shared output wins over project default");
        preset.overrideMachineOutput = true;
        Check(BuildStorage.BuildOutputRoot(preset, shared: state) == BuildStorage.OutputRoot("ProjectBuilds"), "explicit project override is respected");
        string ciFolder = Path.Combine(fixture, "CI Builds");
        Check(BuildStorage.BuildOutputRoot(preset, ciFolder, state) == ciFolder && firstEditor.Read().outputFolder == sharedFolder, "CI output wins without changing shared settings");
        try { firstEditor.SetOutputFolder("relative/path"); throw new Exception("Relative shared folder accepted"); }
        catch (ArgumentException) { Check(true, "relative shared folder is rejected"); }
        firstEditor.SetOutputFolder("");
        preset.overrideMachineOutput = false;
        Check(BuildStorage.BuildOutputRoot(preset, shared: firstEditor.Read()) == BuildStorage.OutputRoot("ProjectBuilds"), "clearing shared output restores project fallback");
        firstEditor.SetOutputFolder(sharedFolder);
        state = firstEditor.AddEndpoint("TV.LOCAL:5555");
        Check(state.devices.Count == 1, "manual add is duplicate-aware");
        state = firstEditor.Observe(new[] { new Device { Id = "tv.local:5555", HardwareId = "TV-SERIAL-42", Name = "Fire TV" } });
        state = secondEditor.Observe(new[] { new Device { Id = "TV-SERIAL-42", HardwareId = "TV-SERIAL-42", Name = "Fire TV" } });
        Check(state.devices.Count == 1 && state.devices[0].aliases.Count == 2 && state.devices[0].nickname == "Office TV", "USB and wireless identities merge into one named device");
        state = firstEditor.Observe(new[] { new Device { Id = "192.168.1.8:39001", HardwareId = "TV-SERIAL-42", Name = "Fire TV" } });
        state = firstEditor.Observe(new[] { new Device { Id = "192.168.1.8:39002", HardwareId = "TV-SERIAL-42", Name = "Fire TV" } });
        Check(state.devices.Count == 1 && MachineSettingsStore.Find(state, "192.168.1.8:39002").nickname == "Office TV", "new IP/port is an alias of the verified device");
        var mdnsDevice = MachineSettingsStore.FindByMdns(state, "adb-TV-SERIAL-42-random");
        Check(mdnsDevice != null && mdnsDevice.hardwareId == "TV-SERIAL-42", "mDNS advertisement matches a saved hardware identity without using its changing endpoint");
        state = firstEditor.RememberMdns(mdnsDevice.key, "adb-persistent-guid");
        Check(MachineSettingsStore.FindByMdns(state, "adb-persistent-guid._adb-tls-connect._tcp").key == mdnsDevice.key, "verified mDNS identity persists across refreshes");
        var following = new LocalState();
        following.selectedDevices.Add("192.168.1.8:39001");
        MachineSettingsStore.ReconcileSelection(following, state);
        state = firstEditor.Observe(new[] { new Device { Id = "192.168.1.8:39003", HardwareId = "TV-SERIAL-42", Name = "Fire TV" } });
        Check(MachineSettingsStore.FollowReadyTransports(following, state,
            new[] { new Device { Id = "192.168.1.8:39003", HardwareId = "TV-SERIAL-42", Name = "Fire TV", State = "device", Transport = "Wi-Fi" } }) &&
            following.selectedDevices.Single() == "192.168.1.8:39003", "saved target selection follows the verified device to its new IP and port");
        var selection = new LocalState();
        selection.selectedDevices.Add("192.168.1.8:39002");
        Check(MachineSettingsStore.ReconcileSelection(selection, state) && selection.selectionIdentities[0].hardwareId == "TV-SERIAL-42", "selection remembers the verified device identity");
        state = secondEditor.Observe(new[] { new Device { Id = "192.168.1.8:39002", HardwareId = "OTHER-SERIAL-99", Name = "Fire TV" } });
        MachineSettingsStore.ReconcileSelection(selection, state);
        Check(selection.selectedDevices.Count == 0 && selection.requireDeviceSelection, "reassigned address requires explicit device reselection");
        Check(state.devices.Count == 2 && MachineSettingsStore.Find(state, "192.168.1.8:39002").hardwareId == "OTHER-SERIAL-99", "reassigned IP cannot merge conflicting identities");
        Check(state.devices.First(d => d.hardwareId == "TV-SERIAL-42").nickname == "Office TV", "reassigned IP does not steal old device nickname");
        state = firstEditor.Observe(new[] { new Device { Id = "USB-OTHER", Name = "Fire TV" } });
        Check(state.devices.Count == 3, "matching model names alone are never merged");
        Check(MachineSettingsStore.ValidHardwareId("unknown") == "" && MachineSettingsStore.ValidHardwareId("000000") == "", "placeholder serials do not identify hardware");
        Check(MachineSettingsStore.NormalizeTransport("[FE80:0:0:0:0:0:0:1]:05555") == "[fe80::1]:5555", "IPv6 and numeric ports normalize");
        Check(MachineSettingsStore.NormalizeTransport("USBabc") != MachineSettingsStore.NormalizeTransport("USBABC"), "USB serial case is preserved");
        var toForget = state.devices.First(d => d.hardwareId == "TV-SERIAL-42");
        selection.selectedDevices.Add("TV-SERIAL-42");
        MachineSettingsStore.ReconcileSelection(selection, state);
        firstEditor.Forget(toForget.key);
        MachineSettingsStore.ReconcileSelection(selection, secondEditor.Read());
        Check(selection.selectedDevices.Count == 0, "forgetting a device in another project clears its stale selection");
        state = secondEditor.Migrate("not-opened-before", legacy);
        Check(state.devices.All(d => d.nickname != "Office TV"), "forgotten devices are not resurrected by stale project migration");
        state = secondEditor.AddEndpoint("tv.local:5555");
        Check(MachineSettingsStore.Find(firstEditor.Read(), "tv.local:5555") != null, "explicitly adding a forgotten device works");
        string renameKey = state.devices.First(d => d.id == "USB-OTHER").key;
        firstEditor.Rename(renameKey, "QA tablet");
        Check(secondEditor.Read().devices.First(d => d.key == renameKey).nickname == "QA tablet", "renames propagate across Editor instances");
        using (var locked = new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            try { firstEditor.SetRevealBuild(true); throw new Exception("Concurrent lock was ignored"); }
            catch (IOException) { Check(true, "busy interprocess lock fails safely without overwriting settings"); }
        }
        Check(!firstEditor.Read().revealBuild, "failed write preserves prior shared value");
        var writers = Enumerable.Range(0, 8).Select(i => System.Threading.Tasks.Task.Run(() => new MachineSettingsStore(path).AddEndpoint("10.0.0." + (i + 1) + ":5555"))).ToArray();
        System.Threading.Tasks.Task.WaitAll(writers);
        Check(firstEditor.Read().devices.Count(d => d.id.StartsWith("10.0.0.")) == 8, "concurrent writers retain all added devices");
        Check(File.Exists(path + ".bak"), "shared settings have an atomic-write backup");
        string corruptPath = Path.Combine(fixture, "corrupt.json");
        File.WriteAllText(corruptPath, "not valid json");
        try { new MachineSettingsStore(corruptPath).AddEndpoint("10.0.0.20"); throw new Exception("Corrupted settings overwritten"); }
        catch (IOException) { Check(File.ReadAllText(corruptPath) == "not valid json", "corrupted settings are never silently overwritten"); }
        File.WriteAllText(Path.Combine(BuildStorage.Root, "machine-settings-result.txt"), "PASS: " + checks + " shared-settings checks; " + Application.unityVersion);
        Debug.Log("BUILD_AUTOMATOR_MACHINE_SETTINGS_COMPLETE: " + checks + " checks");
    }
    public static void RunSharedAndBuildChecks()
    {
        RunMachineSettingsChecks();
        checks = 0;
        Run();
    }
    public static void Run()
    {
        var preset = new BuildPreset { name = "Smoke", development = false, outputFolder = "SmokeBuilds" };
        string originalVersion = PlayerSettings.bundleVersion;
        EditorBuildSettings.scenes = new EditorBuildSettingsScene[0];
        Check(BuildJobs.Validate(preset, "1.2.3", 1, false, false, "").Any(e => e.Contains("scene")), "empty scenes blocked");
        try { BuildJobs.Build(preset, "1.2.3", 1, false); throw new Exception("Missing preflight failure"); }
        catch (BuildFailedException) { Check(PlayerSettings.bundleVersion == originalVersion, "preflight does not mutate version"); }
        foreach (string forbidden in new[] { "Assets/Builds", "Library/Builds", "Packages", "ProjectSettings", "Assets/../Assets/Builds" })
        {
            try { BuildStorage.OutputRoot(forbidden); throw new Exception("Unsafe output was accepted"); }
            catch (InvalidOperationException) { Check(true, "output protected: " + forbidden); }
        }
        Check(BuildStorage.SafeName("com.example/test:v1") == "com.example_test_v1", "safe output filename");
        Check(BuildStorage.ValidatePresets(new PresetFile()) == "", "default presets valid");
        var bad = new PresetFile();
        bad.presets[1].name = bad.presets[0].name;
        Check(BuildStorage.ValidatePresets(bad).Length > 0, "duplicate preset names rejected");
        var state = new LocalState();
        state.autoRefresh = false;
        state.devices.Add(new SavedDevice { id = "USB-test", nickname = "QA phone" });
        BuildStorage.Write(BuildStorage.LocalPath, state);
        state.devices[0].nickname = "QA phone renamed";
        BuildStorage.Write(BuildStorage.LocalPath, state);
        Check(BuildStorage.Read<LocalState>(BuildStorage.LocalPath).devices[0].nickname == "QA phone renamed" && File.Exists(BuildStorage.LocalPath + ".bak"), "local settings atomic save and backup");
        BuildDashboard.Open();
        var window = Resources.FindObjectsOfTypeAll<BuildDashboard>().First();
        window.Close();
        Check(Resources.FindObjectsOfTypeAll<BuildDashboard>().Length == 0, "standalone dashboard opens and closes");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Smoke.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Smoke.unity", true) };
        bool development = EditorUserBuildSettings.development;
        string machineOutput = Path.Combine(BuildStorage.Root, "Machine Output With Spaces");
        BuildStorage.MachineStore.SetOutputFolder(machineOutput);
        bool reportOutputExisted = false, publishedOutputExisted = false;
        Action<BuildReport> onReport = report => reportOutputExisted = File.Exists(report.summary.outputPath);
        Action<BuildRecord> onPublished = published => publishedOutputExisted = File.Exists(published.path) && published.sha256 == BuildStorage.HashFile(published.path);
        BuildJobs.BuildCompleted += onReport; BuildJobs.ArtifactPublished += onPublished;
        BuildRecord record;
        try { record = BuildJobs.Build(preset, "1.2.3", 1, false); }
        finally { BuildJobs.BuildCompleted -= onReport; BuildJobs.ArtifactPublished -= onPublished; }
        Check(reportOutputExisted && publishedOutputExisted, "report and publication callbacks receive usable output paths");
        Check(record.path.StartsWith(machineOutput + Path.DirectorySeparatorChar), "actual player build uses the shared output folder");
        Check(record.result == "Succeeded" && File.Exists(record.path), "actual player build succeeds");
        Check(PlayerSettings.bundleVersion == "1.2.3" && EditorUserBuildSettings.development == development, "success retains version and restores development option");
        Check(Path.GetDirectoryName(record.path) == machineOutput, "artifact is written directly to the selected folder");
        Check(File.Exists(record.path + ".build-manifest.json"), "build manifest created beside the artifact");
        Check(record.sha256.Length == 64 && record.sha256 == BuildStorage.HashFile(record.path), "artifact checksum recorded");
        string artifactHash = record.sha256;
        string manifest = File.ReadAllText(record.path + ".build-manifest.json");
        preset.namingTemplate = Path.GetFileNameWithoutExtension(record.path);
        BuildAutomatorIntentionalFailure.FailNext = true;
        BuildRecord failed = null;
        try { failed = BuildJobs.Build(preset, "9.9.9", 99, false); }
        catch (BuildFailedException) { }
        finally { BuildAutomatorIntentionalFailure.FailNext = false; }
        Check(failed == null || failed.result != "Succeeded", "intentional build failure is not successful");
        Check(PlayerSettings.bundleVersion == "1.2.3" && EditorUserBuildSettings.development == development, "failed build restores version and temporary options");
        Check(File.Exists(record.path) && BuildStorage.HashFile(record.path) == artifactHash, "failed rebuild preserves the previous artifact");
        Check(File.ReadAllText(record.path + ".build-manifest.json") == manifest, "failed rebuild preserves the previous artifact manifest");
        Check(BuildStorage.Read<LocalState>(BuildStorage.LocalPath).history.Count == 2, "success and failure retained in history");
        AssetDatabase.ExportPackage("Assets/Editor/BuildAutomator", Path.Combine(BuildStorage.Root, "BuildAutomator-smoke.unitypackage"), ExportPackageOptions.Recurse);
        Check(File.Exists(Path.Combine(BuildStorage.Root, "BuildAutomator-smoke.unitypackage")), "standalone package export succeeds");
        File.WriteAllText(Path.Combine(BuildStorage.Root, "smoke-result.txt"), "PASS: " + checks + " Unity Editor checks; " + Application.unityVersion);
        Debug.Log("BUILD_AUTOMATOR_SMOKE_COMPLETE: " + checks + " checks");
    }
}
