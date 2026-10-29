using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AdsCallerPackageValidation
{
    private static bool Main => AdsCallerAssets.Load<GameObject>("Assets/AdsCaller/Prefabs/AdsCaller.prefab") != null;
    private static bool Cross => AdsCallerAssets.Load<GameObject>("Assets/AdsCaller/SalonAds/Prefabs/AdsCallerSalon.prefab") != null;
    public static void ExportLegacy()
    {
        string output=Environment.GetEnvironmentVariable("ADSCALLER_EXPORT_OUTPUT");
        if(string.IsNullOrEmpty(output)) throw new InvalidOperationException("Set ADSCALLER_EXPORT_OUTPUT.");
        AdsCallerPackageExporter.ExportForValidation(Main && Cross ? "Combined" : Main ? "Main" : "CrossPromo",output,false,true);
        Debug.Log("ADSCALLER_LEGACY_EXPORT_PASS");
    }
    public static void GenerateBootstraps()
    {
        if (Main) Generate("Main",true,false);
        if (Cross) Generate("CrossPromo",false,true);
        if (Main && Cross) Generate("Combined",true,true);
        Debug.Log("ADSCALLER_BOOTSTRAPS_PASS");
    }
    private static void Generate(string name, bool main, bool cross)
    {
        string folder="Assets/AdsCaller/Bootstrap/"+name;
        Directory.CreateDirectory(folder); AssetDatabase.Refresh();
        var root=new GameObject("Ads Caller - "+name);
        try
        {
            string[] paths={"Assets/AdsCaller/Prefabs/AdsMediator.prefab","Assets/AdsCaller/Prefabs/FbAnalytics.prefab","Assets/MMPHandler/SEHandler.prefab"};
            if(main) paths=paths.Concat(new[]{"Assets/AdsCaller/Prefabs/AdsCaller.prefab"}).ToArray();
            if(cross) paths=paths.Concat(new[]{"Assets/AdsCaller/SalonAds/Prefabs/AdsCallerSalon.prefab","Assets/AdsCaller/SalonAds/Prefabs/CatalogLoader.prefab"}).ToArray();
            foreach(var path in paths)
            {
                var prefab=AdsCallerAssets.Load<GameObject>(path);
                Check(prefab!=null,"Missing source prefab: "+path);
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root.transform);
                if(path.EndsWith("/AdsMediator.prefab")) AdsCallerSetup.ConfigureMediator(instance,main,cross);
            }
            Check(NoMissing(root),"Missing scripts in "+name+" bootstrap");
            Check(PrefabUtility.SaveAsPrefabAsset(root,folder+"/Ads Caller "+name+".prefab")!=null,"Save bootstrap");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    public static void Verify()
    {
        var assemblies=CompilationPipeline.GetAssemblies();
        var bootstrap=assemblies.Single(a=>a.name=="AdsCaller.Editor");
        Check(!bootstrap.assemblyReferences.Any(a=>a.name.StartsWith("AdsCaller.")||a.name.StartsWith("Assembly-CSharp")),"Bootstrap must be independent");
        var core=assemblies.Single(a=>a.name=="AdsCaller.Core");
        Check(!core.assemblyReferences.Any(a=>a.name.Contains("MaxSdk")||a.name.Contains("AppsFlyer")||a.name.StartsWith("Assembly-CSharp")),"Core isolation");
        Check(assemblies.Where(a=>a.name.StartsWith("AdsCaller")).All(a=>!a.assemblyReferences.Any(d=>d.name.StartsWith("Assembly-CSharp"))),"No Ads Caller assembly references game scripts");
        string online=Environment.GetEnvironmentVariable("ADSCALLER_EXPECT_MAIN");
        string cross=Environment.GetEnvironmentVariable("ADSCALLER_EXPECT_CROSS");
        if(online!=null) Check(Main==(online=="1"),"Online availability");
        if(cross!=null) Check(Cross==(cross=="1"),"CrossPromo availability");
        Directory.CreateDirectory("Assets/AdsCallerPackageValidation");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        Check(EditorSceneManager.SaveScene(scene,"Assets/AdsCallerPackageValidation/Initialization.unity"),"Save validation scene");
        AdsCallerSetup.Apply(scene,Main,Cross);
        var mediator=AdsCallerAssets.Components(scene).Single(c=>c.GetType().Name=="AdsMediator");
        var serialized=new SerializedObject(mediator);
        serialized.FindProperty("removeAdsPlayerPrefKey").stringValue="GSS.Validation.Preserved";
        serialized.ApplyModifiedPropertiesWithoutUndo();
        int count=scene.GetRootGameObjects().Length;
        var configIds=ConfigurationPaths(scene);
        AdsCallerSetup.Apply(scene,Main,Cross);
        Check(count==scene.GetRootGameObjects().Length,"Setup duplicates objects");
        Check(new SerializedObject(mediator).FindProperty("removeAdsPlayerPrefKey").stringValue=="GSS.Validation.Preserved","Mediator overrides preserved");
        Check(configIds.SequenceEqual(ConfigurationPaths(scene)),"Configuration identity preserved");
        Check(scene.GetRootGameObjects().All(NoMissing),"No missing scene scripts");
        var duplicate=UnityEngine.Object.Instantiate(mediator.gameObject);
        bool rejected=false;
        try { AdsCallerSetup.Apply(scene,Main,Cross); } catch(InvalidOperationException) { rejected=true; }
        UnityEngine.Object.DestroyImmediate(duplicate);
        Check(rejected,"Duplicate mediator rejected");
        Check(AdsCallerSetup.Verify(scene,Main,Cross),"Structural verification");
        if (Environment.GetEnvironmentVariable("ADSCALLER_UPM")=="1")
            Check(configIds.All(p=>p.StartsWith("Assets/")),"UPM config must be writable");
        EditorSceneManager.SaveScene(scene);
        Debug.Log("ADSCALLER_VALIDATION_PASS");
    }
    public static void VerifyExisting()
    {
        var scene=EditorSceneManager.OpenScene("Assets/AdsCallerPackageValidation/Initialization.unity",OpenSceneMode.Single);
        Check(AdsCallerSetup.Verify(scene,Main,Cross),"Existing scene references survived upgrade");
        var mediator=AdsCallerAssets.Components(scene).Single(c=>c.GetType().Name=="AdsMediator");
        Check(new SerializedObject(mediator).FindProperty("removeAdsPlayerPrefKey").stringValue=="GSS.Validation.Preserved","Existing mediator override survived upgrade");
        Check(scene.GetRootGameObjects().All(NoMissing),"Existing scene scripts survived upgrade");
        AdsCallerSetup.Apply(scene,Main,Cross);
        Debug.Log("ADSCALLER_EXISTING_PASS");
    }
    private static string[] ConfigurationPaths(Scene scene) => AdsCallerAssets.Components(scene)
        .SelectMany(c=>{
            var refs=new System.Collections.Generic.List<string>();
            var p=new SerializedObject(c).GetIterator();
            while(p.Next(true)) if(p.propertyType==SerializedPropertyType.ObjectReference && p.objectReferenceValue is ScriptableObject) refs.Add(AssetDatabase.GetAssetPath(p.objectReferenceValue));
            return refs;
        }).OrderBy(p=>p).ToArray();
    private static bool NoMissing(GameObject root) => root.GetComponentsInChildren<Transform>(true).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0);
    private static void Check(bool ok,string message) { if(!ok) throw new Exception("ADSCALLER_VALIDATION_FAILED: "+message); }
}
