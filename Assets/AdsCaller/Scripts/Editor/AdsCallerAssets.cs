using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>GUID-based source/UPM asset lookup, independent of all runtime assemblies.</summary>
public static class AdsCallerAssets
{
    [Serializable] private sealed class Index { public Entry[] assets; }
    [Serializable] private sealed class Entry { public string path; public string guid; }
    private static Dictionary<string, string> paths;
    public static bool InstallationPending => File.Exists("Library/AdsCallerInstaller/state.json")
        || (Directory.Exists("Assets/AdsCallerInstaller") && Directory.GetFiles("Assets/AdsCallerInstaller", "*.adscaller.bytes").Length != 0);
    public static string Resolve(string legacyPath)
    {
        if (File.Exists(legacyPath)) return legacyPath;
        if (legacyPath.EndsWith(".meta", StringComparison.Ordinal)) return Resolve(legacyPath.Substring(0, legacyPath.Length - 5)) + ".meta";
        if (paths == null)
        {
            paths = new Dictionary<string, string>(StringComparer.Ordinal);
            var indexes = AssetDatabase.FindAssets("AdsCallerAssetIndex t:TextAsset").Select(AssetDatabase.GUIDToAssetPath)
                .Concat(UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                    .Where(p => p.name.StartsWith("com.gss.adscaller.", StringComparison.Ordinal) || p.name.StartsWith("com.adscaller.family.", StringComparison.Ordinal))
                    .Select(p => p.resolvedPath + "/AdsCallerAssetIndex.json")).Where(File.Exists).Distinct();
            foreach (var file in indexes)
            {
                var index = JsonUtility.FromJson<Index>(File.ReadAllText(file));
                if (index?.assets == null) continue;
                foreach (var entry in index.assets)
                {
                    var resolved = AssetDatabase.GUIDToAssetPath(entry.guid);
                    if (!string.IsNullOrEmpty(resolved)) paths[entry.path] = resolved;
                }
            }
        }
        if (paths.TryGetValue(legacyPath, out var path)) return path;
        // Legacy unitypackage imports can replace a source index in either order.
        // Resolve a moved asset by its exact, unique filename within the Ads Caller module roots.
        var matches = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(legacyPath))
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => (p.StartsWith("Assets/AdsCaller/", StringComparison.Ordinal) || p.StartsWith("Packages/com.gss.adscaller.", StringComparison.Ordinal) || p.StartsWith("Packages/com.adscaller.family.", StringComparison.Ordinal))
                && string.Equals(Path.GetFileName(p), Path.GetFileName(legacyPath), StringComparison.Ordinal)).Distinct().ToArray();
        return matches.Length == 1 ? matches[0] : legacyPath;
    }
    public static void Refresh() { paths = null; }
    public static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(Resolve(path));
    public static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
    public static IEnumerable<MonoBehaviour> Components(UnityEngine.SceneManagement.Scene scene) =>
        scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MonoBehaviour>(true)).Where(c => c != null);

    public static void MakeConfigurationWritable(GameObject root)
    {
        var copied = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null) throw new InvalidOperationException("A bootstrap component is unavailable. Finish compilation before reconciliation.");
            var so = new SerializedObject(component);
            var prop = so.GetIterator();
            while (prop.Next(true))
            {
                if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                var original = prop.objectReferenceValue;
                var local = LocalConfiguration(original, copied);
                if (local == original) continue;
                Undo.RecordObject(component, "Ads Caller configuration");
                prop.objectReferenceValue = local;
            }
            if (so.ApplyModifiedProperties()) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
    }
    private static UnityEngine.Object LocalConfiguration(UnityEngine.Object original, Dictionary<UnityEngine.Object, UnityEngine.Object> copied)
    {
        if (!(original is ScriptableObject)) return original;
        string type = original.GetType().Name;
        if (type != "AdsCallerConfig" && type != "AdsConfiguration" && type != "GameCatalog" && type != "GameAdEntry") return original;
        string source = AssetDatabase.GetAssetPath(original);
        if (!source.StartsWith("Packages/com.gss.adscaller.", StringComparison.Ordinal) && !source.StartsWith("Packages/com.adscaller.family.", StringComparison.Ordinal)) return original;
        if (copied.TryGetValue(original, out var known)) return known;
        const string folder = "Assets/AdsCaller/Configuration";
        if (!Directory.Exists(folder)) { Directory.CreateDirectory(folder); AssetDatabase.Refresh(); }
        string path = folder + "/" + type + "-" + AssetDatabase.AssetPathToGUID(source) + ".asset";
        var local = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
        // Reuse configuration created before the branding correction, including catalogs.
        if (local == null)
            local = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/GSSAdsCaller/Configuration/" + Path.GetFileName(path));
        if (local != null) { copied[original] = local; return local; }
        var candidates = AssetDatabase.FindAssets("t:" + type, new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).Where(p => p != path).ToArray();
        if ((type == "AdsCallerConfig" || type == "AdsConfiguration") && candidates.Length == 1)
        {
            local = AssetDatabase.LoadAssetAtPath<ScriptableObject>(candidates[0]);
            copied[original] = local; return local;
        }
        if ((type == "AdsCallerConfig" || type == "AdsConfiguration") && candidates.Length > 1)
            throw new InvalidOperationException("Multiple " + type + " assets exist. Assign the intended one on the scene component and rerun setup.");
        local = UnityEngine.Object.Instantiate((ScriptableObject)original); local.name = original.name;
        AssetDatabase.CreateAsset(local, path); copied[original] = local;
        var so = new SerializedObject(local); var prop = so.GetIterator();
        while (prop.Next(true))
            if (prop.propertyType == SerializedPropertyType.ObjectReference)
                prop.objectReferenceValue = LocalConfiguration(prop.objectReferenceValue, copied);
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(local);
        return local;
    }
}
