using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace OutBuild
{
    public sealed class WebGLStudio : EditorWindow
    {
        [SerializeField] private int page, previewPort;
        [SerializeField] private bool isolate;
        [SerializeField] private float connectionMbps = 10;
        [SerializeField] private string artifact = "";
        private WebAnalysis analysis;
        private Label status, readiness, previewStatus;
        private Button build, buildPreview;
        private Button cancel;
        private BuildTarget lastTarget;
        private BuildController Controller { get { return BuildController.Instance; } }
        private WebBuildSettings Settings { get { return Controller.Preset.web ?? (Controller.Preset.web = new WebBuildSettings()); } }

#if UNITY_WEBGL
        [MenuItem("Build Automator/WebGL Studio", false, 3)]
#endif
        public static void Open()
        {
            var window = GetWindow<WebGLStudio>("WebGL Studio");
            window.minSize = new Vector2(620, 510);
            window.Show();
        }
        private void OnEnable()
        {
            Controller.Changed += UpdateStatus;
            BuildJobs.ArtifactPublished += Published;
        }
        private void OnDisable()
        {
            Controller.Changed -= UpdateStatus;
            BuildJobs.ArtifactPublished -= Published;
        }
        private void Published(BuildRecord record)
        {
            if (record.target != BuildTarget.WebGL.ToString()) return;
            EditorApplication.delayCall += () => {
                if (this == null) return;
                artifact = record.path; analysis = null;
                Draw();
                Analyze();
            };
        }
        public void CreateGUI()
        {
            lastTarget = BuildJobs.Target;
            if (string.IsNullOrEmpty(artifact)) artifact = Latest()?.path ?? "";
            Draw();
            rootVisualElement.schedule.Execute(() => {
                if (lastTarget != BuildJobs.Target) { lastTarget = BuildJobs.Target; Draw(); }
                UpdateStatus();
            }).Every(1000);
        }
        private BuildRecord Latest()
        {
            return Controller.local.history.FirstOrDefault(h => h.target == BuildTarget.WebGL.ToString() && h.result == "Succeeded" && File.Exists(Path.Combine(h.path, "index.html")));
        }
        private void Draw()
        {
            rootVisualElement.Clear();
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(BuildDashboardWindow.ToolFolder() + "/WebGLStudio.uss");
            // Unity supplies the editor theme and fonts here. Preserve it across redraws.
            if (sheet != null && !rootVisualElement.styleSheets.Contains(sheet)) rootVisualElement.styleSheets.Add(sheet);
            rootVisualElement.AddToClassList("web-studio");
            rootVisualElement.EnableInClassList("light", !EditorGUIUtility.isProSkin);
            var hero = Box(rootVisualElement, "hero");
            Text(hero, "BUILD AUTOMATOR  /  WEB", "eyebrow");
            Text(hero, "From build to browser.", "hero-title");
            Text(hero, "Tune your release. Test locally. Ship with the right headers.", "muted");
            var tabs = Box(rootVisualElement, "tabs");
            string[] names = { "01   Build", "02   Preview", "03   Ship" };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var button = Action(tabs, names[i], () => { page = index; Draw(); });
                button.AddToClassList("tab"); button.EnableInClassList("selected", page == i);
            }
            var scroll = new ScrollView(); scroll.AddToClassList("body"); rootVisualElement.Add(scroll);
            if (BuildJobs.Target != BuildTarget.WebGL)
            {
                Notice(scroll, "Select WebGL in Unity's Build Settings / Build Profiles to build here. Existing WebGL releases can still be previewed and packaged.");
                Action(scroll, "Open Unity Build Settings", () => EditorApplication.ExecuteMenuItem("File/Build Settings"));
            }
            if (page == 0) DrawBuild(scroll);
            else if (page == 1) DrawPreview(scroll);
            else DrawShip(scroll);
            var footer = Box(rootVisualElement, "footer");
            status = Text(footer, "", "status");
            cancel = Action(footer, "Cancel pending work", Controller.Stop);
            UpdateStatus();
        }
        private void DrawBuild(VisualElement parent)
        {
            var card = Card(parent, "Release setup", "Presets share the same controller and output rules as the dashboard.");
            var preset = new PopupField<string>("Preset", Controller.presets.presets.Select(p => p.name).ToList(), Controller.local.presetIndex);
            preset.RegisterValueChangedCallback(e => { Controller.ChoosePreset(Controller.presets.presets.FindIndex(p => p.name == e.newValue)); Draw(); });
            card.Add(preset);
            var version = new TextField("Version") { value = Controller.version, isDelayed = true };
            version.RegisterValueChangedCallback(e => Controller.version = e.newValue); card.Add(version);
            var row = Box(card, "row");
            var output = new TextField("Shared build folder") { value = Controller.machine.outputFolder, isDelayed = true };
            output.AddToClassList("grow"); row.Add(output);
            output.RegisterValueChangedCallback(e => Try(() => { Controller.ChangeMachine(() => Controller.sharedStore.SetOutputFolder(e.newValue)); Draw(); }));
            Action(row, "Browse", () => {
                string path = EditorUtility.OpenFolderPanel("Build location for all projects", BuildStorage.Root, "");
                if (path.Length > 0) Try(() => { Controller.ChangeMachine(() => Controller.sharedStore.SetOutputFolder(path)); Draw(); });
            });
            try { Text(card, "Output: " + BuildStorage.BuildOutputRoot(Controller.Preset, shared: Controller.machine), "path"); }
            catch (Exception e) { Notice(card, e.Message); }
            if (Controller.Preset.overrideMachineOutput) Notice(card, "This preset uses a project output override. Change it in More settings below.");
            Text(card, "Copy destinations (shared): copies only the main build artifact. Manifests and automator helper files stay at the primary location.", "muted");
            foreach (string destination in Controller.machine.additionalOutputFolders)
            {
                string saved = destination;
                var copyRow = Box(card, "row");
                var copyFolder = new TextField { value = saved, isDelayed = true };
                copyFolder.AddToClassList("grow"); copyRow.Add(copyFolder);
                copyFolder.RegisterValueChangedCallback(e => Try(() => { Controller.ChangeMachine(() => Controller.sharedStore.ReplaceOutputFolder(saved, e.newValue)); Draw(); }));
                Action(copyRow, "Browse", () => {
                    string path = EditorUtility.OpenFolderPanel("Choose copy destination", Directory.Exists(saved) ? saved : BuildStorage.Root, "");
                    if (path.Length > 0) Try(() => { Controller.ChangeMachine(() => Controller.sharedStore.ReplaceOutputFolder(saved, path)); Draw(); });
                });
                Action(copyRow, "Remove", () => Try(() => { Controller.ChangeMachine(() => Controller.sharedStore.RemoveOutputFolder(saved)); Draw(); }));
            }
            Action(card, "Add copy destination", () => {
                string path = EditorUtility.OpenFolderPanel("Add copy destination", BuildStorage.Root, "");
                if (path.Length > 0) Try(() => { Controller.ChangeMachine(() => Controller.sharedStore.AddOutputFolder(path)); Draw(); });
            });

            card = Card(parent, "Delivery recipe", "Choose a starting point, then reveal only the settings you need.");
            var recipes = Box(card, "row wrap");
            string[] labels = { "Fast iteration", "Portable host", "Gzip server", "Brotli / HTTPS" };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                Action(recipes, labels[i], () => {
                    Settings.Recipe(index); Controller.Preset.development = index == 0;
                    Save(); Draw();
                });
            }
            var inherit = new Toggle("Use this preset's WebGL settings") { value = Settings.overrideSettings };
            inherit.RegisterValueChangedCallback(e => { Settings.overrideSettings = e.newValue; Save(); Draw(); }); card.Add(inherit);
            var effective = Settings.overrideSettings ? Settings : WebBuildSettings.Capture();
            Text(card, effective.compression + "  /  " + (effective.fallback ? "JavaScript fallback" : "native decompression") + "  /  cache " + (effective.cache ? "on" : "off"), "summary");
            if (!Settings.overrideSettings) Text(card, "Using the active Unity Player Settings. Recipe changes apply only during this preset's build.", "muted");
            if (effective.compression == WebGLCompressionFormat.Brotli) Notice(card, "Brotli is intended for HTTPS hosting. Use Gzip for a dependable HTTP preview.");
            else if (effective.compression != WebGLCompressionFormat.Disabled && !effective.fallback) Text(card, "Your host must serve compressed payloads with matching Content-Encoding headers.", "muted");
            else if (effective.fallback) Text(card, "Portable hosting uses Unity's fallback decompressor. This trades startup work for simpler hosting.", "muted");
            var options = new Foldout { text = "More settings", value = false }; card.Add(options);
            var web = new VisualElement(); options.Add(web); web.SetEnabled(Settings.overrideSettings);
            Enum(web, "Compression", Settings.compression, value => { Settings.compression = (WebGLCompressionFormat)value; Save(); Draw(); });
            Toggle(web, "Decompression fallback", Settings.fallback, value => { Settings.fallback = value; Save(); });
            Toggle(web, "Browser data cache", Settings.cache, value => { Settings.cache = value; Save(); });
            Toggle(web, "Content hashes in filenames", Settings.hashedNames, value => { Settings.hashedNames = value; Save(); });
            Enum(web, "Exception support", Settings.exceptions, value => { Settings.exceptions = (WebGLExceptionSupport)value; Save(); });
            Toggle(options, "Development build", Controller.Preset.development, value => { Controller.Preset.development = value; Save(); });
            Toggle(options, "Project-only output override", Controller.Preset.overrideMachineOutput, value => { Controller.Preset.overrideMachineOutput = value; Save(); Draw(); });
            Field(options, "Project output folder", Controller.Preset.outputFolder, value => { Controller.Preset.outputFolder = value; Save(); });
            Field(options, "Filename tokens", Controller.Preset.namingTemplate, value => { Controller.Preset.namingTemplate = value; Save(); });
            Text(options, "Tokens: {package}, {version}, {code}, {preset}, {target}. A matching name replaces the previous release after a successful build.", "muted");
            var budget = new FloatField("Download budget (MB)") { value = Settings.budgetMB, isDelayed = true };
            budget.RegisterValueChangedCallback(e => { Settings.budgetMB = Mathf.Max(0, e.newValue); Save(); }); options.Add(budget);
            Text(options, "Template: " + PlayerSettings.WebGL.template + ". Rendering, memory, texture formats and remote content remain in Unity Player Settings.", "muted");
            Action(options, "Open Player Settings", () => SettingsService.OpenProjectSettings("Project/Player"));
            Action(options, "Export standalone Build Automator", ExportTool);
            card = Card(parent, "Ready to build", BuildJobs.Scenes().Length + " enabled scenes from the active scene list / Build Profile.");
            readiness = Text(card, "", "muted");
            var actions = Box(card, "row");
            build = Action(actions, "Build WebGL", () => Controller.QueueBuild(false)); build.AddToClassList("primary");
            buildPreview = Action(actions, "Build & Preview", () => Controller.QueueBuild(true));
            Text(card, "Successful builds publish atomically. Temporary WebGL settings are restored even after interruption.", "muted");
        }
        private void DrawArtifact(VisualElement parent)
        {
            var card = Card(parent, "Release artifact", "Preview and package a previous build without rebuilding.");
            Text(card, string.IsNullOrEmpty(artifact) ? "No release selected yet." : artifact, "path");
            var row = Box(card, "row wrap");
            Action(row, "Use latest build", () => { artifact = Latest()?.path ?? ""; analysis = null; Draw(); Analyze(); });
            Action(row, "Choose build folder", () => {
                string path = EditorUtility.OpenFolderPanel("Select WebGL build containing index.html", artifact, "");
                if (path.Length > 0) Try(() => { artifact = WebArtifacts.Root(path); analysis = null; Draw(); Analyze(); });
            });
            Action(row, "Analyze files", Analyze).AddToClassList("file-operation");
            Action(row, "Reveal", () => Try(() => EditorUtility.RevealInFinder(WebArtifacts.Root(artifact))));
            var history = Controller.local.history.Where(h => h.target == "WebGL" && h.result == "Succeeded").Take(15).ToList();
            if (history.Count > 0)
            {
                var items = new List<string> { "Choose a previous release…" };
                items.AddRange(history.Select((h, i) => (i + 1) + ". " + h.version + " · " + h.preset + " · " + h.utc));
                var choice = new PopupField<string>("History", items, 0); card.Add(choice);
                choice.RegisterValueChangedCallback(e => {
                    int index = items.IndexOf(e.newValue) - 1;
                    if (index >= 0) { artifact = history[index].path; analysis = null; Draw(); Analyze(); }
                });
            }
        }
        private void DrawPreview(VisualElement parent)
        {
            DrawArtifact(parent);
            var card = Card(parent, "Browser preview", "Local HTTP with Unity-aware MIME and compression headers. No extra tools to install.");
            var port = new IntegerField("Port (0 = automatic)") { value = previewPort, isDelayed = true };
            port.RegisterValueChangedCallback(e => previewPort = e.newValue); card.Add(port);
            Toggle(card, "Cross-origin isolation (COOP / COEP)", isolate, value => isolate = value);
            Text(card, "Enable isolation for threaded WebAssembly; external resources must support it. Restart preview after changing these settings.", "muted");
            var row = Box(card, "row wrap");
            Action(row, "Start & open browser", () => Try(() => {
                if (Controller.Conflicting) throw new InvalidOperationException("Wait for the build or file operation to finish.");
                WebPreview.Start(artifact, previewPort, isolate); Application.OpenURL(WebPreview.Url);
            })).AddToClassList("file-operation");
            Action(row, "Open browser", () => { if (WebPreview.Running) Application.OpenURL(WebPreview.Url); });
            Action(row, "Copy URL", () => EditorGUIUtility.systemCopyBuffer = WebPreview.Url);
            Action(row, "Stop server", WebPreview.Stop);
            previewStatus = Text(card, "", "summary");
            Text(card, "Preview stays available when this window closes; it stops on script reload or Editor exit. It is accessible only on this computer. Rebuilding stops the previous server.", "muted");
            DrawAnalysis(parent);
        }
        private void DrawShip(VisualElement parent)
        {
            DrawArtifact(parent);
            DrawAnalysis(parent);
            var card = Card(parent, "Release handoff", "An upload-ready ZIP and hosting instructions generated from the actual build files.");
            var row = Box(card, "row wrap");
            var zipButton = Action(row, "Export upload ZIP", () => {
                string destination = EditorUtility.SaveFilePanel("Export WebGL ZIP", Path.GetDirectoryName(artifact), Path.GetFileName(artifact), "zip");
                if (destination.Length == 0) return;
                string source = artifact;
                Controller.StartOperation("Packaging WebGL", async token => {
                    await Task.Run(() => WebArtifacts.ExportZip(source, destination, token), token);
                    Controller.SetMessage("Upload ZIP ready: " + destination, MessageType.Info);
                    EditorUtility.RevealInFinder(destination);
                });
            });
            zipButton.AddToClassList("primary"); zipButton.AddToClassList("file-operation");
            Action(row, "Save hosting kit", () => {
                string destination = EditorUtility.OpenFolderPanel("Save _headers, nginx snippet and release checklist", BuildStorage.Root, "");
                if (destination.Length == 0) return;
                string source = artifact;
                string[] names = { "_headers", "nginx-webgl.conf", "DEPLOY.txt" };
                if (names.Any(n => File.Exists(Path.Combine(destination, n))) && !EditorUtility.DisplayDialog("Replace hosting kit files?", "The selected folder already contains hosting kit files.", "Replace", "Cancel")) return;
                Controller.StartOperation("Preparing hosting kit", async token => {
                    var kit = await Task.Run(() => WebArtifacts.HostingKit(WebArtifacts.Analyze(source, token)), token);
                    foreach (var file in kit) { token.ThrowIfCancellationRequested(); File.WriteAllText(Path.Combine(destination, file.Key), file.Value); }
                    Controller.SetMessage("Hosting kit saved: " + destination, MessageType.Info);
                    EditorUtility.RevealInFinder(destination);
                });
            }).AddToClassList("file-operation");
            Text(card, "ZIP places index.html at its root. Upload it to an HTML game host, or extract it to your server. Hosting kit files are saved separately; review and install the configuration for your host.", "muted");
            var checklist = new Foldout { text = "Before you publish", value = false }; card.Add(checklist);
            foreach (string item in new[] { "Verify HTTPS, wasm MIME and gzip / Brotli response headers.", "Test cold load, fullscreen, audio, save data and remote asset CORS.", "Confirm the intended mobile browsers and iframe / host restrictions.", "Use a portable recipe when your host cannot set compression headers.", "Keep index.html fresh; cache immutable assets only with verified content hashes." })
                Text(checklist, "• " + item, "muted");
            Action(checklist, "Unity deployment documentation", () => Application.OpenURL("https://docs.unity3d.com/2022.3/Documentation/Manual/webgl-deploying.html"));
        }
        private void DrawAnalysis(VisualElement parent)
        {
            var card = Card(parent, "Download lens", "Actual local artifact sizes; remote assets and runtime memory are not included.");
            if (analysis == null) { Text(card, "Select a build and Analyze files to see payload sizes and hosting checks.", "muted"); return; }
            float mb = analysis.bytes / 1000000f;
            Text(card, mb.ToString("0.00") + " MB total  ·  " + (analysis.payloadBytes / 1000000f).ToString("0.00") + " MB in Build/  ·  " + analysis.files.Count + " files", "summary");
            if (Settings.budgetMB > 0)
            {
                var bar = new ProgressBar { title = mb.ToString("0.0") + " / " + Settings.budgetMB.ToString("0.0") + " MB budget", value = Mathf.Clamp(mb / Settings.budgetMB * 100, 0, 100) };
                card.Add(bar);
                if (mb > Settings.budgetMB) Notice(card, "Download budget exceeded by " + (mb - Settings.budgetMB).ToString("0.00") + " MB. Inspect the largest files below.");
            }
            var previous = Controller.local.history.FirstOrDefault(h => h.target == "WebGL" && h.result == "Succeeded" && !string.Equals(h.path, artifact, StringComparison.OrdinalIgnoreCase));
            if (previous != null) Text(card, "Size difference from previous distinct release: " + ((analysis.bytes - previous.bytes) / 1000000f).ToString("+0.00;-0.00;0.00") + " MB (" + previous.version + ").", "muted");
            var speed = new FloatField("Connection (Mbps)") { value = connectionMbps, isDelayed = true }; card.Add(speed);
            var estimate = Text(card, "", "muted");
            System.Action updateEstimate = () => estimate.text = "Ideal transfer floor: " + (analysis.bytes * 8d / (Math.Max(0.1f, connectionMbps) * 1000000d)).ToString("0.0") + " seconds. This is not measured startup time.";
            speed.RegisterValueChangedCallback(e => { connectionMbps = Mathf.Max(0.1f, e.newValue); updateEstimate(); }); updateEstimate();
            foreach (string warning in analysis.warnings) Notice(card, warning);
            var files = new Foldout { text = "Largest files / response headers", value = false }; card.Add(files);
            foreach (var file in analysis.files.Take(12))
            {
                Text(files, (file.bytes / 1000000f).ToString("0.00") + " MB  ·  " + file.name, "path");
                Text(files, WebArtifacts.Mime(file.name) + (WebArtifacts.Encoding(file.name).Length > 0 ? "  /  Content-Encoding: " + WebArtifacts.Encoding(file.name) : "  /  no Content-Encoding"), "muted");
            }
        }
        private void Analyze()
        {
            string source = artifact;
            Controller.StartOperation("Analyzing WebGL", async token => {
                var result = await Task.Run(() => WebArtifacts.Analyze(source, token), token);
                if (this == null || artifact != source) return;
                analysis = result; Draw();
                Controller.SetMessage("Analysis ready: " + result.files.Count + " local files.", MessageType.Info);
            });
        }
        private void UpdateStatus()
        {
            if (status == null) return;
            status.text = Controller.message;
            cancel?.SetEnabled(Controller.scheduler.ForegroundBusy);
            rootVisualElement.Query<Button>(className: "file-operation").ForEach(button => button.SetEnabled(!Controller.Conflicting));
            if (previewStatus != null) previewStatus.text = WebPreview.Running ? WebPreview.Url + "\n" + WebPreview.Root + "\n" + WebPreview.Requests + " requests  ·  " + (WebPreview.Bytes / 1000000f).ToString("0.00") + " MB served" + (WebPreview.Error.Length > 0 ? "\n" + WebPreview.Error : "") : "Preview is stopped.";
            if (readiness == null || build == null || buildPreview == null || page != 0) return;
            var errors = BuildJobs.Validate(Controller.Preset, Controller.version, Controller.code, false, false, "");
            if (BuildJobs.Target != BuildTarget.WebGL) errors.Insert(0, "Select WebGL in Unity to enable these build actions.");
            readiness.text = errors.Count == 0 ? "Ready. The reviewed version and settings will be used for this build." : string.Join("\n", errors);
            bool enabled = errors.Count == 0 && !Controller.Conflicting;
            build.SetEnabled(enabled); buildPreview.SetEnabled(enabled);
        }
        private void Save() { Controller.SavePresets(); UpdateStatus(); }
        private void Try(System.Action action)
        {
            try { action(); }
            catch (Exception e) { Controller.SetMessage(e.Message, MessageType.Warning); }
        }
        private void ExportTool()
        {
            string path = EditorUtility.SaveFilePanel("Export standalone tool", "", "BuildAutomator-2.5.1", "unitypackage");
            if (path.Length > 0) Try(() => BuildDashboardWindow.ExportStandalonePackage(path));
        }
        private static VisualElement Box(VisualElement parent, string classes)
        {
            var box = new VisualElement(); foreach (string cls in classes.Split(' ')) box.AddToClassList(cls); parent.Add(box); return box;
        }
        private static VisualElement Card(VisualElement parent, string title, string subtitle)
        {
            var card = Box(parent, "card"); Text(card, title, "card-title"); Text(card, subtitle, "muted"); return card;
        }
        private static Label Text(VisualElement parent, string text, string cls)
        {
            var label = new Label(text); label.AddToClassList(cls); parent.Add(label); return label;
        }
        private static void Notice(VisualElement parent, string text) { parent.Add(new HelpBox(text, HelpBoxMessageType.Info)); }
        private static Button Action(VisualElement parent, string text, System.Action callback)
        {
            var button = new Button(callback) { text = text }; parent.Add(button); return button;
        }
        private static void Toggle(VisualElement parent, string text, bool value, System.Action<bool> callback)
        {
            var field = new Toggle(text) { value = value }; field.RegisterValueChangedCallback(e => callback(e.newValue)); parent.Add(field);
        }
        private static void Field(VisualElement parent, string text, string value, System.Action<string> callback)
        {
            var field = new TextField(text) { value = value, isDelayed = true }; field.RegisterValueChangedCallback(e => callback(e.newValue)); parent.Add(field);
        }
        private static void Enum(VisualElement parent, string text, System.Enum value, System.Action<System.Enum> callback)
        {
            var field = new EnumField(text, value); field.RegisterValueChangedCallback(e => callback(e.newValue)); parent.Add(field);
        }
    }
}
