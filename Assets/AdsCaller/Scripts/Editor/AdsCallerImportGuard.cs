using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Keep existing configuration values when matching-release packages are reimported in either order.</summary>
[InitializeOnLoad]
public static class AdsCallerImportGuard
{
    [Serializable] private sealed class Snapshot { public Entry[] entries; }
    [Serializable] private sealed class Entry { public string path, assetBase64, metaBase64; }
    private const string SnapshotPath = "Library/AdsCallerImport/configuration.json";
    private static bool restoreQueued;
    static AdsCallerImportGuard()
    {
        AssetDatabase.importPackageStarted += Capture;
        AssetDatabase.importPackageCompleted += _ => QueueRestore();
        AssetDatabase.importPackageFailed += (_, __) => QueueRestore();
        AssetDatabase.importPackageCancelled += _ => QueueRestore();
        // Static callbacks are lost during the script reload caused by an import.
        if (File.Exists(SnapshotPath)) QueueRestore();
    }
    private static void Capture(string packageName)
    {
        if (File.Exists(SnapshotPath)) Restore();
        AssetDatabase.SaveAssets();
        var paths = AssetDatabase.FindAssets("t:AdsCallerConfig").Concat(AssetDatabase.FindAssets("t:AdsConfiguration"))
            .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && File.Exists(p)).Distinct();
        var entries = paths.Select(path => new Entry { path = path,
            assetBase64 = Convert.ToBase64String(File.ReadAllBytes(path)),
            metaBase64 = File.Exists(path + ".meta") ? Convert.ToBase64String(File.ReadAllBytes(path + ".meta")) : null }).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath));
        File.WriteAllText(SnapshotPath, JsonUtility.ToJson(new Snapshot { entries = entries }), new UTF8Encoding(false));
    }
    private static void QueueRestore()
    {
        if (restoreQueued) return;
        restoreQueued = true; EditorApplication.delayCall += RestoreWhenReady;
    }
    private static void RestoreWhenReady()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += RestoreWhenReady; return; }
        restoreQueued = false; Restore();
    }
    public static void Restore()
    {
        if (!File.Exists(SnapshotPath)) return;
        var snapshot = JsonUtility.FromJson<Snapshot>(File.ReadAllText(SnapshotPath));
        foreach (var entry in snapshot.entries)
        {
            string full = Path.GetFullPath(entry.path);
            string assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(assets, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Configuration snapshot is outside Assets.");
            // A removed asset is not silently resurrected; restore values only for the same GUID.
            if (!File.Exists(entry.path) || !File.Exists(entry.path + ".meta")) continue;
            string savedMeta = entry.metaBase64 == null ? "" : Encoding.UTF8.GetString(Convert.FromBase64String(entry.metaBase64));
            string savedGuid = savedMeta.Split('\n').FirstOrDefault(line => line.StartsWith("guid: "))?.Trim();
            string currentGuid = File.ReadLines(entry.path + ".meta").FirstOrDefault(line => line.StartsWith("guid: "))?.Trim();
            if (savedGuid == null || savedGuid != currentGuid) continue;
            byte[] bytes = Convert.FromBase64String(entry.assetBase64);
            if (File.ReadAllBytes(entry.path).SequenceEqual(bytes)) continue;
            File.WriteAllBytes(entry.path, bytes); AssetDatabase.ImportAsset(entry.path, ImportAssetOptions.ForceUpdate);
            Debug.Log("[AdsCallerImportGuard] Preserved configuration: " + entry.path);
        }
        File.Delete(SnapshotPath);
    }
}
