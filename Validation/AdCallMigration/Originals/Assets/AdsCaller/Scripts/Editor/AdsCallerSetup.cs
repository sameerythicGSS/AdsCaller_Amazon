using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Independent editor setup. UPM resolves mandatory dependencies before compilation.</summary>
[InitializeOnLoad]
public sealed class AdsCallerSetup : EditorWindow
{
    [Serializable] public sealed class Receipt
    {
        public int version = 1;
        public string sceneGuid;
        public bool main, crossPromo, completed;
    }
    private const string ReceiptPath = "ProjectSettings/AdsCallerSetup.json";
    private const string PreviousReceiptPath = "ProjectSettings/GSSAdsCallerSetup.json";
    private const string MainPath = "Assets/AdsCaller/Prefabs/AdsCaller.prefab";
    private static readonly string[] CorePaths = { "Assets/AdsCaller/Prefabs/AdsMediator.prefab", "Assets/AdsCaller/Prefabs/FbAnalytics.prefab", "Assets/MMPHandler/SEHandler.prefab" };
    private static readonly string[] CrossPaths = { "Assets/AdsCaller/SalonAds/Prefabs/AdsCallerSalon.prefab", "Assets/AdsCaller/SalonAds/Prefabs/CatalogLoader.prefab" };
    private SceneAsset target;
    private bool main, cross;
    private string status;
    private Vector2 scroll;
    private static string DismissKey => "AdsCaller_SetupDone_" + Hash128.Compute(Application.dataPath);

