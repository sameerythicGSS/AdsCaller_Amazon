using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Editor-only provider discovery and import prompting; never initializes an ad SDK.</summary>
[InitializeOnLoad]
public static class AdsCallerProviderManager
{
    public const string ReceiptPath = "ProjectSettings/AdsCallerProviders.json";
    private const string PendingKey = "AdsCaller.ProviderImportPending";
    private const string ImportKey = "AdsCaller.PackageImportInProgress";
    private static bool queued;
    [Serializable] private sealed class Receipt { public int version = 1; public string signature; public string choice; }

    static AdsCallerProviderManager()
    {
        AssetDatabase.importPackageStarted += name =>
        {
            SessionState.SetBool(ImportKey, true);
            if (IsAdsCallerPackage(name)) SessionState.SetBool(PendingKey, true);
        };
        AssetDatabase.importPackageCompleted += name => { SessionState.SetBool(ImportKey, false); QueuePrompt(); };
        AssetDatabase.importPackageCancelled += name => CancelImport();
        AssetDatabase.importPackageFailed += (name, error) => CancelImport();
        QueuePrompt(); // Also covers first import, UPM installation, and callbacks lost during reload.
    }

    private static bool IsAdsCallerPackage(string name) =>
        (name ?? "").Replace(" ", "").Replace("-", "").Replace("_", "").IndexOf("adscaller", StringComparison.OrdinalIgnoreCase) >= 0;

    public static bool HasOnline => AdsCallerAssets.FindType("AdsCaller") != null;
    public static bool HasCrossPromo => AdsCallerAssets.FindType("AdsCallerSalon") != null;
    public static string Signature => (HasOnline ? "online:" + AdsCallerAssets.FindType("AdsCaller").Assembly.GetName().Name : "-")
        + "|" + (HasCrossPromo ? "crosspromo" : "-");
    public static bool ShouldPrompt
        => !Application.isBatchMode && NeedsChoice;
    public static bool NeedsChoice
    {
        get
        {
            if (!HasOnline || !HasCrossPromo) return false;
            if (SessionState.GetBool(PendingKey, false) || !File.Exists(ReceiptPath)) return true;
            try { return JsonUtility.FromJson<Receipt>(File.ReadAllText(ReceiptPath))?.signature != Signature; }
            catch { return true; }
        }
    }
    private static void CancelImport()
    { SessionState.SetBool(ImportKey, false); SessionState.SetBool(PendingKey, false); }
    internal static void ImportedProviderAssets(string[] paths)
    {
        // Identify custom-named unitypackages by their contents, not only by the archive filename.
        if (!SessionState.GetBool(ImportKey, false)) return;
        foreach (var path in paths)
            if (path.StartsWith("Assets/AdsCaller/Scripts/Providers/", StringComparison.Ordinal)
                || path.StartsWith("Assets/AdsCaller/SalonAds/", StringComparison.Ordinal)
                || (path.StartsWith("Assets/AdsCallerInstaller/", StringComparison.Ordinal) && path.EndsWith(".adscaller.bytes", StringComparison.Ordinal))
                || path == "Assets/AdsCaller/Prefabs/AdsCaller.prefab")
            { SessionState.SetBool(PendingKey, true); break; }
    }

    public static void QueuePrompt()
    {
        if (Application.isBatchMode || queued) return;
        queued = true;
        EditorApplication.delayCall += PromptWhenReady;
    }

    private static void PromptWhenReady()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode
            || SessionState.GetBool(ImportKey, false) || AdsCallerAssets.InstallationPending || File.Exists("Library/AdsCallerImport/configuration.json"))
        { EditorApplication.delayCall += PromptWhenReady; return; }
        queued = false;
        AdsCallerAssets.Refresh();
        if (ShouldPrompt && Resources.FindObjectsOfTypeAll<AdsCallerProviderWindow>().Length == 0)
            AdsCallerProviderWindow.Open();
        else if (HasOnline != HasCrossPromo)
            Remember(HasOnline ? "Online" : "CrossPromo");
    }

    public static void Remember(string choice, string signature = null)
    {
        Directory.CreateDirectory("ProjectSettings");
        File.WriteAllText(ReceiptPath, JsonUtility.ToJson(new Receipt { signature = signature ?? Signature, choice = choice }, true));
        SessionState.SetBool(PendingKey, false);
    }

    // Keeping both changes no existing scenes or policy. New scenes use the Setup Wizard's existing hybrid defaults.
    public static void KeepBoth() => Remember("Hybrid");
}

internal sealed class AdsCallerProviderImportWatcher : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    { AdsCallerProviderManager.ImportedProviderAssets(importedAssets); }
}
