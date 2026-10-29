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
    public abstract class BuildDashboardWindow : EditorWindow
    {
        internal BuildController Controller { get { return BuildController.Instance; } }
        private bool advanced, setup, showSigning;
        private int tab, setupType;
        private Vector2 scroll;
        private string endpoint = "", pairingEndpoint = "", pairingCode = "";
        protected abstract bool SimpleControls { get; }
        private PresetFile presets { get { return Controller.presets; } set { Controller.presets = value; } }
        private LocalState local { get { return Controller.local; } set { Controller.local = value; } }
        private MachineSettings machine { get { return Controller.machine; } set { Controller.machine = value; } }
        private MachineSettingsStore sharedStore { get { return Controller.sharedStore; } set { Controller.sharedStore = value; } }
        private Dictionary<string, string> connectionChoices { get { return Controller.connectionChoices; }  }
        private List<Device> devices { get { return Controller.devices; } set { Controller.devices = value; } }
        private List<WirelessService> services { get { return Controller.services; } set { Controller.services = value; } }
        private string adb { get { return Controller.adb; } set { Controller.adb = value; } }
        private string adbVersion { get { return Controller.adbVersion; } set { Controller.adbVersion = value; } }
        private string mdnsStatus { get { return Controller.mdnsStatus; } set { Controller.mdnsStatus = value; } }
        private string message { get { return Controller.message; } set { Controller.message = value; } }
        private string logs { get { return Controller.logs; } set { Controller.logs = value; } }
        private string version { get { return Controller.version; } set { Controller.version = value; } }
        private int code { get { return Controller.code; } set { Controller.code = value; } }
        private bool adbChecked { get { return Controller.adbChecked; } set { Controller.adbChecked = value; } }
        private MessageType messageType { get { return Controller.messageType; } set { Controller.messageType = value; } }
        private BuildPreset Preset { get { return Controller.Preset; } }
        private List<Device> Selected { get { return Controller.Selected; } }
        private BuildRecord LastApk { get { return Controller.LastApk; } }
        private bool Conflicting { get { return Controller.Conflicting; } }
        protected void OnEnable() { Controller.Changed += Repaint; }
        protected void OnDisable() { Controller.Changed -= Repaint; pairingCode = ""; }
        private bool IsSelected(string value) { return Controller.IsSelected(value); }
        private void SelectTransport(SavedDevice saved, string transport, bool selected) { Controller.SelectTransport(saved, transport, selected); }
        private void ChangeMachine(Func<MachineSettings> change) { Controller.ChangeMachine(change); }
        private void SaveLocal() { Controller.SaveLocal(); }
        private void SavePresets() { Controller.SavePresets(); }
        private void ReadVersion() { Controller.ReadVersion(); }
        private void SetMessage(string value, MessageType type) { Controller.SetMessage(value, type); }
        private void Stop() { Controller.Stop(); }
        private void StartOperation(string title, Func<CancellationToken, Task> action) { Controller.StartOperation(title, action); }
        private Task RefreshDevices(CancellationToken token) { return Controller.RefreshDevices(token); }
        private Task Discover(CancellationToken token) { return Controller.Discover(token); }
        private Task Connect(CancellationToken token, string input) { return Controller.Connect(token, input); }
        private Task ReadLogs(CancellationToken token) { return Controller.ReadLogs(token); }
        private void QueueBuild(bool run) { Controller.QueueBuild(run); }
        private void Install(BuildRecord record) { Controller.Install(record); }

        private bool ActionButton(string label)
        {
            using (new EditorGUI.DisabledScope(Conflicting)) return GUILayout.Button(label);
        }
        private void DrawBuildLocation()
        {
            EditorGUILayout.LabelField("Build location — shared across projects", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string folder = EditorGUILayout.DelayedTextField(machine.outputFolder);
                if (EditorGUI.EndChangeCheck()) ChangeMachine(() => sharedStore.SetOutputFolder(folder));
                if (GUILayout.Button("Browse...", GUILayout.Width(90)))
                {
                    string initial = Directory.Exists(machine.outputFolder) ? machine.outputFolder : BuildStorage.Root;
                    string chosen = EditorUtility.OpenFolderPanel("Choose build location for all projects", initial, "");
                    if (!string.IsNullOrEmpty(chosen)) ChangeMachine(() => sharedStore.SetOutputFolder(chosen));
                }
            }
            try { EditorGUILayout.LabelField("Effective output", BuildStorage.BuildOutputRoot(Preset, null, machine), EditorStyles.wordWrappedMiniLabel); }
            catch (Exception e) { EditorGUILayout.HelpBox(e.Message, MessageType.Warning); }
            if (string.IsNullOrEmpty(machine.outputFolder))
                EditorGUILayout.HelpBox("Choose a folder once to use it in every project. Until then, each project's preset folder is used.", MessageType.None);
            if (Preset.overrideMachineOutput)
                EditorGUILayout.HelpBox("This preset has a project-only output override. Disable it under Advanced to use the shared location.", MessageType.Info);
        }
        protected void OnGUI()
        {
            if (local == null || presets == null) return;
            if (BuildJobs.Target == BuildTarget.WebGL)
            {
                EditorGUILayout.HelpBox("WebGL Studio brings browser preview, download budgets and hosting tools into one workflow.", MessageType.Info);
                if (GUILayout.Button("Open WebGL Studio", GUILayout.Height(40))) WebGLStudio.Open();
                return;
            }
            if (SimpleControls) DrawSimple();
            else DrawFullControls();
        }

        private void DrawSimple()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("BUILD AUTOMATOR", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(BuildJobs.Target.ToString(), EditorStyles.miniLabel);
                if (GUILayout.Button("Show Full Controls", EditorStyles.toolbarButton)) BuildFullControlsDashboard.Open();
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(message, messageType);
            if (Controller.scheduler.ForegroundBusy)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Operation in progress — this window can be closed.", EditorStyles.miniLabel);
                    if (GUILayout.Button("Cancel", GUILayout.Width(80))) Stop();
                }
            }
            {
                DrawPresetSelector();
                version = EditorGUILayout.TextField("Version", version);
                if (BuildJobs.Target == BuildTarget.Android || BuildJobs.Target == BuildTarget.iOS)
                    code = EditorGUILayout.IntField("Build / version code", code);
                DrawBuildLocation();
                if (BuildJobs.Target == BuildTarget.Android) DrawSimpleDevices();

                DrawInlineSetup();
                var errors = BuildJobs.Validate(Preset, version, code, false, false, adb, null);
                var runErrors = BuildJobs.Validate(Preset, version, code, true, Selected.Count > 0 && Selected.Count == local.selectedDevices.Count, adb, null);
                EditorGUILayout.Space(8);
                foreach (string error in errors) EditorGUILayout.HelpBox(error, MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(Conflicting || errors.Count > 0))
                        if (GUILayout.Button("Build", GUILayout.Height(38))) QueueBuild(false);
                    using (new EditorGUI.DisabledScope(Conflicting || runErrors.Count > 0))
                        if (GUILayout.Button("Build & Run", GUILayout.Height(38))) QueueBuild(true);
                }
                if (errors.Count == 0 && runErrors.Count > 0)
                    EditorGUILayout.HelpBox(string.Join("\n", runErrors), MessageType.None);
                EditorGUILayout.Space(10);
                if (GUILayout.Button("Show Full Controls", GUILayout.Height(28))) BuildFullControlsDashboard.Open();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawSimpleDevices()
        {
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Devices", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(Conflicting))
                    if (GUILayout.Button("Refresh", GUILayout.Width(80))) StartOperation("Refreshing devices", RefreshDevices);
                if (GUILayout.Button("Setup", GUILayout.Width(65))) setup = !setup;
            }
            DrawDeviceGroups(true);
            if (setup) DrawSetup();
        }
        private void DrawPresetSelector()
        {
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup("Preset", local.presetIndex, presets.presets.Select(p => p.name).ToArray());
            if (EditorGUI.EndChangeCheck()) Controller.ChoosePreset(selected);
        }
        private void DrawDeviceGroups(bool compact)
        {
            var paired = machine.devices.Where(d => d.pairedWireless).ToArray();
            EditorGUILayout.LabelField("Paired Devices", EditorStyles.boldLabel);
            foreach (var saved in paired) DrawDevice(saved, compact);
            if (paired.Length == 0) EditorGUILayout.LabelField("Paired Wi-Fi connections appear here after verification.", EditorStyles.wordWrappedMiniLabel);
            var other = machine.devices.Where(d => !d.pairedWireless).ToArray();
            if (other.Length > 0)
            {
                EditorGUILayout.LabelField("USB / Saved Devices", EditorStyles.boldLabel);
                foreach (var saved in other) DrawDevice(saved, compact);
            }
            EditorGUILayout.LabelField("Available Devices", EditorStyles.boldLabel);
            var available = Controller.AvailableServices.ToArray();
            foreach (var service in available)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(service.Name, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(service.Pairing ? "Pairing available" : "Wi-Fi visible — authorization not verified", EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.LabelField(service.Endpoint, EditorStyles.miniLabel);
                    if (GUILayout.Button(service.Pairing ? "Pair this device" : "Connection / pairing help"))
                    {
                        setup = true; setupType = 1;
                        if (service.Pairing) pairingEndpoint = service.Endpoint;
                        else endpoint = service.Endpoint;
                    }
                }
            }
            if (available.Length == 0) EditorGUILayout.LabelField("No additional devices advertising wireless debugging.", EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrEmpty(Controller.discoveryMessage))
                EditorGUILayout.LabelField(Controller.discoveryMessage, EditorStyles.wordWrappedMiniLabel);
        }
        private void DrawInlineSetup()
        {
            if (BuildJobs.Scenes().Length == 0 && GUILayout.Button("Choose build scenes"))
                EditorApplication.ExecuteMenuItem(
#if UNITY_6000_0_OR_NEWER
                    "File/Build Profiles"
#else
                    "File/Build Settings..."
#endif
                );
            if (BuildJobs.Target != BuildTarget.Android) return;
            if (string.IsNullOrEmpty(adb))
            {
                EditorGUILayout.HelpBox("Android tools are missing. Open External Tools or choose ADB.", MessageType.Warning);
                if (GUILayout.Button("External Tools")) SettingsService.OpenUserPreferences("Preferences/External Tools");
                using (new EditorGUI.DisabledScope(Conflicting))
                    if (GUILayout.Button("Choose ADB"))
                    {
                        string selected = EditorUtility.OpenFilePanel("Select adb executable", "", Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "");
                        if (selected.Length > 0) ChangeMachine(() => sharedStore.SetAdbOverride(selected));
                    }
            }
            if (PlayerSettings.Android.useCustomKeystore &&
                (string.IsNullOrEmpty(PlayerSettings.Android.keystorePass) || string.IsNullOrEmpty(PlayerSettings.Android.keyaliasPass) || showSigning))
            {
                showSigning = true;
                EditorGUILayout.LabelField("Signing — this Editor session", EditorStyles.boldLabel);
                PlayerSettings.Android.keystorePass = EditorGUILayout.PasswordField("Keystore password", PlayerSettings.Android.keystorePass);
                PlayerSettings.Android.keyaliasPass = EditorGUILayout.PasswordField("Key alias password", PlayerSettings.Android.keyaliasPass);
                if (GUILayout.Button("Signing settings")) SettingsService.OpenProjectSettings("Project/Player");
            }
        }

        private void DrawFullControls()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("BUILD AUTOMATOR", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(BuildJobs.Target.ToString(), EditorStyles.miniLabel);
                if (GUILayout.Button("Help", EditorStyles.toolbarButton)) OpenGuide();
            }
            tab = GUILayout.Toolbar(tab, new[] { "Build", "Devices", "History" });
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(message, messageType);
            if (Controller.scheduler.ForegroundBusy)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Operation in progress — this window can be closed.", EditorStyles.miniLabel);
                    if (GUILayout.Button("Cancel operation", GUILayout.Width(140))) Stop();
                }
            }
            {
                if (tab == 0) DrawBuild();
                if (tab == 1) DrawDevices();
                if (tab == 2) DrawHistory();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawBuild()
        {
            EditorGUI.BeginChangeCheck();
            local.presetIndex = EditorGUILayout.Popup("Preset", local.presetIndex, presets.presets.Select(p => p.name).ToArray());
            if (EditorGUI.EndChangeCheck()) Controller.ChoosePreset(local.presetIndex);
            EditorGUILayout.LabelField("Application", BuildJobs.Package);
            EditorGUILayout.LabelField("Scenes", BuildJobs.Scenes().Length + " enabled (active profile overrides respected)");
            DrawBuildLocation();
            version = EditorGUILayout.TextField("Version", version);
            if (BuildJobs.Target == BuildTarget.Android || BuildJobs.Target == BuildTarget.iOS) code = EditorGUILayout.IntField("Build / version code", code);
            if (BuildJobs.Target == BuildTarget.Android)
            {
                EditorGUILayout.LabelField("Selected devices", Selected.Count + " ready / " + local.selectedDevices.Count + " selected");
                if (GUILayout.Button("Choose devices / Setup")) tab = 1;
            }
            DrawInlineSetup();
            var errors = BuildJobs.Validate(Preset, version, code, false, false, adb);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Readiness", EditorStyles.boldLabel);
            if (errors.Count == 0) EditorGUILayout.HelpBox("Ready to build. The artifact is written directly to the selected folder and replaces the same target/version file.", MessageType.Info);
            foreach (string error in errors) EditorGUILayout.HelpBox(error, MessageType.Warning);
            var runErrors = BuildJobs.Validate(Preset, version, code, true, Selected.Count > 0 && Selected.Count == local.selectedDevices.Count, adb);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(Conflicting || errors.Count > 0))
                    if (GUILayout.Button("Build", GUILayout.Height(34))) QueueBuild(false);
                using (new EditorGUI.DisabledScope(Conflicting || runErrors.Count > 0))
                    if (GUILayout.Button("Build & Run", GUILayout.Height(34))) QueueBuild(true);
                using (new EditorGUI.DisabledScope(Conflicting || LastApk == null || Selected.Count == 0 || !adbChecked))
                    if (GUILayout.Button("Install Last Build", GUILayout.Height(34))) Install(LastApk);
            }
            if (errors.Count == 0 && runErrors.Count > 0) EditorGUILayout.HelpBox(string.Join("\n", runErrors), MessageType.None);
            advanced = EditorGUILayout.Foldout(advanced, "Advanced / Setup checks", true);
            if (advanced) DrawAdvanced();
        }

        private void DrawAdvanced()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Shared preset settings", EditorStyles.boldLabel);
                string[] presetNames = presets.presets.Select(p => p.name).ToArray();
                int defaultIndex = Math.Max(0, Array.IndexOf(presetNames, string.IsNullOrEmpty(presets.defaultPreset) ? "Release" : presets.defaultPreset));
                int chosenDefault = EditorGUILayout.Popup("First-use default", defaultIndex, presetNames);
                if (chosenDefault != defaultIndex) { presets.defaultPreset = presetNames[chosenDefault]; SavePresets(); }
                EditorGUI.BeginChangeCheck();
                Preset.development = EditorGUILayout.Toggle("Development build", Preset.development);
                Preset.appBundle = EditorGUILayout.Toggle("Android App Bundle", Preset.appBundle);
                Preset.incrementVersionCode = EditorGUILayout.Toggle("Suggest next version code", Preset.incrementVersionCode);
                Preset.overrideMachineOutput = EditorGUILayout.Toggle("Project-only output override", Preset.overrideMachineOutput);
                Preset.outputFolder = EditorGUILayout.TextField("Project fallback / override", Preset.outputFolder);
                Preset.namingTemplate = EditorGUILayout.TextField("Filename template", Preset.namingTemplate);
                if (EditorGUI.EndChangeCheck()) SavePresets();
                EditorGUILayout.LabelField("Tokens: {package}, {version}, {code}, {preset}, {target}", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Import presets")) ImportPresets();
                    if (GUILayout.Button("Export presets")) ExportPresets();
                }
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Shared on this computer (current OS user)", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                bool auto = EditorGUILayout.Toggle("Refresh devices automatically", machine.autoRefresh);
                if (EditorGUI.EndChangeCheck()) ChangeMachine(() => sharedStore.SetAutoRefresh(auto));
                EditorGUI.BeginChangeCheck();
                int interval = EditorGUILayout.IntSlider("Wi-Fi discovery interval (seconds)", (int)Controller.RefreshInterval, 2, 30);
                if (EditorGUI.EndChangeCheck()) ChangeMachine(() => sharedStore.SetRefreshSeconds(interval));
                EditorGUI.BeginChangeCheck();
                bool reveal = EditorGUILayout.Toggle("Reveal successful build", machine.revealBuild);
                if (EditorGUI.EndChangeCheck()) ChangeMachine(() => sharedStore.SetRevealBuild(reveal));
                EditorGUI.BeginChangeCheck();
                string adbOverride;
                using (new EditorGUI.DisabledScope(Conflicting)) adbOverride = EditorGUILayout.DelayedTextField("ADB executable override", machine.adbOverride);
                if (EditorGUI.EndChangeCheck()) ChangeMachine(() => sharedStore.SetAdbOverride(adbOverride));
                EditorGUILayout.LabelField("Shared settings file", sharedStore.FilePath, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("Resolved ADB", string.IsNullOrEmpty(adb) ? "Not found" : adb, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(adbVersion, EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(mdnsStatus)) EditorGUILayout.LabelField(mdnsStatus, EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (ActionButton("Browse ADB"))
                    {
                        string selected = EditorUtility.OpenFilePanel("Select adb executable", "", Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "");
                        if (selected.Length > 0) ChangeMachine(() => sharedStore.SetAdbOverride(selected));
                    }
                    if (ActionButton("Check ADB")) StartOperation("Checking ADB", RefreshDevices);
                    if (GUILayout.Button("External Tools")) SettingsService.OpenUserPreferences("Preferences/External Tools");
                    if (GUILayout.Button("Player Settings")) SettingsService.OpenProjectSettings("Project/Player");
                }
                EditorGUILayout.HelpBox("Install Android Build Support, SDK/NDK and OpenJDK through Unity Hub. ADB discovery alone does not verify the Android compiler toolchain. Unity reports toolchain errors during the build.", MessageType.None);
                if (BuildJobs.Target == BuildTarget.Android && PlayerSettings.Android.useCustomKeystore)
                {
                    var keystore = BuildStorage.InspectKeystore();
                    EditorGUILayout.LabelField("Configured keystore", keystore.ConfiguredPath, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.LabelField("Resolved keystore", string.IsNullOrEmpty(keystore.ResolvedPath) ? "Delegated to Unity" : keystore.ResolvedPath, EditorStyles.wordWrappedMiniLabel);
                    if (!string.IsNullOrEmpty(keystore.Note)) EditorGUILayout.HelpBox(keystore.Note, MessageType.Info);
                    EditorGUILayout.LabelField("Signing passwords (this Editor session only)", EditorStyles.boldLabel);
                    PlayerSettings.Android.keystorePass = EditorGUILayout.PasswordField("Keystore password", PlayerSettings.Android.keystorePass);
                    PlayerSettings.Android.keyaliasPass = EditorGUILayout.PasswordField("Key alias password", PlayerSettings.Android.keyaliasPass);
                }
                if (GUILayout.Button("Export standalone Build Automator package")) ExportStandalone();
            }
        }

        private void DrawDevices()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (ActionButton("Refresh devices")) StartOperation("Refreshing devices", RefreshDevices);
                if (ActionButton("Discover Wi-Fi")) { setup = true; StartOperation("Discovering wireless services", Discover); }
                if (GUILayout.Button("Add device / Help")) setup = !setup;
                if (local.selectedDevices.Count > 0 && GUILayout.Button("Clear selection"))
                {
                    local.selectedDevices.Clear();
                    local.selectionIdentities.Clear();
                    local.requireDeviceSelection = true;
                    SaveLocal();
                }
            }
            if (string.IsNullOrEmpty(adb))
            {
                EditorGUILayout.HelpBox("ADB was not found. Open Build > Advanced to check setup or select an executable.", MessageType.Warning);
                if (GUILayout.Button("Open setup checks")) { tab = 0; advanced = true; }
            }
            if (devices.Count == 0) EditorGUILayout.HelpBox("No connected devices. Enable debugging, connect a data-capable USB cable, or follow Add device below.", MessageType.Info);
            EditorGUILayout.HelpBox("Saved devices and nicknames are shared across projects. Selection is per project. Verified USB/Wi-Fi identities share one card; choose the connection to use.", MessageType.None);
            DrawDeviceGroups(false);
            setup = EditorGUILayout.Foldout(setup, "Add device / Guided setup", true);
            if (setup) DrawSetup();
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Device diagnostics", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(Conflicting || Selected.Count != 1 || !adbChecked))
            {
                if (GUILayout.Button("Read app logs (selected device)")) StartOperation("Reading app logs", ReadLogs);
                if (GUILayout.Button("Check device storage")) StartOperation("Checking storage", async token =>
                {
                    var result = await Adb.Run(adb, token, 10000, "-s", Selected[0].Id, "shell", "df", "-h", "/data");
                    token.ThrowIfCancellationRequested();
                    if (!result.Success) throw new InvalidOperationException(result.Detail);
                    logs = result.Output;
                });
                if (GUILayout.Button("Uninstall current app (deletes app data)...")) Uninstall();
            }
            if (!string.IsNullOrEmpty(logs))
            {
                EditorGUILayout.TextArea(logs, GUILayout.MinHeight(100), GUILayout.MaxHeight(250));
                if (GUILayout.Button("Save diagnostic text...")) SaveDiagnostic();
            }
        }

        private void DrawDevice(SavedDevice saved, bool compact = false)
        {
            var live = devices.Where(d => saved.aliases.Contains(MachineSettingsStore.NormalizeTransport(d.Id))).ToArray();
            var choices = live.Length > 0 ? live : saved.aliases.Select(id => new Device
                { Id = id, Name = saved.model, State = "saved", Transport = id.Contains(":") || id.Contains("._tcp") ? "Wi-Fi" : "USB" }).ToArray();
            bool selected = local.selectedDevices.Any(id => saved.aliases.Contains(MachineSettingsStore.NormalizeTransport(id)));
            if (choices.Length == 0)
            {
                choices = new[] { new Device { Id = "", Name = saved.model, State = "saved", Transport = "No known connection" } };
            }
            string choice;
            connectionChoices.TryGetValue(saved.key, out choice);
            var device = choices.FirstOrDefault(d => d.Id == choice) ?? choices.FirstOrDefault(d => IsSelected(d.Id)) ?? choices.FirstOrDefault(d => d.Ready) ?? choices[0];
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!device.Ready && !selected))
                    {
                        bool next = EditorGUILayout.Toggle(selected, GUILayout.Width(18));
                        if (next != selected) SelectTransport(saved, device.Id, next);
                    }
                    GUILayout.Label(string.IsNullOrEmpty(saved.nickname) ? saved.model : saved.nickname, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(device.Transport + " | " + (device.Ready ? "Ready" : device.State), EditorStyles.miniLabel);
                }
                if (choices.Length > 1)
                {
                    int current = Array.IndexOf(choices, device);
                    int next = EditorGUILayout.Popup("Connection", current, choices.Select(d => d.Transport + " — " + d.Id).ToArray());
                    if (next != current)
                    {
                        device = choices[next];
                        connectionChoices[saved.key] = device.Id;
                        if (selected && device.Ready) SelectTransport(saved, device.Id, true);
                    }
                }
                EditorGUILayout.SelectableLabel(device.Id, EditorStyles.miniLabel, GUILayout.Height(17));
                if (!device.Ready) EditorGUILayout.HelpBox(device.Hint, MessageType.None);
                if (!device.Ready && saved.pairedWireless) EditorGUILayout.LabelField("Waiting for a current Wi-Fi advertisement; reconnect is automatic.", EditorStyles.wordWrappedMiniLabel);
                if (selected && device.Ready && !IsSelected(device.Id) && GUILayout.Button("Use this connection for the selected device")) SelectTransport(saved, device.Id, true);
                if (compact) return;
                EditorGUI.BeginChangeCheck();
                string nickname = EditorGUILayout.DelayedTextField("Nickname (shared)", saved.nickname);
                if (EditorGUI.EndChangeCheck()) ChangeMachine(() => sharedStore.Rename(saved.key, nickname));
                using (new EditorGUILayout.HorizontalScope())
                {
                    string address;
                    if (!device.Ready && Adb.TryEndpoint(device.Id, out address, 0) && ActionButton("Reconnect"))
                    {
                        string reconnect = device.Id;
                        StartOperation("Reconnecting", token => Connect(token, reconnect));
                    }
                    if (live.Length == 0 && GUILayout.Button("Forget in all projects..."))
                    {
                        if (EditorUtility.DisplayDialog("Forget shared device?", "Remove this saved device and its nickname from all projects on this computer? This does not revoke Android pairing.", "Forget", "Cancel"))
                        {
                            ChangeMachine(() => sharedStore.Forget(saved.key));
                            SelectTransport(saved, "", false);
                        }
                    }
                }
            }
        }
        private void DrawSetup()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                setupType = GUILayout.Toolbar(setupType, new[] { "USB", "Android Wi-Fi", "Fire TV / Manual" });
                if (setupType == 0)
                    EditorGUILayout.HelpBox("1. Enable Developer options and USB debugging on the device.\n2. Use a USB data cable and approve this computer's RSA prompt on the device.\n3. Refresh devices. On Windows, check the manufacturer's USB driver if nothing appears. Fire tablets may require Amazon's USB driver.", MessageType.Info);
                if (setupType == 1)
                {
                    EditorGUILayout.HelpBox("On supported Android 11+ phones: use the same Wi-Fi network, enable Wireless debugging, then choose Pair device with pairing code. Enter the pairing IP:port and six-digit code once. TV/device support varies. After pairing, Refresh or Discover Wi-Fi automatically connects the advertised device even when its connection IP and port change.", MessageType.Info);
                    pairingEndpoint = EditorGUILayout.TextField("Pairing IP:port", pairingEndpoint);
                    pairingCode = EditorGUILayout.PasswordField("Pairing code", pairingCode);
                    if (ActionButton("Pair device"))
                    {
                        string address, secret = pairingCode;
                        pairingCode = "";
                        if (!Adb.TryEndpoint(pairingEndpoint, out address, 0) || !Regex.IsMatch(secret, @"^\d{6}$"))
                            SetMessage("Enter the exact pairing endpoint and a six-digit code shown on the device.", MessageType.Warning);
                        else Controller.Pair(address, secret);
                    }
                }
                if (setupType == 2)
                    EditorGUILayout.HelpBox("For Fire OS devices supporting ADB: enable ADB debugging under Developer Options, find the device IP in its network settings, and connect on the same network (usually port 5555). Approve the debugging prompt on the TV. Do not expose ADB to the internet. Discovery is not guaranteed on Fire TV or restricted Wi-Fi networks.", MessageType.Info);
                foreach (var service in services)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label((service.Pairing ? "Pair: " : "Connect: ") + service.Name + "  " + service.Endpoint, EditorStyles.wordWrappedMiniLabel);
                        if (GUILayout.Button("Use", GUILayout.Width(45)))
                        {
                            if (service.Pairing) { setupType = 1; pairingEndpoint = service.Endpoint; }
                            else endpoint = service.Endpoint;
                        }
                    }
                }
                endpoint = EditorGUILayout.TextField("Connection IP:port", endpoint);
                if (GUILayout.Button("Save device for all projects")) ChangeMachine(() => sharedStore.AddEndpoint(endpoint));
                if (ActionButton("Connect")) { string input = endpoint; StartOperation("Connecting", token => Connect(token, input)); }
                EditorGUILayout.HelpBox("Advertised, previously paired Android devices connect automatically by mDNS identity. Some Fire OS 8 builds do not advertise the service; a random port cannot be derived safely when that happens. Manual IP:port, USB, or legacy ADB port 5555 is then required.", MessageType.None);
            }
        }

        private void DrawHistory()
        {
            EditorGUILayout.HelpBox("Last 100 build records on this computer. No automatic cleanup deletes build files. Install verifies the saved APK checksum. Select deployment targets in Devices.", MessageType.Info);
            if (local.history.Count == 0) EditorGUILayout.LabelField("No builds recorded yet.");
            foreach (var record in local.history)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(record.preset + " | " + record.target + " | " + record.result, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(record.utc + "   v" + record.version + " (" + record.code + ")   " + record.seconds.ToString("F1") + "s   " + (record.bytes / 1048576d).ToString("F1") + " MB", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(record.path, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.LabelField("Deployment: " + record.deployment, EditorStyles.wordWrappedMiniLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(!File.Exists(record.path) && !Directory.Exists(record.path)))
                            if (GUILayout.Button("Reveal")) EditorUtility.RevealInFinder(record.path);
                        using (new EditorGUI.DisabledScope(Conflicting || !record.IsApk || !File.Exists(record.path) || Selected.Count == 0 || !adbChecked))
                            if (GUILayout.Button("Install & Run")) Install(record);
                    }
                }
            }
        }

        private void Uninstall()
        {
            string serial = Selected[0].Id, package = BuildJobs.Package;
            if (!Regex.IsMatch(package, @"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$")) return;
            if (!EditorUtility.DisplayDialog("Delete this app and its data?", "Uninstall " + package + " from " + serial + "?\nSaved progress and app data will be deleted. This cannot be undone.", "Uninstall and delete data", "Cancel")) return;
            StartOperation("Uninstalling app", async token =>
            {
                var result = await Adb.Run(adb, token, 30000, "-s", serial, "uninstall", package);
                token.ThrowIfCancellationRequested();
                if (!result.Success || !result.Output.Contains("Success")) throw new InvalidOperationException(result.Detail);
                SetMessage("App uninstalled. Use Install Last Build to reinstall when ready.", MessageType.Info);
            });
        }

        private void ImportPresets()
        {
            string path = EditorUtility.OpenFilePanel("Import shared presets", "", "json");
            if (path.Length == 0) return;
            try
            {
                var incoming = JsonUtility.FromJson<PresetFile>(File.ReadAllText(path));
                string error = BuildStorage.ValidatePresets(incoming);
                if (error.Length > 0) throw new InvalidOperationException(error);
                if (!EditorUtility.DisplayDialog("Replace shared presets?", "This replaces this project's preset list. Local device preferences and signing passwords are not imported.", "Replace", "Cancel")) return;
                presets = incoming;
                Controller.ChoosePreset(0);
                SavePresets();
                SaveLocal();
                ReadVersion();
            }
            catch (Exception e) { SetMessage(e.Message, MessageType.Error); }
        }

        private void ExportPresets()
        {
            string path = EditorUtility.SaveFilePanel("Export shared presets", "", "BuildAutomator-Presets", "json");
            if (path.Length == 0) return;
            try { BuildStorage.Write(path, presets); }
            catch (Exception e) { SetMessage(e.Message, MessageType.Error); }
        }

        private void SaveDiagnostic()
        {
            string path = EditorUtility.SaveFilePanel("Save diagnostic text (review before sharing)", "", "BuildAutomator-Diagnostics", "txt");
            if (path.Length == 0) return;
            try { File.WriteAllText(path, "Build Automator 2\nUnity: " + Application.unityVersion + "\n" + adbVersion + "\nApp: " + BuildJobs.Package + "\n\n" + logs); }
            catch (Exception e) { SetMessage(e.Message, MessageType.Error); }
        }

        internal static string ToolFolder()
        {
            // Both dashboards live in this file; Full Controls has no matching MonoScript asset.
            foreach (string guid in AssetDatabase.FindAssets("BuildDashboard t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
                var definition = AssetDatabase.LoadAssetAtPath<UnityEditorInternal.AssemblyDefinitionAsset>(folder + "/OutBuildAutomator.Editor.asmdef");
                if (definition != null && JsonUtility.FromJson<AssemblyName>(definition.text)?.name == "OutBuildAutomator.Editor") return folder;
            }
            throw new InvalidOperationException("Cannot locate the complete BuildAutomator folder. Reimport the standalone tool.");
        }

        [Serializable] private sealed class AssemblyName { public string name; }

        public static void ExportStandalonePackage(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose a package output path.", nameof(path));
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.ExportPackage(ToolFolder(), path, ExportPackageOptions.Recurse);
            if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new IOException("Unity did not create the standalone package: " + path);
        }

        private void OpenGuide()
        {
            var guide = AssetDatabase.LoadMainAssetAtPath(ToolFolder() + "/README.md");
            if (guide != null) AssetDatabase.OpenAsset(guide);
            else SetMessage("Import the complete BuildAutomator folder, including README.md.", MessageType.Warning);
        }

        private void ExportStandalone()
        {
            string path = EditorUtility.SaveFilePanel("Export standalone Editor tool", "", "BuildAutomator-2.5.1", "unitypackage");
            if (path.Length == 0) return;
            try
            {
                ExportStandalonePackage(path);
                SetMessage("Exported standalone tool: " + path + ". No game scripts, device history, or passwords were included.", MessageType.Info);
            }
            catch (Exception e) { SetMessage(e.Message, MessageType.Error); }
        }
    }

    public sealed class BuildDashboard : BuildDashboardWindow
    {
        protected override bool SimpleControls { get { return true; } }

        [MenuItem("Build Automator/Dashboard", false, 0)]
        public static void Open()
        {
            if (BuildJobs.Target == BuildTarget.WebGL) { WebGLStudio.Open(); return; }
            var window = GetWindow<BuildDashboard>("Build Automator");
            window.minSize = new Vector2(520, 430);
            window.Show();
        }

        [MenuItem("Build Automator/Build %&b", false, 1)]
        public static void OpenBuild() { Open(); }

        [MenuItem("Build Automator/Build and Run %&r", false, 2)]
        public static void OpenRun() { Open(); }
    }

    public sealed class BuildFullControlsDashboard : BuildDashboardWindow
    {
        protected override bool SimpleControls { get { return false; } }

        [MenuItem("Build Automator/Full Controls", false, 20)]
        public static void Open()
        {
            var window = GetWindow<BuildFullControlsDashboard>("Build Automator — Full Controls");
            window.minSize = new Vector2(620, 500);
            window.Show();
        }
    }
}
