using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using E = AdsCallMigrationEngine;

public sealed class AdsCallMigrationWindow : EditorWindow
{
    [Serializable] private sealed class Settings
    {
        public int version = 1;
        public string scope = "Assets";
        public string excludedFolders = "";
        public bool showAll;
        public string lastSummary = "Scan gameplay scripts for calls to older ad managers.";
        public List<E.Rule> rules = new List<E.Rule>();
    }
    private const string SettingsPath = "ProjectSettings/AdsCallerAdCallMigration.json";
    private Settings settings;
    private E.Scan scan;
    private E.Plan plan;
    private string search = "", status = "", previewText = "";
    private Vector2 scroll, previewScroll;
    private readonly HashSet<string> selected = new HashSet<string>();
    private readonly HashSet<string> expanded = new HashSet<string>();
    private readonly HashSet<string> expandedReferences = new HashSet<string>();
    private bool showNotes;
    private string lastBackup;
    private string scanEnvironment;

    [MenuItem("Ads Caller/Ad Call Migration")]
    public static void ShowWindow()
    {
        var window = GetWindow<AdsCallMigrationWindow>("Ad Call Migration");
        window.minSize = new Vector2(760, 560);
        window.Show();
    }
    public static string Summary
    {
        get
        {
            try { return File.Exists(SettingsPath) ? JsonUtility.FromJson<Settings>(File.ReadAllText(SettingsPath)).lastSummary : "Scan gameplay scripts for calls to older ad managers."; }
            catch { return "Open Ad Call Migration to scan gameplay scripts."; }
        }
    }
    private void OnEnable()
    {
        try { settings = File.Exists(SettingsPath) ? JsonUtility.FromJson<Settings>(File.ReadAllText(SettingsPath)) : new Settings(); }
        catch (Exception e) { settings = new Settings(); status = "Could not load saved mappings: " + e.Message; }
        if (settings == null) settings = new Settings();
        if (settings.rules == null) settings.rules = new List<E.Rule>();
        lastBackup = E.LatestBackup();
        EditorApplication.projectChanged -= ProjectChanged;
        EditorApplication.projectChanged += ProjectChanged;
    }
    private void OnDisable() { EditorApplication.projectChanged -= ProjectChanged; }
    private void ProjectChanged()
    {
        if (scan == null) return;
        scan = null; InvalidatePlan();
        status = "Project assets changed. Scan again before replacing calls.";
        Repaint();
    }
    private static string EnvironmentKey() => EditorUserBuildSettings.activeBuildTarget + "|" + string.Join("|",
        CompilationPipeline.GetAssemblies(AssembliesType.Player).OrderBy(a => a.name).Select(a => a.name + ":" + string.Join(",", a.defines)));
    private void SaveSettings() => File.WriteAllText(SettingsPath, JsonUtility.ToJson(settings, true));
    private void InvalidatePlan() { plan = null; previewText = ""; }
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception e) { status = e.GetBaseException().Message; Debug.LogWarning("[Ads Caller Migration] " + status); }
        finally { EditorUtility.ClearProgressBar(); }
    }

    private void OnGUI()
    {
        if (settings == null) OnEnable();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("Migrate old ad calls to AdsMediator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Select a manager, map each method once, then preview and replace its matching calls. Spelling and argument values do not need to match AdsMediator. Unmapped methods stay unchanged.", MessageType.Info);
        EditorGUI.BeginChangeCheck();
        settings.scope = EditorGUILayout.TextField(new GUIContent("Scan folder", "An Assets folder containing your gameplay scripts."), settings.scope);
        settings.excludedFolders = EditorGUILayout.TextField(new GUIContent("Exclude folders", "Semicolon-separated Assets paths. Add other vendor SDK or wrapper implementation folders here."), settings.excludedFolders);
        if (EditorGUI.EndChangeCheck()) { scan = null; InvalidatePlan(); Guard(SaveSettings); }
        EditorGUILayout.LabelField("Ads Caller, known SDKs, Plugins, Editor and Tests folders are excluded.", EditorStyles.wordWrappedMiniLabel);
        using (new EditorGUI.DisabledScope(EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("Scan Project", GUILayout.Height(30))) Guard(ScanProject);
        }
        EditorGUILayout.LabelField("Scans the active build target and current #if symbols. Change targets and rescan for other branches. Scene/prefab bindings and DLL internals are not migrated.", EditorStyles.wordWrappedMiniLabel);
        if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.Info);

        if (scan != null)
        {
            EditorGUI.BeginChangeCheck();
            settings.showAll = EditorGUILayout.ToggleLeft("Show non-ad types too (advanced)", settings.showAll);
            if (EditorGUI.EndChangeCheck()) Guard(SaveSettings);
            search = EditorGUILayout.TextField("Filter manager", search);
            var managers = scan.calls.Select(c => c.manager).Concat(scan.references.Select(r => r.manager)).Distinct().OrderBy(s => s).ToArray();
            var visible = managers.Where(m => m.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 &&
                (settings.showAll || !string.IsNullOrEmpty(search) || scan.calls.Any(c => c.manager == m && c.likely) || E.Likely(m, "", ""))).ToArray();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(visible.Length + " managers shown • " + selected.Count + " selected");
                if (GUILayout.Button("Select shown", GUILayout.Width(110))) { foreach (string manager in visible) selected.Add(manager); InvalidatePlan(); }
                if (GUILayout.Button("Clear selection", GUILayout.Width(110))) { selected.Clear(); InvalidatePlan(); }
            }
            if (visible.Length == 0) EditorGUILayout.HelpBox("No matching manager calls or type references found. Use Show all, enter a manager name, or choose a different scan folder.", MessageType.None);
            foreach (string manager in visible) DrawManager(manager);
            showNotes = EditorGUILayout.Foldout(showNotes, "Scan notes (" + scan.notes.Count + ")", true);
            if (showNotes) foreach (string note in scan.notes) EditorGUILayout.LabelField(note, EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Mapped non-rewarded calls become parameterless calls. All old arguments and their side effects are discarded. Rewarded mappings retain only the selected callbacks. Review the preview before applying.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(selected.Count == 0 || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Preview Selected Replacements", GUILayout.Height(30))) Guard(() =>
                {
                    SaveSettings();
                    plan = E.BuildPlan(scan, selected, settings.rules);
                    previewText = string.Join("\n\n", plan.preview) + "\n\nSKIPPED\n" + string.Join("\n", plan.skipped) + "\n\nNEW COMPILER ERRORS\n" + string.Join("\n", plan.errors);
                });
        }
        if (plan != null)
        {
            EditorGUILayout.LabelField(plan.count + " replacements in " + plan.files.Count + " files • " + plan.skipped.Count + " skipped", EditorStyles.boldLabel);
            if (plan.errors.Count > 0) EditorGUILayout.HelpBox("The preview introduces compiler errors. Resolve the listed issues (including any missing AdsCaller.Core assembly reference), then rescan. No files have been changed.", MessageType.Error);
            previewScroll = EditorGUILayout.BeginScrollView(previewScroll, GUILayout.Height(230));
            EditorGUILayout.TextArea(previewText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            using (new EditorGUI.DisabledScope(plan.count == 0 || plan.errors.Count > 0 || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Replace Selected Calls (" + plan.count + ")", GUILayout.Height(34))) Guard(() =>
                {
                    int count = plan.count, files = plan.files.Count;
                    if (scanEnvironment != EnvironmentKey()) throw new InvalidOperationException("Build target or scripting defines changed. Scan and preview again.");
                    lastBackup = E.Apply(plan);
                    status = "Replaced " + count + " calls in " + files + " files. Backup: " + lastBackup + ". Unity will compile the changes; inspect the Console, then rescan for remaining references.";
                    settings.lastSummary = "Last migration: " + count + " calls in " + files + " files. Rescan to check remaining references.";
                    SaveSettings(); scan = null; InvalidatePlan();
                    AssetDatabase.Refresh();
                });
        }
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(lastBackup) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("Restore Last Migration")) Guard(() =>
            {
                E.Restore(lastBackup); lastBackup = E.LatestBackup();
                status = "Last migration restored. Rescan before making further replacements.";
                settings.lastSummary = status; SaveSettings(); scan = null; InvalidatePlan(); AssetDatabase.Refresh();
            });
        EditorGUILayout.LabelField("Backups and preview reports are saved outside Assets. Restore refuses to overwrite files edited after migration.", EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.EndScrollView();
    }

    private void DrawManager(string manager)
    {
        var calls = scan.calls.Where(c => c.manager == manager).ToArray();
        var references = scan.references.Where(r => r.manager == manager).ToArray();
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool chosen = selected.Contains(manager), next = EditorGUILayout.Toggle(chosen, GUILayout.Width(18));
                if (next != chosen) { if (next) selected.Add(manager); else selected.Remove(manager); InvalidatePlan(); }
                bool open = EditorGUILayout.Foldout(expanded.Contains(manager), manager + "   —   " + calls.Length + " calls, " + references.Length + " type references", true);
                if (open) expanded.Add(manager); else expanded.Remove(manager);
            }
            if (!expanded.Contains(manager)) return;
            foreach (var group in calls.GroupBy(c => c.key).OrderBy(g => g.First().method))
            {
                var first = group.First();
                var rule = settings.rules.FirstOrDefault(r => r.key == group.Key);
                if (rule == null) { rule = new E.Rule { key = group.Key }; settings.rules.Add(rule); }
                EditorGUILayout.LabelField(first.signature + "   × " + group.Count(), EditorStyles.wordWrappedLabel);
                if (!first.resolved) EditorGUILayout.LabelField("Type unresolved: matched by explicit receiver spelling. Check the call locations carefully.", EditorStyles.wordWrappedMiniLabel);
                EditorGUI.BeginChangeCheck();
                var previousAction = rule.action;
                rule.action = (E.AdAction)EditorGUILayout.EnumPopup("Replace entire call with", rule.action);
                if (rule.action == E.AdAction.Rewarded && previousAction != E.AdAction.Rewarded) E.SuggestRewardCallbacks(rule, group);
                if (rule.action == E.AdAction.Rewarded)
                {
                    string[] choices = new[] { "Choose callback" }.Concat(first.argumentLabels.Select((label, i) => (i + 1) + ": " + label)).ToArray();
                    int success = EditorGUILayout.Popup("Reward earned callback", Math.Max(0, Math.Min(rule.successArgument + 1, choices.Length - 1)), choices);
                    rule.successArgument = success - 1;
                    choices[0] = "No failure callback";
                    rule.failureArgument = EditorGUILayout.Popup("Failure callback", Math.Max(0, Math.Min(rule.failureArgument + 1, choices.Length - 1)), choices) - 1;
                    EditorGUILayout.LabelField("Uses declared parameter order when resolved, otherwise source argument order. Parameterless lambdas are preserved in full and compiler-checked in the preview. Review the suggested success/failure order.", EditorStyles.wordWrappedMiniLabel);
                }
                if (EditorGUI.EndChangeCheck()) { InvalidatePlan(); Guard(SaveSettings); }
                int eligible = group.Count(c => E.Replacement(c, rule, out _) != null);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(eligible + " ready • " + (group.Count() - eligible) + " unmapped / manual", EditorStyles.miniLabel);
                    if (GUILayout.Button("Open first call", GUILayout.Width(110))) Open(first.source.path, first.line);
                }
            }
            if (references.Length > 0)
            {
                bool open = EditorGUILayout.Foldout(expandedReferences.Contains(manager), "Type references — review manually", true);
                if (open) expandedReferences.Add(manager); else expandedReferences.Remove(manager);
                if (open) foreach (var reference in references)
                    if (GUILayout.Button(reference.path + ":" + reference.line + "  " + reference.text, EditorStyles.miniButton)) Open(reference.path, reference.line);
            }
        }
    }
    private static void Open(string path, int line)
    {
        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
        if (script != null) AssetDatabase.OpenAsset(script, line);
    }

    private void ScanProject()
    {
        string scope = E.Relative(settings.scope.Trim()).TrimEnd('/');
        if ((scope != "Assets" && !scope.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) || !Directory.Exists(scope))
            throw new IOException("Choose an existing folder inside Assets.");
        var excluded = settings.excludedFolders.Split(';').Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => E.Relative(p.Trim()).TrimEnd('/') + "/").ToArray();
        Func<string, bool> include = path => E.Writable(path) && E.Relative(path).StartsWith(scope + "/", StringComparison.OrdinalIgnoreCase) &&
            !excluded.Any(p => E.Relative(path).StartsWith(p, StringComparison.OrdinalIgnoreCase));
        InvalidatePlan(); scan = null; selected.Clear();
        var result = new E.Scan();
        var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player);
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < assemblies.Length; index++)
        {
            var assembly = assemblies[index];
            if (!assembly.sourceFiles.Any(include)) continue;
            if (EditorUtility.DisplayCancelableProgressBar("Ad Call Migration", "Scanning " + assembly.name, (float)index / assemblies.Length)) throw new OperationCanceledException("Scan cancelled. No files changed.");
            var sources = new List<E.Source>();
            foreach (string path in assembly.sourceFiles.Where(File.Exists))
            {
                bool writable = include(path);
                sources.Add(E.ReadSource(path, assembly.defines, writable));
                if (writable) covered.Add(E.Relative(path));
            }
            E.Analyze(result, sources.ToArray(), assembly.allReferences);
        }
        foreach (string path in AssetDatabase.FindAssets("t:MonoScript", new[] { scope }).Select(AssetDatabase.GUIDToAssetPath).Where(include))
            if (!covered.Contains(E.Relative(path))) result.notes.Add(path + ": outside the active player assemblies; switch build target/defines and rescan if required.");
        result.notes.Add("Calls inside their own manager implementation are excluded. Add any other vendor implementation folders to Exclude folders.");
        result.notes.Add("Only standalone expression statements are replaceable. Conditions, return values, conditional access, expression-bodied methods and unverified reward callbacks need manual review.");
        scan = result;
        scanEnvironment = EnvironmentKey();
        settings.lastSummary = "Last scan: " + result.calls.Where(c => c.likely).Select(c => c.manager).Distinct().Count() + " candidate managers in " + covered.Count + " gameplay scripts.";
        status = settings.lastSummary + " Expand a manager to map its methods. Mappings are saved per project.";
        SaveSettings();
    }
}
