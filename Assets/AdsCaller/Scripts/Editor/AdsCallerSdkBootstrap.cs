using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;

/// <summary>Lives outside the provider assemblies so a checkout without SDKs can bootstrap.</summary>
[InitializeOnLoad]
public sealed class AdsCallerSdkBootstrap : AssetPostprocessor
{
    static AdsCallerSdkBootstrap() { EditorApplication.delayCall += Reconcile; }

    public static bool HasSdkAsset(string filename) => AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(filename))
        .Select(AssetDatabase.GUIDToAssetPath).Any(p => Path.GetFileName(p) == filename && Exists(p));

    private static bool Exists(string path)
    {
        if (File.Exists(path)) return true;
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
        return package != null && File.Exists(Path.Combine(package.resolvedPath, path.Substring(package.assetPath.Length + 1)));
    }

    public static void Reconcile()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        bool max = HasSdkAsset("MaxSdkBase.cs") && HasSdkAsset("MaxSdk.Scripts.asmdef");
        bool google = HasSdkAsset("GoogleMobileAds.dll") && HasSdkAsset("GoogleMobileAds.Ump.dll");
        foreach (var target in new[] { NamedBuildTarget.Standalone, NamedBuildTarget.Android, NamedBuildTarget.iOS, NamedBuildTarget.WebGL })
        {
            var original = PlayerSettings.GetScriptingDefineSymbols(target);
            var defines = new List<string>(original.Split(';').Where(s => !string.IsNullOrWhiteSpace(s)));
            Set(defines, "ADSCALLER_MAX", max);
            Set(defines, "ADSCALLER_ADMOB", google);
            // Analytics integrations discover their optional SDKs without saved defines.
            defines.RemoveAll(s => s == "ADSCALLER_FIREBASE" || s == "ADSCALLER_APPSFLYER");
            string updated = string.Join(";", defines);
            if (updated != original) PlayerSettings.SetScriptingDefineSymbols(target, updated);
        }
    }

    private static void Set(List<string> defines, string name, bool present)
    {
        defines.RemoveAll(s => s == name);
        if (present) defines.Add(name);
    }

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] oldPaths)
    {
        // Check files rather than loaded types: removed SDK assemblies may still be in this domain.
        if (imported.Concat(deleted).Concat(moved).Any(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".asmdef") || p.EndsWith("MaxSdkBase.cs")))
            Reconcile();
    }
}
