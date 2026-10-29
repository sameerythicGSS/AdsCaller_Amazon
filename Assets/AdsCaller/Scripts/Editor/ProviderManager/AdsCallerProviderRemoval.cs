using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

/// <summary>Preflight, backup and provider cleanup. No dependency on either provider's runtime assembly.</summary>
public static class AdsCallerProviderRemoval
{
    public sealed class Plan
    {
        public bool removeOnline;
        public string signature;
        public readonly List<string> assets = new List<string>();
        public readonly List<string> packages = new List<string>();
        public readonly List<string> retained = new List<string>();
        public readonly List<string> blockers = new List<string>();
        public readonly List<string> reconcile = new List<string>();
        internal readonly List<string> addressableSettings = new List<string>();
        internal readonly List<string> addressableAssets = new List<string>();
        internal readonly List<string> addressableGuids = new List<string>();
        internal readonly List<string> embedded = new List<string>();
        internal readonly Dictionary<string, string> textBefore = new Dictionary<string, string>(StringComparer.Ordinal);
        internal readonly Dictionary<string, string> textAfter = new Dictionary<string, string>(StringComparer.Ordinal);
        internal readonly HashSet<string> types = new HashSet<string>(StringComparer.Ordinal);
        internal string manifestBefore, manifestAfter;
        public bool CanApply => blockers.Count == 0;
    }
    private const string Prefix = "com.adscaller.family.";
    private const string LegacyPrefix = "com.gss.adscaller.";
    private const string Manifest = "Packages/manifest.json";
    private static string PhysicalPath(string path)
    {
        if (!path.StartsWith("Packages/", StringComparison.Ordinal)) return path;
        var package = PackageInfo.FindForAssetPath(path);
        return package == null ? path : package.resolvedPath + path.Substring(("Packages/" + package.name).Length);
    }
    private static readonly string[] OnlineRoots = {
        "Assets/AdsCaller/Scripts/Providers/Android", "Assets/AdsCaller/Scripts/Providers/Amazon",
        "Assets/AdsCaller/AndroidNetworkModule.json", "Assets/AdsCaller/Bootstrap/Main",
        "Assets/AdsCaller/Prefabs/AdsCaller.prefab", "Assets/AdsCaller/Data", "Assets/AdsCaller/Docs",
        "Assets/AdsCaller/Demo_AdsCaller",
        "Assets/AdsCaller/Scripts/Core/AdsCaller.cs", "Assets/AdsCaller/Scripts/Core/AdsCallerConfig.cs",
        "Assets/AdsCaller/Scripts/Editor/AdsCallerConfigEditor.cs", "Assets/AdsCaller/Scripts/Editor/AdsCredentialsPopup.cs",
        "Assets/AdsCaller/Scripts/Others/EnumsJar.cs", "Assets/AdsCaller/Scripts/Others/FakeLoading.cs",
        "Assets/AdsCaller/Scripts/Others/InAppCallbacks_AMZ.cs", "Assets/AdsCaller/Scripts/Others/InAppPurchaser_AMZ.cs"
    };
    private static readonly string[] CrossRoots = {
        "Assets/AdsCaller/SalonAds", "Assets/AdsCaller/Bootstrap/CrossPromo",
        "Assets/AdsCaller/Scripts/Data/AdsConfiguration.cs", "Assets/AdsCaller/Scripts/Others/BannerAnimator.cs"
    };

