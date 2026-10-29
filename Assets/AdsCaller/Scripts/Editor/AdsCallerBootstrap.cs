using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Creates a composition asset without requiring a runtime dependency in the editor assembly.</summary>
public static class AdsCallerBootstrap
{
    [MenuItem("Ads Caller/Create Bootstrap Prefab")]
    public static void CreateForInstalledModules()
    {
        bool main=AdsCallerAssets.Load<GameObject>("Assets/AdsCaller/Prefabs/AdsCaller.prefab")!=null;
        bool cross=AdsCallerAssets.Load<GameObject>("Assets/AdsCaller/SalonAds/Prefabs/AdsCallerSalon.prefab")!=null;
        if(!main && !cross) throw new InvalidOperationException("Install an Online or CrossPromo provider first.");
        string name=main && cross ? "Combined" : main ? "Main" : "CrossPromo";
        const string folder="Assets/AdsCaller/Bootstrap";
        Directory.CreateDirectory(folder); AssetDatabase.Refresh();
        string path=AssetDatabase.GenerateUniqueAssetPath(folder+"/Ads Caller "+name+".prefab");
        var root=new GameObject("Ads Caller - "+name);
        try
        {
            Add(root,"Assets/AdsCaller/Prefabs/AdsMediator.prefab",main,cross);
            Add(root,"Assets/AdsCaller/Prefabs/FbAnalytics.prefab",main,cross);
            Add(root,"Assets/MMPHandler/SEHandler.prefab",main,cross);
            if(main) Add(root,"Assets/AdsCaller/Prefabs/AdsCaller.prefab",main,cross);
            if(cross)
            {
                Add(root,"Assets/AdsCaller/SalonAds/Prefabs/AdsCallerSalon.prefab",main,cross);
                Add(root,"Assets/AdsCaller/SalonAds/Prefabs/CatalogLoader.prefab",main,cross);
            }
            AdsCallerAssets.MakeConfigurationWritable(root);
            var asset=PrefabUtility.SaveAsPrefabAsset(root,path);
            if(asset==null) throw new IOException("Failed to create bootstrap prefab.");
            Selection.activeObject=asset; EditorGUIUtility.PingObject(asset);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void Add(GameObject root,string path,bool main,bool cross)
    {
        var prefab=AdsCallerAssets.Load<GameObject>(path);
        if(prefab==null) throw new InvalidOperationException("Missing prefab: "+path);
        var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root.transform);
        if(path.EndsWith("/AdsMediator.prefab",StringComparison.Ordinal)) AdsCallerSetup.ConfigureMediator(instance,main,cross);
    }
}
