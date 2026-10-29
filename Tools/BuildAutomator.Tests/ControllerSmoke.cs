using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OutBuild;
using UnityEditor;
using UnityEngine;

// Runs in the disposable Editor project. All ADB responses are in-memory fixtures.
public static class BuildAutomatorControllerSmoke
{
    private static int checks;
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static BuildController service;
    private static object Get(string name) { return typeof(BuildController).GetField(name, Flags).GetValue(service); }
    private static void Set(string name, object value) { typeof(BuildController).GetField(name, Flags).SetValue(service, value); }
    private static object Call(string name, params object[] args)
    {
        return typeof(BuildController).GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(service, args);
    }
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("CONTROLLER FAIL: " + message);
        checks++; Debug.Log("CONTROLLER PASS: " + message);
    }
    public static async void RunAll()
    {
        try
        {
            if (!BuildStorage.Root.Contains(".buildautomator-verification")) throw new Exception("Disposable project required.");
            BuildStorage.Write(BuildStorage.LocalPath, new LocalState { autoRefresh = false, revealBuild = false });
            BuildStorage.MachineStore.SetAutoRefresh(false);
            BuildStorage.MachineStore.SetRevealBuild(false);
            service = BuildController.Instance;
            await Run();
            File.WriteAllText(Path.Combine(BuildStorage.Root, "controller-result.txt"), "PASS: " + checks + " controller/recovery checks; " + Application.unityVersion);
            BuildAutomatorEditorSmoke.RunWirelessIdentityChecks();
            BuildAutomatorEditorSmoke.RunSharedAndBuildChecks();
            Debug.Log("BUILD_AUTOMATOR_ALL_COMPLETE");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText(Path.Combine(BuildStorage.Root, "controller-result.txt"), "FAIL: " + e);
            EditorApplication.Exit(1);
        }
    }
    private static async Task Run()
    {
        var compact = ScriptableObject.CreateInstance<BuildDashboard>();
        var full = ScriptableObject.CreateInstance<BuildFullControlsDashboard>();
        var property = typeof(BuildDashboardWindow).GetProperty("Controller", Flags);
        Check(ReferenceEquals(property.GetValue(compact), property.GetValue(full)), "compact and full windows observe the same controller");
        Call("ChoosePreset", 0);
        UnityEngine.Object.DestroyImmediate(compact);
        UnityEngine.Object.DestroyImmediate(full);
        compact = ScriptableObject.CreateInstance<BuildDashboard>();
        Check(((LocalState)Get("local")).selectedPreset == "Local Test", "reopening remembers the selected preset");
        var presets = (PresetFile)Get("presets");
        presets.defaultPreset = "QA";
        BuildStorage.Write(BuildStorage.LocalPath, new LocalState());
        Call("LoadLocal");
        Check(((LocalState)Get("local")).selectedPreset == "QA", "first use honors a configured default");
        File.WriteAllText(BuildStorage.LocalPath, "{\"schemaVersion\":1,\"presetIndex\":1}");
        Call("LoadLocal");
        Check(((LocalState)Get("local")).selectedPreset == "QA", "upgrade preserves an existing saved preset choice");
        presets.defaultPreset = "Release";
        BuildStorage.Write(BuildStorage.LocalPath, new LocalState());
        Call("LoadLocal");
        Check(((LocalState)Get("local")).selectedPreset == "Release", "Release remains the default first-use preset");
        var preset = presets.presets[((LocalState)Get("local")).presetIndex];
        preset.overrideMachineOutput = true; preset.outputFolder = "ProfileOutput";
        var machine = (MachineSettings)Get("machine");
        machine.outputFolder = Path.Combine(BuildStorage.Root, "Different shared output");
        Check(BuildStorage.BuildOutputRoot(preset, shared: machine) == BuildStorage.OutputRoot("ProfileOutput"), "shared controller respects the project output override");
        preset.overrideMachineOutput = false;
        Check(BuildStorage.BuildOutputRoot(preset, shared: machine) == machine.outputFolder, "shared output is used when the override is disabled");

        var scheduler = (OperationScheduler)Get("scheduler");
        var release = new TaskCompletionSource<bool>();
        bool cancelled = false;
        Func<CancellationToken, Task> operation = async token =>
        {
            await release.Task;
            cancelled = token.IsCancellationRequested;
            Call("SetMessage", "Completed with both windows closed", MessageType.Info);
            Call("SaveLocal");
        };
        Call("StartOperation", "Fixture operation", operation, false);
        Check(scheduler.ForegroundBusy, "foreground operation is owned by controller");
        UnityEngine.Object.DestroyImmediate(compact);
        release.SetResult(true);
        for (int i = 0; i < 100 && scheduler.Busy; i++) await Task.Delay(10);
        Check(!cancelled && !scheduler.Busy, "closing the last window does not cancel its operation");
        Check(BuildStorage.Read<LocalState>(BuildStorage.LocalPath).statusMessage == "Completed with both windows closed", "completion persists with no open window");

        var originalRunner = Get("RunAdb");
        var originalStore = Get("sharedStore");
        var originalMachine = Get("machine");
        var originalLocal = Get("local");
        var fixtureStore = new MachineSettingsStore(Path.Combine(BuildStorage.Root, "ControllerDevices", "settings.json"));
        Set("sharedStore", fixtureStore);
        Set("machine", fixtureStore.Read());
        Set("local", new LocalState { autoRefresh = false });
        Set("adb", "fixture"); Set("adbChecked", true);
        bool connected = false, visible = true;
        string endpoint = "192.168.1.20:39001", serial = "PAIRED-SERIAL";
        var commands = new List<string>();
        Func<string, CancellationToken, int, string[], Task<AdbResult>> runner = (path, token, timeout, args) =>
        {
            token.ThrowIfCancellationRequested();
            string command = string.Join(" ", args);
            commands.Add(command);
            string output = "";
            if (args[0] == "mdns")
                output = visible ? "paired-phone _adb-tls-connect._tcp " + endpoint + "\nnew-phone _adb-tls-pairing._tcp 192.168.1.30:42000" : "";
            else if (args[0] == "devices") output = connected ? endpoint + " device model:Phone" : "";
            else if (args[0] == "connect")
            {
                Check(args[1] == "paired-phone._adb-tls-connect._tcp" || args[1] == endpoint, "only current TLS connection targets are attempted");
                connected = true; output = "connected";
            }
            else if (args[0] == "-s") output = serial;
            return Task.FromResult(new AdbResult { ExitCode = 0, Output = output });
        };
        Set("RunAdb", runner);
        try
        {
            await (Task)Call("RefreshDevices", CancellationToken.None, false);
            var state = fixtureStore.Read();
            Check(state.devices.Count == 1 && state.devices[0].pairedWireless, "newly visible previously paired device reconnects and enters Paired Devices");
            var available = (IEnumerable<WirelessService>)typeof(BuildController).GetProperty("AvailableServices", Flags).GetValue(service);
            Check(available.Single().Name == "new-phone" && available.Single().Pairing, "unpaired pairing advertisement stays highlighted in Available Devices");
            Check(!commands.Any(c => c.StartsWith("pair ")), "discovery never pairs automatically");
            Check(((LocalState)Get("local")).selectedDevices.Single() == endpoint, "first verified ready device may be selected");
            endpoint = "192.168.1.20:49002"; connected = false;
            await (Task)Call("RefreshDevices", CancellationToken.None, false);
            Check(((LocalState)Get("local")).selectedDevices.Single() == endpoint, "selection follows verified hardware to a changed Wi-Fi port");
            var local = (LocalState)Get("local");
            local.selectedDevices.Clear(); local.selectionIdentities.Clear(); local.requireDeviceSelection = true;
            await (Task)Call("RefreshDevices", CancellationToken.None, false);
            Check(local.selectedDevices.Count == 0, "automatic reconnect respects explicit deselection");
            visible = false; connected = false; commands.Clear();
            await (Task)Call("RefreshDevices", CancellationToken.None, false);
            Check(!commands.Any(c => c.StartsWith("connect ")), "quiet discovery does not hammer stale endpoints without an advertisement");
            Check(fixtureStore.Read().devices.Single().pairedWireless, "disconnected paired device stays in the paired group");
            visible = true; connected = true; serial = "OTHER-HARDWARE";
            local.selectedDevices.Add(endpoint);
            MachineSettingsStore.ReconcileSelection(local, fixtureStore.Read());
            await (Task)Call("RefreshDevices", CancellationToken.None, false);
            Check(local.selectedDevices.Count == 0, "reused address cannot inherit selection from a different physical device");
        }
        finally
        {
            Set("RunAdb", originalRunner); Set("sharedStore", originalStore); Set("machine", originalMachine); Set("local", originalLocal);
            Set("devices", new List<Device>()); Set("services", new List<WirelessService>()); Set("adbChecked", false);
        }
        RecoveryChecks();
    }
    private static void RecoveryChecks()
    {
        string output = Path.Combine(BuildStorage.Root, "Recovery Output");
        Directory.CreateDirectory(output);
        string artifact = Path.Combine(output, "player.apk");
        File.WriteAllText(artifact, "old-good-artifact");
        string version = PlayerSettings.bundleVersion;
        bool development = EditorUserBuildSettings.development;
        var journal = BuildRecovery.Begin(output);
        File.WriteAllText(Path.Combine(BuildRecovery.Stage(journal), "player.apk"), "new-artifact");
        File.WriteAllText(Path.Combine(BuildRecovery.Stage(journal), "companion.txt"), "new-companion");
        PlayerSettings.bundleVersion = "9.8.7";
        EditorUserBuildSettings.development = !development;
        BuildRecovery.Publish(journal);
        Check(File.ReadAllText(artifact) == "new-artifact", "staged publication installs a complete new artifact");
        string result = BuildRecovery.Recover();
        Check(result.StartsWith("Recovered") && File.ReadAllText(artifact) == "old-good-artifact", "interrupted publication restores the previous artifact");
        Check(!File.Exists(Path.Combine(output, "companion.txt")), "recovery removes newly published companions");
        Check(PlayerSettings.bundleVersion == version && EditorUserBuildSettings.development == development, "recovery restores persisted original settings");
        journal = BuildRecovery.Begin(output);
        File.WriteAllText(Path.Combine(BuildRecovery.Stage(journal), "player.apk"), "committed");
        BuildRecovery.Publish(journal); BuildRecovery.Commit(journal);
        BuildRecovery.Recover();
        Check(File.ReadAllText(artifact) == "committed", "committed publication survives recovery");
        Check(BuildRecovery.Recover() == "", "recovery is idempotent after completion");
    }
}
