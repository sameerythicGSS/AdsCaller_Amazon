using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class PackageValidation
{
    private const string ImportPending = "AdsCaller.Validation.ImportPending";
    private static double stableSince;
    static PackageValidation()
    {
        if (SessionState.GetBool(ImportPending, false)) EditorApplication.update += FinishAfterReload;
    }
    private static void FinishAfterReload()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { stableSince = 0; return; }
        if (stableSince == 0) stableSince = EditorApplication.timeSinceStartup;
        if (EditorApplication.timeSinceStartup - stableSince < 3) return;
        SessionState.SetBool(ImportPending, false);
        Debug.Log("PACKAGE_IMPORT_PASS after domain reload"); EditorApplication.Exit(0);
    }
    public static void Export()
    {
        string output = Environment.GetEnvironmentVariable("ADS_PACKAGE_OUTPUT");
        string composition = Environment.GetEnvironmentVariable("ADS_COMPOSITION") ?? "CrossPromo";
        Type exporter = FindType("AdsCallerPackageExporter");
        exporter.GetMethod("ExportForValidation").Invoke(null, new object[] { composition, output, true, composition != "CrossPromo" });
        Debug.Log("PACKAGE_EXPORT_PASS " + output);
    }
    public static void Import()
    {
        string package = Environment.GetEnvironmentVariable("ADS_IMPORT_PACKAGE");
        SessionState.SetBool(ImportPending, true);
        AssetDatabase.importPackageCompleted += name => { Debug.Log("PACKAGE_IMPORT_FINISHED " + name); stableSince = 0; EditorApplication.update -= FinishAfterReload; EditorApplication.update += FinishAfterReload; };
        AssetDatabase.importPackageFailed += (name, error) => { Debug.LogError("PACKAGE_IMPORT_FAIL " + error); EditorApplication.Exit(1); };
        AssetDatabase.importPackageCancelled += name => EditorApplication.Exit(1);
        AssetDatabase.ImportPackage(package, false);
        Debug.Log("PACKAGE_IMPORT_REQUESTED " + package);
    }
    public static void Verify()
    {
        bool wantNetwork = Environment.GetEnvironmentVariable("ADS_EXPECT_NETWORK") == "1";
        bool wantCross = Environment.GetEnvironmentVariable("ADS_EXPECT_CROSS") == "1";
        Require((FindType("AdsCaller") != null) == wantNetwork, "Network presence");
        Require((FindType("AdsCallerSalon") != null) == wantCross, "CrossPromo presence");
        Require(FindType("AdsMediator") != null && FindType("AdsCallerSetup") != null && FindType("AdsCallerPackageExporter") != null, "Shared tools present");
        Require(FindType("SamProjectReporting.Reporter") != null, "Report writer present");
        Require(File.Exists("Assets/Editor/BuildAutomator/BuildController.cs"), "Complete Build Automator present");
        var duplicates = Directory.GetFiles("Assets", "*.meta", SearchOption.AllDirectories)
            .Select(path => new { path, guid = File.ReadLines(path).FirstOrDefault(line => line.StartsWith("guid: ")) })
            .Where(a => a.guid != null).GroupBy(a => a.guid).Where(g => g.Count() > 1).ToArray();
        Require(duplicates.Length == 0, "No duplicate asset GUIDs");
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/AdsCaller" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Require(root != null && root.GetComponentsInChildren<Transform>(true).All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), "Prefab references: " + path);
        }
        if (wantNetwork)
        {
            var config = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/AdsCaller/Data/AdsCallerConfig.asset");
            Require(config != null, "Network config resolves");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AdsCaller/Prefabs/AdsCaller.prefab");
            var caller = prefab != null ? prefab.GetComponent<AdsCaller>() : null;
            var database = typeof(AdsCaller).GetField("Database", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(caller != null && database != null && database.GetValue(caller) != null, "Amazon prefab references its network config");
            Require(caller is IManagedFullscreenProvider, "Amazon provider owns direct-call fullscreen sessions");
            var so = new SerializedObject(config);
            string sentinel = Environment.GetEnvironmentVariable("ADS_SENTINEL");
            if (sentinel == "set")
            {
                so.FindProperty("interstitialCooldown").floatValue = 73;
                so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets();
            }
            else if (sentinel == "check") Require(so.FindProperty("interstitialCooldown").floatValue == 73, "CrossPromo import preserves Android configuration");
        }
        var coreFile = "Assets/AdsCaller/Scripts/Core/IOutAdsProvider.cs";
        Require(File.ReadAllText(coreFile).Contains("Version = 3;"), "Core API 3");
        FindType("SamProjectReporting.Reporter").GetMethod("Publish").Invoke(null, null);
        Require(File.Exists(".samtracker/report.json"), "Local report generated");
        Debug.Log("PACKAGE_VERIFY_PASS");
    }
    private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
    private static void Require(bool value, string reason) { if (!value) throw new Exception("VALIDATION FAILED: " + reason); }
}