    public static bool ContainsPath(string root, string path) => path == root || path.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal);
    private static bool Removed(Plan plan, string path) => plan.assets.Any(root => ContainsPath(root, path))
        || plan.packages.Any(name => ContainsPath("Packages/" + name, path));
    private static void AddRoot(Plan plan, string path)
    {
        if (path.StartsWith("Packages/", StringComparison.Ordinal)) return;
        if (File.Exists(path) || Directory.Exists(path)) plan.assets.Add(path);
    }

    public static Plan Preview(bool removeOnline)
    {
        AdsCallerAssets.Refresh();
        var plan = new Plan { removeOnline = removeOnline, signature = AdsCallerProviderManager.Signature };
        if (!AdsCallerProviderManager.HasOnline || !AdsCallerProviderManager.HasCrossPromo)
        { plan.blockers.Add("Both providers must be installed. The last provider cannot be removed here."); return plan; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        { plan.blockers.Add("Finish compilation and exit Play Mode before changing providers."); return plan; }
        foreach (var root in removeOnline ? OnlineRoots : CrossRoots) AddRoot(plan, root);
        var installed = PackageInfo.GetAllRegisteredPackages();
        foreach (var package in installed.Where(p => removeOnline
            ? p.name == Prefix + "main.android" || p.name == Prefix + "main.amazon" || p.name == LegacyPrefix + "android" || p.name == LegacyPrefix + "amazon"
            : p.name == Prefix + "cross-promo" || p.name == LegacyPrefix + "crosspromo"))
            plan.packages.Add(package.name);
        var paths = AssetDatabase.GetAllAssetPaths().Where(p => !AssetDatabase.IsValidFolder(p)).ToArray();
        // Imported UPM sample configurations are writable project assets. Remove only the selected provider's types.
        foreach (var path in paths.Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && p.EndsWith(".asset", StringComparison.Ordinal)))
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;
            if (asset == null) continue;
            var script = MonoScript.FromScriptableObject(asset);
            if (script != null && Removed(plan, AssetDatabase.GetAssetPath(script))) AddRoot(plan, path);
        }
        CollectTypes(plan, paths);
        var outside = paths.Where(p => !Removed(plan, p)).ToArray();
        var identifiers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var path in outside.Where(p => p.EndsWith(".cs", StringComparison.Ordinal) && File.Exists(PhysicalPath(p))))
        {
            try { identifiers[path] = Identifiers(File.ReadAllText(PhysicalPath(path)), path); }
            catch (Exception e) { plan.blockers.Add("Cannot safely scan " + path + ": " + e.GetBaseException().Message); }
        }
        foreach (var pair in identifiers)
        {
            var references = pair.Value.Intersect(plan.types).OrderBy(n => n).ToArray();
            if (references.Length > 0) plan.blockers.Add(pair.Key + " uses " + string.Join(", ", references) + ". Migrate these calls before removal.");
        }
        if (removeOnline)
        {
            AddSdk(plan, installed, outside, identifiers, Prefix + "sdk.max", new[] { "Assets/MaxSdk" }, new[] { "MaxSdk", "MaxSdkBase", "MaxSdkCallbacks" });
            AddSdk(plan, installed, outside, identifiers, Prefix + "sdk.admob", new[] { "Assets/GoogleMobileAds", "Assets/Plugins/Android/googlemobileads-unity.aar", "Assets/Plugins/Android/GoogleMobileAdsPlugin.androidlib" }, new[] { "GoogleMobileAds" });
            AddSdk(plan, installed, outside, identifiers, Prefix + "sdk.edm", new[] { "Assets/ExternalDependencyManager" }, new[] { "GooglePlayServices", "IOSResolver", "VersionHandler", "PlayServicesResolver", "ExternalDependencyManager" }, true);
            foreach (var suffix in new[] { "sdk.max", "sdk.admob", "sdk.edm" })
                if (installed.Any(p => p.name == LegacyPrefix + suffix))
                    AddSdk(plan, installed, outside, identifiers, LegacyPrefix + suffix, Array.Empty<string>(),
                        suffix == "sdk.max" ? new[] { "MaxSdk", "MaxSdkBase", "MaxSdkCallbacks" } : suffix == "sdk.admob" ? new[] { "GoogleMobileAds" } : new[] { "GooglePlayServices", "IOSResolver", "VersionHandler" }, suffix == "sdk.edm");
            CollectAndroidDependencies(plan, paths);
        }
        else
        {
            foreach (var package in installed.Where(p => p.name == "com.unity.addressables" || p.name == "com.unity.nuget.newtonsoft-json"))
                plan.retained.Add(package.name + " — project-managed package; other systems may use it");
            CollectAddressables(plan, paths);
        }
        // Shared UPM dependencies (Addressables, Newtonsoft, uGUI) are deliberately preserved.
        // Their direct pins may be project-owned, and Core still requires uGUI.
        foreach (var package in installed.Where(p => !plan.packages.Contains(p.name)))
            foreach (var dependency in package.dependencies ?? new UnityEditor.PackageManager.DependencyInfo[0])
                if (plan.packages.Contains(dependency.name)) plan.blockers.Add(package.name + " requires " + dependency.name + ". Remove that dependency first.");
        foreach (var package in installed.Where(p => plan.packages.Contains(p.name) && p.source == UnityEditor.PackageManager.PackageSource.Embedded))
        {
            string expected = Path.GetFullPath("Packages/" + package.name);
            if (!expected.StartsWith(Path.GetFullPath("Packages") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(expected.TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(package.resolvedPath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                plan.blockers.Add("Embedded package path could not be verified: " + package.name);
            else plan.embedded.Add("Packages/" + package.name);
        }
        foreach (var path in outside.Where(p => p.EndsWith(".asmdef", StringComparison.Ordinal) || p.EndsWith(".asmref", StringComparison.Ordinal)))
        {
            if (Removed(plan, path) || !File.Exists(PhysicalPath(path))) continue;
            string text = File.ReadAllText(PhysicalPath(path));
            foreach (var assembly in removeOnline ? new[] { "AdsCaller.Android", "AdsCaller.Amazon", "AdsCaller.Firebase", "AdsCaller.AppsFlyer" } : new[] { "AdsCaller.CrossPromo" })
                if (Regex.IsMatch(text, "\"" + Regex.Escape(assembly) + "(?:\\.Editor)?\"")) plan.blockers.Add(path + " references the removed provider assembly.");
            if (paths.Where(p => Removed(plan, p) && p.EndsWith(".asmdef", StringComparison.Ordinal))
                .Select(AssetDatabase.AssetPathToGUID).Any(guid => text.Contains("\"GUID:" + guid + "\"")))
                plan.blockers.Add(path + " references a removed assembly by GUID.");
        }
        // Prefabs and scenes can be reconciled by Unity. Other consumers must be repaired before deletion.
        foreach (var path in outside.Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && !Removed(plan, p)))
        {
            string[] removedDependencies = AssetDatabase.GetDependencies(path, false).Where(p => p != path && Removed(plan, p)).ToArray();
            bool container = path.EndsWith(".prefab", StringComparison.Ordinal) || path.EndsWith(".unity", StringComparison.Ordinal);
            bool mediator = container && File.Exists(path) && File.ReadAllText(path).Contains("useAdvancedRouting:");
            if (container && removedDependencies.Any(p => !p.EndsWith(".cs", StringComparison.Ordinal)
                && !p.EndsWith(".prefab", StringComparison.Ordinal) && !p.EndsWith(".asset", StringComparison.Ordinal)))
                plan.blockers.Add(path + " uses provider media outside its provider prefab. Update these references before removal.");
            if (container && (removedDependencies.Length > 0 || mediator)) plan.reconcile.Add(path);
            else if (removedDependencies.Length > 0) plan.blockers.Add(path + " references provider assets. Update these references before removal.");
        }
        foreach (var path in outside.Where(p => p.StartsWith("Packages/", StringComparison.Ordinal) && !Removed(plan, p)))
            if (AssetDatabase.GetDependencies(path, false).Any(p => p != path && Removed(plan, p)))
                plan.blockers.Add(path + " is a read-only package asset referencing the removed provider. Update that package first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded || (!scene.isDirty && !string.IsNullOrEmpty(scene.path))) continue;
            if (scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MonoBehaviour>(true)).Any(c => c != null &&
                (c.GetType().Name == "AdsMediator" || (MonoScript.FromMonoBehaviour(c) != null && Removed(plan, AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(c)))))))
                plan.blockers.Add("Save the affected scene before removing a provider: " + (string.IsNullOrEmpty(scene.path) ? "Untitled" : scene.path));
        }
        plan.assets.Sort(StringComparer.Ordinal);
        var nested = plan.assets.Where(p => plan.assets.Any(root => root != p && ContainsPath(root, p))).ToArray();
        plan.assets.RemoveAll(nested.Contains);
        if (File.Exists(Manifest))
        {
            plan.manifestBefore = File.ReadAllText(Manifest);
            try { plan.manifestAfter = RemoveManifestDependencies(plan.manifestBefore, plan.packages); }
            catch (Exception e) { plan.blockers.Add(e.Message); }
        }
        if (plan.assets.Count == 0 && plan.packages.Count == 0) plan.blockers.Add("Provider ownership could not be resolved. No files will be removed.");
        return plan;
    }

    private static HashSet<string> Identifiers(string source, string path)
    {
        // Scan all conditional branches, including inactive platform code, without matching comments or strings.
        source = Regex.Replace(source, @"(?m)^[ \t]*#(?:if|elif|else|endif)\b[^\r\n]*", "");
        var tree = AdsMigrationRoslyn.Parse(source, path, Array.Empty<string>());
        var root = AdsMigrationRoslyn.Call(tree, "GetRoot");
        return new HashSet<string>(AdsMigrationRoslyn.Nodes(root)
            .Where(n => AdsMigrationRoslyn.Kind(n) == "IdentifierNameSyntax" || AdsMigrationRoslyn.Kind(n) == "GenericNameSyntax")
            .Select(AdsMigrationRoslyn.Name), StringComparer.Ordinal);
    }

    private static void CollectAddressables(Plan plan, string[] paths)
    {
        var removedGuids = new HashSet<string>(paths.Where(p => Removed(plan, p)).Select(AssetDatabase.AssetPathToGUID));
        foreach (var guid in AssetDatabase.FindAssets("t:AddressableAssetSettings", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var settings = AssetDatabase.LoadMainAssetAtPath(path);
            bool affected = false;
            foreach (var group in AdsMigrationRoslyn.Items(AdsMigrationRoslyn.Get(settings, "groups")))
            {
                if (!(group is UnityEngine.Object asset) || asset == null) continue;
                var entries = AdsMigrationRoslyn.Items(AdsMigrationRoslyn.Get(group, "entries"));
                var selected = entries.Select(e => AdsMigrationRoslyn.Text(AdsMigrationRoslyn.Get(e, "guid"))).Where(removedGuids.Contains).ToArray();
                if (selected.Length == 0) continue;
                plan.addressableGuids.AddRange(selected); affected = true;
                plan.addressableAssets.Add(AssetDatabase.GetAssetPath(asset));
            }
            if (affected) { plan.addressableSettings.Add(path); plan.addressableAssets.Add(path); }
        }
    }

    private static HashSet<string> AndroidSpecs(IEnumerable<string> paths)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths.Where(p => p.EndsWith(".xml", StringComparison.Ordinal) && File.Exists(PhysicalPath(p))))
            foreach (Match match in Regex.Matches(File.ReadAllText(PhysicalPath(path)), "<androidPackage\\s+spec=[\"'](?<spec>[^\"']+)[\"']"))
            {
                var parts = match.Groups["spec"].Value.Split(':');
                if (parts.Length >= 2) result.Add(parts[0] + ":" + parts[1]);
            }
        return result;
    }

    private static void CollectAndroidDependencies(Plan plan, string[] paths)
    {
        var exclusive = AndroidSpecs(paths.Where(p => Removed(plan, p)));
        exclusive.ExceptWith(AndroidSpecs(paths.Where(p => !Removed(plan, p))));
        foreach (var path in paths.Where(p => p.StartsWith("Assets/Plugins/Android/", StringComparison.Ordinal) && p.EndsWith(".gradle", StringComparison.Ordinal) && !Removed(plan, p)))
        {
            string before = File.ReadAllText(path);
            string after = Regex.Replace(before, @"(?m)^[ \t]*(?:implementation|api|compile)[ \t]+['"" ](?<group>[^:'""\r\n ]+):(?<artifact>[^:'""\r\n ]+):[^\r\n]*(?:\r?\n|$)", match =>
                exclusive.Contains(match.Groups["group"].Value + ":" + match.Groups["artifact"].Value) ? "" : match.Value);
            if (after == before) continue;
            plan.textBefore[path] = before; plan.textAfter[path] = after;
            plan.reconcile.Add(path);
        }
        foreach (var path in paths.Where(p => p.StartsWith("Assets/Plugins/Android/", StringComparison.Ordinal) && !Removed(plan, p)
            && (p.EndsWith(".aar", StringComparison.Ordinal) || p.EndsWith(".jar", StringComparison.Ordinal))))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (exclusive.Any(spec => { var parts = spec.Split(':'); return name.StartsWith(parts[0] + "." + parts[1] + "-", StringComparison.Ordinal); }))
                AddRoot(plan, path);
        }
    }

    private static void CollectTypes(Plan plan, string[] paths)
    {
        foreach (var path in paths.Where(p => Removed(plan, p) && p.EndsWith(".cs", StringComparison.Ordinal) && File.Exists(PhysicalPath(p))))
        {
            var source = Regex.Replace(File.ReadAllText(PhysicalPath(path)), @"(?m)^[ \t]*#(?:if|elif|else|endif)\b[^\r\n]*", "");
            var tree = AdsMigrationRoslyn.Parse(source, path, Array.Empty<string>());
            var root = AdsMigrationRoslyn.Call(tree, "GetRoot");
            foreach (var node in AdsMigrationRoslyn.Nodes(root).Where(n =>
                new[] { "ClassDeclarationSyntax", "StructDeclarationSyntax", "EnumDeclarationSyntax", "InterfaceDeclarationSyntax" }.Contains(AdsMigrationRoslyn.Kind(n))
                && AdsMigrationRoslyn.Kind(AdsMigrationRoslyn.Get(n, "Parent")) != "ClassDeclarationSyntax"))
                plan.types.Add(AdsMigrationRoslyn.Name(node));
        }
    }

    private static void AddSdk(Plan plan, PackageInfo[] installed, string[] paths, Dictionary<string, HashSet<string>> identifiers,
        string packageName, string[] roots, string[] tokens, bool resolver = false)
    {
        Func<string, bool> inSdk = p => roots.Any(r => ContainsPath(r, p)) || ContainsPath("Packages/" + packageName, p);
        string consumer = identifiers.FirstOrDefault(p => !Removed(plan, p.Key) && !inSdk(p.Key) && p.Value.Overlaps(tokens)).Key;
        string[] assemblies = packageName.EndsWith("sdk.max", StringComparison.Ordinal) ? new[] { "MaxSdk.Scripts" }
            : packageName.EndsWith("sdk.admob", StringComparison.Ordinal) ? new[] { "GoogleMobileAds", "GoogleMobileAds.Editor" }
            : new[] { "Google.VersionHandler", "Google.JarResolver", "Google.IOSResolver" };
        if (consumer == null)
            foreach (var binary in paths.Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !Removed(plan, p) && !inSdk(p)))
            {
                var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == Path.GetFileNameWithoutExtension(binary));
                if (loaded != null && loaded.GetReferencedAssemblies().Any(a => assemblies.Contains(a.Name))) { consumer = binary; break; }
            }
        if (consumer == null)
        {
            var sdkGuids = paths.Where(p => inSdk(p) && p.EndsWith(".asmdef", StringComparison.Ordinal)).Select(AssetDatabase.AssetPathToGUID).ToArray();
            consumer = paths.Where(p => !Removed(plan, p) && !inSdk(p) && (p.EndsWith(".asmdef", StringComparison.Ordinal) || p.EndsWith(".asmref", StringComparison.Ordinal)))
                .FirstOrDefault(p => { string text = File.ReadAllText(PhysicalPath(p)); return assemblies.Any(a => text.Contains("\"" + a + "\"")) || sdkGuids.Any(g => text.Contains("\"GUID:" + g + "\"")); });
        }
        if (consumer == null)
            consumer = installed.Where(p => !plan.packages.Contains(p.name) && p.name != packageName)
                .FirstOrDefault(p => (p.dependencies ?? new UnityEditor.PackageManager.DependencyInfo[0]).Any(d => d.name == packageName))?.name;
        if (consumer == null)
            consumer = paths.FirstOrDefault(p => !Removed(plan, p) && !inSdk(p) && AssetDatabase.GetDependencies(p, false).Any(inSdk));
        if (consumer == null && resolver)
            consumer = paths.FirstOrDefault(p => !Removed(plan, p) && !inSdk(p) && p.EndsWith("Dependencies.xml", StringComparison.Ordinal));
        if (consumer != null) { plan.retained.Add(packageName.Replace(Prefix, "") + " — also used by " + consumer); return; }
        foreach (var root in roots) AddRoot(plan, root);
        if (installed.Any(p => p.name == packageName)) plan.packages.Add(packageName);
    }

    // Restrict edits to the standard string-valued top-level dependencies object; preserve every other manifest field.
    public static string RemoveManifestDependencies(string json, IEnumerable<string> names)
    {
        var section = Regex.Match(json, "\"dependencies\"\\s*:\\s*\\{(?<body>[^{}]*)\\}");
        if (!section.Success) throw new InvalidOperationException("Package manifest dependencies could not be read safely.");
        var body = section.Groups["body"];
        const string entryPattern = "\"(?<name>(?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\"(?:[^\"\\\\]|\\\\.)*\"";
        var entries = Regex.Matches(body.Value, entryPattern).Cast<Match>().ToArray();
        if (Regex.Replace(Regex.Replace(body.Value, entryPattern, ""), "[\\s,]", "").Length != 0)
            throw new InvalidOperationException("Package manifest has unsupported dependency values; no packages were changed.");
        var removing = new HashSet<string>(names, StringComparer.Ordinal);
        if (!entries.Any(e => removing.Contains(e.Groups["name"].Value))) return json;
        string replacement = "\n" + string.Join(",\n", entries.Where(e => !removing.Contains(e.Groups["name"].Value)).Select(e => "    " + e.Value)) + "\n  ";
        return json.Substring(0, body.Index) + replacement + json.Substring(body.Index + body.Length);
    }

    public static string Apply(Plan reviewed)
    {
        if (reviewed == null || !reviewed.CanApply) throw new InvalidOperationException("Resolve preflight issues before removal.");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;
        // Re-scan immediately before mutation: dependencies or scenes may have changed since the preview.
        var plan = Preview(reviewed.removeOnline);
        if (!plan.CanApply || plan.signature != reviewed.signature || !plan.assets.SequenceEqual(reviewed.assets)
            || !plan.packages.SequenceEqual(reviewed.packages) || !plan.reconcile.SequenceEqual(reviewed.reconcile)
            || !plan.addressableGuids.SequenceEqual(reviewed.addressableGuids)
            || !plan.embedded.SequenceEqual(reviewed.embedded)
            || plan.textBefore.Any(p => !reviewed.textBefore.TryGetValue(p.Key, out var value) || value != p.Value)
            || plan.manifestBefore != reviewed.manifestBefore)
            throw new InvalidOperationException("The project changed since the preview. Review a fresh removal plan.");
        AssetDatabase.SaveAssets();
        string backup = "AdsCallerProviderBackups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        Directory.CreateDirectory(backup);
        var backedUp = new HashSet<string>(StringComparer.Ordinal);
        Action<string> copy = path =>
        {
            if (!backedUp.Add(path) || !File.Exists(path)) return;
            string target = Path.Combine(backup, path); Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(path, target, true);
        };
        foreach (var root in plan.assets.Concat(plan.embedded))
        {
            if (Directory.Exists(root)) foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) copy(path);
            else copy(root);
            copy(root + ".meta");
        }
        foreach (var path in plan.reconcile) { copy(path); copy(path + ".meta"); }
        foreach (var path in plan.addressableAssets) { copy(path); copy(path + ".meta"); }
        foreach (var path in new[] { Manifest, "Packages/packages-lock.json", AdsCallerProviderManager.ReceiptPath, "ProjectSettings/AdsCallerSetup.json" }) copy(path);
        File.WriteAllText(backup + "/README.txt", "Close Unity before restoring. Copy Assets, Packages and ProjectSettings from this folder over the project. Reopen Unity to reimport.\n");
        var activeScene = SceneManager.GetActiveScene();
        var originallyLoadedScenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
            .Where(s => s.isLoaded && !string.IsNullOrEmpty(s.path)).Select(s => s.path).ToArray();
        EditorApplication.LockReloadAssemblies();
        try
        {
            foreach (var path in plan.addressableSettings)
            {
                var settings = AssetDatabase.LoadMainAssetAtPath(path);
                foreach (var guid in plan.addressableGuids) AdsMigrationRoslyn.Call(settings, "RemoveAssetEntry", guid);
                EditorUtility.SetDirty(settings);
            }
            foreach (var path in plan.reconcile.OrderBy(p => p.EndsWith(".prefab", StringComparison.Ordinal) ? 0 : 1))
            {
                if (plan.textAfter.TryGetValue(path, out var content)) File.WriteAllText(path, content);
                else Reconcile(path, plan);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in plan.assets)
                    if (!AssetDatabase.DeleteAsset(path)) throw new IOException("Could not remove " + path);
                foreach (var path in plan.embedded) Directory.Delete(path, true); // Exact project-local paths verified during preflight.
                if (plan.manifestAfter != plan.manifestBefore && plan.manifestAfter != null) File.WriteAllText(Manifest, plan.manifestAfter);
                AdsCallerProviderManager.Remember(plan.removeOnline ? "CrossPromo" : "Online", plan.removeOnline ? "-|crosspromo" : plan.signature.Split('|')[0] + "|-");
                UpdateSetupReceipt(plan.removeOnline);
            }
            finally { AssetDatabase.StopAssetEditing(); }
        }
        catch
        {
            Debug.LogError("[Ads Caller] Removal failed. Restoring files. Recovery backup: " + Path.GetFullPath(backup));
            foreach (var path in backedUp.Where(p => File.Exists(Path.Combine(backup, p))))
            { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.Copy(Path.Combine(backup, path), path, true); }
            foreach (var path in plan.reconcile.Where(p => p.EndsWith(".unity", StringComparison.Ordinal) && originallyLoadedScenes.Contains(p)))
            {
                var scene = SceneManager.GetSceneByPath(path);
                bool active = scene == activeScene;
                if (scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                var restored = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                if (active) activeScene = restored;
            }
            throw;
        }
        finally
        {
            try { if (activeScene.IsValid() && activeScene.isLoaded) SceneManager.SetActiveScene(activeScene); }
            finally { EditorApplication.UnlockReloadAssemblies(); AssetDatabase.Refresh(); AdsCallerAssets.Refresh(); }
        }
        if (plan.manifestAfter != plan.manifestBefore) UnityEditor.PackageManager.Client.Resolve();
        Debug.Log("[Ads Caller] Provider removed. Recovery backup: " + Path.GetFullPath(backup));
        return Path.GetFullPath(backup);
    }

    private static void UpdateSetupReceipt(bool removeOnline)
    {
        const string path = "ProjectSettings/AdsCallerSetup.json";
        if (!File.Exists(path)) return;
        var receipt = JsonUtility.FromJson<AdsCallerSetup.Receipt>(File.ReadAllText(path));
        if (receipt == null) return;
        receipt.main = !removeOnline; receipt.crossPromo = removeOnline;
        File.WriteAllText(path, JsonUtility.ToJson(receipt, true));
    }

    private static void Reconcile(string path, Plan plan)
    {
        if (path.EndsWith(".prefab", StringComparison.Ordinal))
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try { if (Clean(root, plan) && PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new IOException("Could not save " + path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        else
        {
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                bool changed = false;
                foreach (var root in scene.GetRootGameObjects()) changed |= Clean(root, plan);
                if (changed && !EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + path);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
    }

    private static bool Clean(GameObject root, Plan plan)
    {
        bool changed = false;
        // Delete selected provider instances together with their owned UI hierarchy.
        // Preserve external prefab variants by unpacking their provider link before pruning components.
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform == null) continue;
            var go = transform.gameObject;
            string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (string.IsNullOrEmpty(source) || !Removed(plan, source)) continue;
            if (PrefabUtility.IsAnyPrefabInstanceRoot(go) && go != root)
            { UnityEngine.Object.DestroyImmediate(go); changed = true; continue; }
            if (go == root && !EditorSceneManager.IsPreviewSceneObject(root))
            { UnityEngine.Object.DestroyImmediate(root); return true; }
            var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            if (outer != null) { PrefabUtility.UnpackPrefabInstance(outer, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction); changed = true; }
        }
        var selected = root.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null)
            .Where(c => { var script = MonoScript.FromMonoBehaviour(c); return script != null && Removed(plan, AssetDatabase.GetAssetPath(script)); }).ToList();
        while (selected.Count > 0)
        {
            // Unity refuses to delete a required component while its dependent still exists.
            var next = selected.FirstOrDefault(c => !selected.Any(other => other != c && other.gameObject == c.gameObject
                && other.GetType().GetCustomAttributes(typeof(RequireComponent), true).Cast<RequireComponent>()
                    .SelectMany(r => new[] { r.m_Type0, r.m_Type1, r.m_Type2 }).Any(t => t != null && t.IsAssignableFrom(c.GetType()))));
            if (next == null) throw new InvalidOperationException("Provider components have circular requirements. Remove their provider object before continuing.");
            selected.Remove(next); UnityEngine.Object.DestroyImmediate(next);
            if (next != null) throw new InvalidOperationException("Unity could not detach a provider component. No provider files will be removed.");
            changed = true;
        }
        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null) continue;
            if (!(component is MonoBehaviour)) continue;
            var so = new SerializedObject(component);
            if (component.GetType().Name == "AdsMediator")
            {
                SetEnum(so, "adsMode", plan.removeOnline ? "CrossPromo" : "Online");
                SetBool(so, "useAdvancedRouting", false);
                SetBool(so, "useAdsCallerRewarded", !plan.removeOnline);
                foreach (var field in new[] { "bannerProvider", "largeBannerProvider", "interstitialProvider", "rewardedProvider" })
                    SetEnum(so, field, plan.removeOnline ? "CrossPromo" : "Online");
            }
            var property = so.GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null
                    && Removed(plan, AssetDatabase.GetAssetPath(property.objectReferenceValue))) property.objectReferenceValue = null;
            if (so.ApplyModifiedPropertiesWithoutUndo()) { PrefabUtility.RecordPrefabInstancePropertyModifications(component); changed = true; }
        }
        return changed;
    }

    private static void SetEnum(SerializedObject so, string field, string name)
    {
        var property = so.FindProperty(field); if (property == null) return;
        int index = Array.IndexOf(property.enumNames, name); if (index >= 0) property.enumValueIndex = index;
    }
    private static void SetBool(SerializedObject so, string field, bool value)
    { var property = so.FindProperty(field); if (property != null) property.boolValue = value; }
}