    static AdsCallerSetup()
    {
        if (Application.isBatchMode) return;
        EditorApplication.delayCall += () =>
        {
            if (!EditorPrefs.HasKey(DismissKey))
                EditorPrefs.SetBool(DismissKey, EditorPrefs.GetBool("GSSAdsCaller_SetupDone_" + Hash128.Compute(Application.dataPath),
                    EditorPrefs.GetBool("OutAdsCaller_SetupDone_" + Hash128.Compute(Application.dataPath), false)));
            // Retain existing completion state without overwriting either receipt.
            if (!File.Exists(ReceiptPath) && File.Exists(PreviousReceiptPath))
            {
                try { File.Copy(PreviousReceiptPath, ReceiptPath); }
                catch (Exception e) { Debug.LogWarning("[Ads Caller] Could not migrate setup receipt: " + e.Message); }
            }
            if (File.Exists(ReceiptPath))
            {
                try { if (!JsonUtility.FromJson<Receipt>(File.ReadAllText(ReceiptPath)).completed) ShowWindow(); }
                catch (Exception e) { Debug.LogWarning("[Ads Caller] Setup receipt could not be read: " + e.Message); }
            }
            else if (!EditorPrefs.GetBool(DismissKey, false)) ShowWindow();
        };
    }
    [MenuItem("Ads Caller/Setup Wizard")]
    public static void ShowWindow()
    {
        var window = GetWindow<AdsCallerSetup>(false, "Ads Caller Setup", true);
        window.minSize = new Vector2(520, 420); window.Show();
    }
    private void OnEnable()
    {
        AdsCallerAssets.Refresh();
        main = AdsCallerAssets.Load<GameObject>(MainPath) != null;
        cross = CrossPaths.All(p => AdsCallerAssets.Load<GameObject>(p) != null);
        var scene = SceneManager.GetActiveScene();
        if (!string.IsNullOrEmpty(scene.path)) target = AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path);
        if (target == null)
        {
            var first = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled);
            if (first != null) target = AssetDatabase.LoadAssetAtPath<SceneAsset>(first.path);
        }
    }
    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("Ads Caller", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("UPM installs declared dependencies before compilation. Setup wires scene components and prepares writable project configuration while preserving existing settings.", MessageType.Info);
        target = (SceneAsset)EditorGUILayout.ObjectField("Initialization scene", target, typeof(SceneAsset), false);
        using (new EditorGUI.DisabledScope(AdsCallerAssets.Load<GameObject>(MainPath) == null))
            main = EditorGUILayout.ToggleLeft("Online ads (Android or Amazon)", main);
        using (new EditorGUI.DisabledScope(!CrossPaths.All(p => AdsCallerAssets.Load<GameObject>(p) != null)))
            cross = EditorGUILayout.ToggleLeft("Cross promotion", cross);
        var issues = DependencyIssues(cross);
        foreach (var issue in issues) EditorGUILayout.HelpBox(issue, MessageType.Error);
        if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.Info);
        using (new EditorGUI.DisabledScope(target == null || (!main && !cross) || issues.Length != 0 || EditorApplication.isCompiling || EditorApplication.isUpdating))
        {
            if (GUILayout.Button("Set up / reconcile scene", GUILayout.Height(36)))
            {
                try
                {
                    if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    {
                        var path = AssetDatabase.GetAssetPath(target);
                        var scene = SceneManager.GetSceneByPath(path);
                        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                        Apply(scene, main, cross);
                        status = "Setup validated. Configure production IDs and catalog URLs before release.";
                    }
                }
                catch (Exception e) { status = e.GetBaseException().Message; Debug.LogException(e); }
            }
        }
        if (GUILayout.Button("Select scene configuration"))
        {
            var component = AdsCallerAssets.Components(SceneManager.GetActiveScene()).FirstOrDefault(c => c.GetType().Name == (main ? "AdsCaller" : "AdsCallerSalon"));
            if (component != null) Selection.activeGameObject = component.gameObject;
        }
        if (GUILayout.Button("Dismiss")) { EditorPrefs.SetBool(DismissKey, true); Close(); }
        EditorGUILayout.HelpBox("Unrelated compilation errors must be repaired before running this wizard. Pre-wired bootstrap prefabs can be placed when normal scene editing is available. Unity Safe Mode does not run project editor scripts or support prefab editing.", MessageType.None);
        EditorGUILayout.EndScrollView();
    }
    public static string[] DependencyIssues(bool crossPromo)
    {
        if (!crossPromo) return Array.Empty<string>();
        var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
        return new[] { "com.unity.addressables", "com.unity.nuget.newtonsoft-json", "com.unity.inputsystem" }
            .Where(n => !packages.Any(p => p.name == n))
            .Select(n => "Missing " + n + ". Install the matching Ads Caller CrossPromo UPM release. For a legacy unitypackage, install dependencies first.").ToArray();
    }
    public static void Apply(Scene scene, bool includeMain, bool includeCross)
    {
        if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path)) throw new InvalidOperationException("Choose a saved, loaded initialization scene.");
        if (!includeMain && !includeCross) throw new InvalidOperationException("Choose at least one provider.");
        var issues = DependencyIssues(includeCross);
        if (issues.Length > 0) throw new InvalidOperationException(string.Join("\n", issues));
        var paths = SelectedPaths(includeMain, includeCross);
        foreach (var path in paths)
        {
            var prefab = AdsCallerAssets.Load<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("Missing Ads Caller prefab: " + path);
            if (prefab.GetComponentsInChildren<Transform>(true).Any(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0))
                throw new InvalidOperationException("Prefab scripts are unavailable: " + path + ". Resolve compilation errors first.");
            if (Existing(scene, prefab).Length > 1) throw new InvalidOperationException("Multiple " + prefab.name + " components exist. Resolve duplicates before setup.");
        }
        int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Ads Caller setup");
        var receipt = new Receipt { sceneGuid = AssetDatabase.AssetPathToGUID(scene.path), main = includeMain, crossPromo = includeCross };
        File.WriteAllText(ReceiptPath, JsonUtility.ToJson(receipt, true));
        foreach (var path in paths)
        {
            var prefab = AdsCallerAssets.Load<GameObject>(path);
            var instance = Existing(scene, prefab).FirstOrDefault();
            if (instance == null)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Undo.RegisterCreatedObjectUndo(instance, "Ads Caller prefab");
                if (path == CorePaths[0]) ConfigureMediator(instance, includeMain, includeCross);
            }
            AdsCallerAssets.MakeConfigurationWritable(instance);
        }
        if (includeCross)
        {
            var ensure = AdsCallerAssets.FindType("AdsCallerCrossPromoSetup")?.GetMethod("EnsureConfigured", BindingFlags.Static | BindingFlags.Public);
            if (ensure == null) throw new InvalidOperationException("CrossPromo editor integration is unavailable. Finish package resolution and compilation.");
            ensure.Invoke(null, null);
        }
        var sync = AdsCallerAssets.FindType("AdsAndroidEditorIntegration")?.GetMethod("SyncSdkSettings", BindingFlags.Static | BindingFlags.Public);
        if (includeMain && sync != null)
        {
            var caller = AdsCallerAssets.Components(scene).First(c => c.GetType().Name == "AdsCaller");
            var config = new SerializedObject(caller).FindProperty("config")?.objectReferenceValue;
            if (config != null) sync.Invoke(null, new object[] { config });
        }
        EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save initialization scene.");
        if (!Verify(scene, includeMain, includeCross)) throw new InvalidOperationException("Setup verification failed.");
        receipt.completed = true; File.WriteAllText(ReceiptPath, JsonUtility.ToJson(receipt, true));
        Undo.CollapseUndoOperations(undo);
        Debug.Log("[Ads Caller] Setup validated: " + scene.path);
    }
    private static string[] SelectedPaths(bool main, bool cross) => CorePaths.Concat(main ? new[] { MainPath } : Array.Empty<string>()).Concat(cross ? CrossPaths : Array.Empty<string>()).ToArray();
    private static GameObject[] Existing(Scene scene, GameObject prefab)
    {
        var marker = prefab.GetComponents<MonoBehaviour>().FirstOrDefault(c => c != null);
        if (marker == null) throw new InvalidOperationException("Prefab has no usable script: " + prefab.name);
        return AdsCallerAssets.Components(scene).Where(c => c.GetType() == marker.GetType()).Select(c => c.gameObject).Distinct().ToArray();
    }
    public static bool Verify(Scene scene, bool main, bool cross) => SelectedPaths(main, cross)
        .All(p => { var prefab = AdsCallerAssets.Load<GameObject>(p); return prefab != null && Existing(scene, prefab).Length == 1; });
    public static void ConfigureMediator(GameObject instance, bool main, bool cross)
    {
        var component = instance.GetComponent("AdsMediator");
        if (component == null) throw new InvalidOperationException("AdsMediator is unavailable.");
        var so = new SerializedObject(component);
        SetEnum(so, "adsMode", cross ? "CrossPromo" : "Online");
        SetBool(so, "testAds", false); SetBool(so, "bypassRewardedAd", false);
        SetBool(so, "useAdsCallerRewarded", main); SetBool(so, "useAdvancedRouting", main && cross);
        if (main && cross)
        {
            SetEnum(so, "bannerProvider", "CrossPromo"); SetEnum(so, "largeBannerProvider", "CrossPromo");
            SetEnum(so, "interstitialProvider", "Online"); SetEnum(so, "rewardedProvider", "Online");
            SetBool(so, "allowProviderFallback", true);
        }
        so.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.RecordPrefabInstancePropertyModifications(component);
    }
    private static void SetEnum(SerializedObject so, string name, string value)
    {
        var p = so.FindProperty(name); int index = p == null ? -1 : Array.IndexOf(p.enumNames, value);
        if (index < 0) throw new InvalidOperationException("Unsupported mediator field/value: " + name + "/" + value);
        p.enumValueIndex = index;
    }
    private static void SetBool(SerializedObject so, string name, bool value)
    {
        var p = so.FindProperty(name);
        if (p == null) throw new InvalidOperationException("Unsupported mediator field: " + name);
        p.boolValue = value;
    }
}
