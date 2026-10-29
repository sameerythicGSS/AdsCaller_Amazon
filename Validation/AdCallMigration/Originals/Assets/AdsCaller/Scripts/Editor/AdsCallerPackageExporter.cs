using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Exports deterministic AdsCaller unitypackages from the modules installed in the current project.
/// Open through Ads Caller > Package Exporter.
/// </summary>
public sealed class AdsCallerPackageExporter : EditorWindow
{
    private enum PackageComposition
    {
        Main,
        CrossPromo,
        Combined
    }

    private const string OutputFolderPref = "AdsCaller_PackageExporter_OutputFolder";
    private const string VersionPref = "AdsCaller_PackageExporter_Version";
    private const string IncludeSamplesPref = "AdsCaller_PackageExporter_IncludeSamples";
    private const string IncludeNetworkSdkPref = "AdsCaller_PackageExporter_IncludeNetworkSdk";
    private const string ProjectPreferencesPath = "UserSettings/AdsCallerPackageExporter.json";
    private const string RequiredCoreApiMarker = "public const int Version = 3;";

    [Serializable] private sealed class ProjectPreferences
    {
        public int formatVersion = 1;
        public string outputFolder;
        public string packageVersion;
        public string mainPackageName;
        public string crossPromoPackageName;
        public string combinedPackageName;
        public bool includeSamples = true;
        public bool includeNetworkSdk = true;
    }

    private static readonly string[] CoreAssets =
    {
        "Assets/AdsCaller/Editor/Branding",
        "Assets/AdsCaller/Scripts/AdsCaller.Core.asmdef",
        "Assets/AdsCaller/Scripts/Editor/AdsCaller.Editor.asmdef",
        "Assets/AdsCaller/Scripts/Editor/AdsCallerAssets.cs",
        "Assets/AdsCaller/Scripts/Editor/AdsCallerBootstrap.cs",
        "Assets/MMPHandler/AdsCaller.Core.asmref",
        "Assets/AdsCaller/AdsCallerAssetIndex.json",
        "Assets/AdsCaller/AdsCaller-UPM-Guide.md",

        "Assets/AdsCaller/Editor/ProjectReporter",
        "Assets/AdsCaller/Package-Composition.md",
        "Assets/AdsCaller/CoreRelease.json",
        "Assets/AdsCaller/Prefabs/AdsMediator.prefab",
        "Assets/AdsCaller/Prefabs/FbAnalytics.prefab",
        "Assets/AdsCaller/Scripts/Core/AdsAnalytics.cs",
        "Assets/AdsCaller/Scripts/Core/AdsMediator.cs",
        "Assets/AdsCaller/Scripts/Core/AdsProviderRegistry.cs",
        "Assets/AdsCaller/Scripts/Core/IOutAdsProvider.cs",
        "Assets/AdsCaller/Scripts/Data/EnumsJar_Salon.cs",
        "Assets/AdsCaller/Scripts/Editor/AdsCallerPackageExporter.cs",
        "Assets/AdsCaller/Scripts/Editor/AdsCallerSetup.cs",
        "Assets/AdsCaller/Scripts/Editor/AdsCallerImportGuard.cs",
        "Assets/AdsCaller/Scripts/Firebase/FbAnalytics.cs",
        "Assets/AdsCaller/Scripts/General/Logger.cs",
        "Assets/MMPHandler/SEHandler.cs",
        "Assets/MMPHandler/SEHandler.prefab",
        "Assets/Editor/BuildAutomator",
        "Assets/Editor/OutBuildAutomator.cs"
    };

    private static readonly string[] AmazonNetworkAssets =
    {
        "Assets/AdsCaller/Scripts/Providers/Amazon",
        "Assets/AdsCaller/Bootstrap/Main",
        "Assets/AdsCaller/Data",
        "Assets/AdsCaller/Docs",
        "Assets/AdsCaller/Prefabs/AdsCaller.prefab",
        "Assets/AdsCaller/Scripts/Core/AdsCaller.cs",
        "Assets/AdsCaller/Scripts/Core/AdsCallerConfig.cs",
        "Assets/AdsCaller/Scripts/Editor/AdsCallerConfigEditor.cs",
        "Assets/AdsCaller/Scripts/Editor/AdsCredentialsPopup.cs",
        "Assets/AdsCaller/Scripts/Others/EnumsJar.cs",
        "Assets/AdsCaller/Scripts/Others/FakeLoading.cs",
        "Assets/AdsCaller/Scripts/Others/InAppCallbacks_AMZ.cs",
        "Assets/AdsCaller/Scripts/Others/InAppPurchaser_AMZ.cs"
    };

    private const string AndroidModulePath = "Assets/AdsCaller/AndroidNetworkModule.json";
    [Serializable] private sealed class NetworkModule { public int coreApi; public string[] assets; public string[] sdks; }
    private static NetworkModule AndroidModule => File.Exists(AndroidModulePath) ? JsonUtility.FromJson<NetworkModule>(File.ReadAllText(AndroidModulePath)) : null;
    private static string[] NetworkAssets => AndroidModule?.assets ?? AmazonNetworkAssets;
    private static string[] NetworkSdkAssets => AndroidModule?.sdks ?? AmazonNetworkSdkAssets;

    // Headless validation/export entry point, also used by clean-project import tests.
    public static void ExportForValidation(string composition, string output, bool samples, bool sdks)
    {
        var exporter = CreateInstance<AdsCallerPackageExporter>();
        try
        {
            exporter.includeSamples = samples; exporter.includeNetworkSdk = sdks;
            var kind = (PackageComposition)Enum.Parse(typeof(PackageComposition), composition);
            if (!exporter.ValidateComposition(kind, false)) throw new InvalidOperationException("Package preflight failed: " + composition);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            AssetDatabase.ExportPackage(exporter.BuildAssetList(kind), output, ExportPackageOptions.Recurse);
        }
        finally { DestroyImmediate(exporter); }
    }

    private static readonly string[] NetworkSampleAssets =
    {
        "Assets/AdsCaller/Demo_AdsCaller"
    };

    private static readonly string[] AmazonNetworkSdkAssets =
    {
        "Assets/MaxSdk",
        "Assets/ExternalDependencyManager"
    };

    private static readonly string[] CrossPromoAssets =
    {
        "Assets/AddressableAssetsData",
        "Assets/AdsCaller/Bootstrap/CrossPromo",
        "Assets/AdsCaller/SalonAds/AdsConfiguration.asset",
        "Assets/AdsCaller/SalonAds/Database",
        "Assets/AdsCaller/SalonAds/Images",
        "Assets/AdsCaller/SalonAds/Prefabs",
        "Assets/AdsCaller/SalonAds/Scripts",
        "Assets/AdsCaller/SalonAds/_Docs",
        "Assets/AdsCaller/Scripts/Data/AdsConfiguration.cs",
        "Assets/AdsCaller/Scripts/Others/BannerAnimator.cs"
    };

    private static readonly string[] CrossPromoSampleAssets =
    {
        "Assets/AdsCaller/SalonAds/_Demo"
    };

    private static readonly Dictionary<string, string> ExpectedCoreGuids =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Assets/AdsCaller/Editor/Branding/AdsCaller.png", "f2eae5513a0849cf8b8b135309f04290" },
            { "Assets/AdsCaller/Editor/Branding/AdsCallerCrossPromo.png", "76918759dc314c6d949147a188a67b5e" },
            { "Assets/AdsCaller/Package-Composition.md", "74d84b7d11674855b4976bf4e0daf2ab" },
            { "Assets/AdsCaller/Prefabs/AdsMediator.prefab", "7b34bc23d9aa1524b9c40a1cc3602f05" },
            { "Assets/AdsCaller/Prefabs/FbAnalytics.prefab", "32794b83b3609a448ab284ffc72038ff" },
            { "Assets/AdsCaller/Scripts/Core/AdsAnalytics.cs", "c1dcc169453546d3a9634c89a29d9153" },
            { "Assets/AdsCaller/Scripts/Core/AdsMediator.cs", "255ef5883e78a18429db513313366f23" },
            { "Assets/AdsCaller/Scripts/Core/AdsProviderRegistry.cs", "a84b1b46b6b24aa0acb8ee0e50fbef31" },
            { "Assets/AdsCaller/Scripts/Core/IOutAdsProvider.cs", "e4345a27886277348a626e305198f287" },
            { "Assets/AdsCaller/Scripts/Data/EnumsJar_Salon.cs", "52d90f2acdc37874ca869e4bf4be8a44" },
            { "Assets/AdsCaller/Scripts/Editor/AdsCallerPackageExporter.cs", "bb65706486fd47b2a2632157b1a7b40f" },
            { "Assets/AdsCaller/Scripts/Editor/AdsCallerSetup.cs", "e8e01d7599597fb4eb5b3e93383ec1b7" },
            { "Assets/AdsCaller/Scripts/Firebase/FbAnalytics.cs", "2b6b222b4b96997428ed1c43d3a8429f" },
            { "Assets/AdsCaller/Scripts/General/Logger.cs", "4c1a2a4dedeebb849b5ba31bd83d9245" },
            { "Assets/MMPHandler/SEHandler.cs", "11964dacc72c84998b3c763e9316f739" },
            { "Assets/MMPHandler/SEHandler.prefab", "5d8c176d7aa824105ac1dc50e8b1605e" },
            { "Assets/Editor/BuildAutomator", "9204948a3bcb402bac2de7b09ef03e85" },
            { "Assets/Editor/BuildAutomator/Adb.cs", "60268a34b8c748e59167ad81c02e7fb5" },
            { "Assets/Editor/BuildAutomator/BuildDashboard.cs", "075a507d225b479eab64e7f0c8e58d0c" },
            { "Assets/Editor/BuildAutomator/BuildJobs.cs", "5a741a7b5f054d7280977a46bc766ae7" },
            { "Assets/Editor/BuildAutomator/BuildSettings.cs", "4d4903dc6cdb42bb92cd96fb50997569" },
            { "Assets/Editor/BuildAutomator/OutBuildAutomator.Editor.asmdef", "11c30fc1e0ea4425b690588b2ddfe577" },
            { "Assets/Editor/BuildAutomator/package.json", "255de869e4634b73acd20e6c88a7ceb4" },
            { "Assets/Editor/BuildAutomator/README.md", "5465a94cd9464e9db609cadb281885c9" },
            { "Assets/Editor/BuildAutomator/MachineSettings.cs", "d1fa90226fc64e4f9df72e150bf82ac2" },
            { "Assets/Editor/BuildAutomator/BuildController.cs", "bc31b2c7e79e45ea9eff3839ad6846e5" },
            { "Assets/Editor/BuildAutomator/BuildRecovery.cs", "8617c6e94ac94e9c8f9919c50ddc9149" },
            { "Assets/Editor/BuildAutomator/OperationScheduler.cs", "f67b0639e2564029bc68cbdbb3d4aaf5" },
            { "Assets/Editor/OutBuildAutomator.cs", "687e171c3d851f84e85dd43b502192e0" }
        };

    private string outputFolder;
    private string packageVersion;
    private string mainPackageName;
    private string crossPromoPackageName;
    private string combinedPackageName;
    private bool includeSamples;
    private bool includeNetworkSdk;
    private bool showExportOptions;
    private bool showPreflightDetails;
    private Vector2 scrollPosition;
    private Dictionary<PackageComposition, string[]> exportIssues;
    private string[] coreWarnings = Array.Empty<string>();
    private bool preflightDirty = true;

    private void InvalidatePreflight()
    {
        preflightDirty = true;
        AdsCallerAssets.Refresh();
        Repaint();
    }

    private void OnDisable() { EditorApplication.projectChanged -= InvalidatePreflight; }
    private void OnFocus() { InvalidatePreflight(); }

    private void RefreshPreflight()
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        CollectCoreIssues(errors, warnings);
        var refreshedIssues = new Dictionary<PackageComposition, string[]>();
        foreach (PackageComposition composition in Enum.GetValues(typeof(PackageComposition)))
            refreshedIssues[composition] = CollectCompositionIssues(composition, errors);

        coreWarnings = warnings.ToArray();
        exportIssues = refreshedIssues;
        preflightDirty = false;
    }

    private void EnsurePreflight()
    {
        if (preflightDirty || exportIssues == null) RefreshPreflight();
    }

    // Uses the same preflight as the window, without dialogs or Console side effects.
    public static string[] GetExportIssuesForValidation(string composition, bool samples, bool sdks)
    {
        var exporter = CreateInstance<AdsCallerPackageExporter>();
        try
        {
            exporter.includeSamples = samples; exporter.includeNetworkSdk = sdks;
            exporter.RefreshPreflight();
            return exporter.exportIssues[(PackageComposition)Enum.Parse(typeof(PackageComposition), composition)];
        }
        finally { DestroyImmediate(exporter); }
    }

    [MenuItem("Ads Caller/Package Exporter")]
    public static void ShowWindow()
    {
        AdsCallerPackageExporter window = GetWindow<AdsCallerPackageExporter>("Ads Caller Package Exporter");
        window.minSize = new Vector2(580f, 520f);
        window.Show();
    }

    private void OnEnable()
    {
        EditorApplication.projectChanged -= InvalidatePreflight;
        EditorApplication.projectChanged += InvalidatePreflight;
        exportIssues = null;
        preflightDirty = true;
        LoadProjectPreferences();
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        try
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Ads Caller Package Exporter", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Export legacy .unitypackage distributions", EditorStyles.miniLabel);
            EditorGUILayout.Space(8f);
            DrawProjectSettings();
            EnsurePreflight();
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Packages", EditorStyles.boldLabel);
            DrawPackageCard(PackageComposition.Main, "Main", AndroidModule != null ? "Android provider + Core" : "Amazon provider + Core");
            DrawPackageCard(PackageComposition.CrossPromo, "CrossPromo", "CrossPromo provider + Core");
            DrawPackageCard(PackageComposition.Combined, "Combined", "Main + CrossPromo");
            EditorGUILayout.Space(6f);
            DrawExportOptions();
            DrawPreflightDetails();
            EditorGUILayout.Space(10f);
            DrawExportAllReady();
        }
        finally
        {
            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawProjectSettings()
    {
        EditorGUILayout.LabelField("Project", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Current project", Path.GetFileName(GetProjectRoot()));

        EditorGUI.BeginChangeCheck();
        using (new EditorGUILayout.HorizontalScope())
        {
            string displayed = DisplayOutputFolder(outputFolder);
            string edited = EditorGUILayout.TextField("Export folder", displayed);
            if (edited != displayed) outputFolder = ExpandOutputFolder(edited);
            if (GUILayout.Button("...", GUILayout.Width(32f)))
            {
                string initialFolder = Directory.Exists(outputFolder) ? outputFolder : GetDefaultOutputFolder();
                string selected = EditorUtility.OpenFolderPanel("Select package export folder", initialFolder, string.Empty);
                if (!string.IsNullOrWhiteSpace(selected))
                    outputFolder = selected;
            }
            using (new EditorGUI.DisabledScope(PathsEqual(outputFolder, GetDefaultOutputFolder())))
                if (GUILayout.Button("Reset", GUILayout.Width(52f))) outputFolder = GetDefaultOutputFolder();
        }

        packageVersion = EditorGUILayout.TextField("Version", packageVersion);

        if (EditorGUI.EndChangeCheck())
        {
            SaveProjectPreferences();
            InvalidatePreflight();
        }
    }

    private void DrawPackageCard(PackageComposition composition, string title, string description)
    {
        var issues = exportIssues[composition];
        bool busy = IsBusy();
        bool ready = issues.Length == 0 && !busy;
        string status = busy ? "Checking..." : issues.Length != 0 ? "Unavailable" : coreWarnings.Length != 0 ? "Warning" : "Ready";
        string statusTooltip = BuildStatusTooltip(status, issues, coreWarnings);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                DrawStatus(status, ready, coreWarnings.Length != 0 && issues.Length == 0, statusTooltip);
            }
            EditorGUILayout.LabelField(description, EditorStyles.miniLabel);
            EditorGUILayout.Space(3f);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                SetPackageName(composition, EditorGUILayout.TextField("Package name", GetPackageName(composition)));
                using (new EditorGUI.DisabledScope(GetPackageName(composition) == GetDefaultPackageName(composition)))
                    if (GUILayout.Button("Reset", GUILayout.Width(52f))) SetPackageName(composition, GetDefaultPackageName(composition));
            }
            if (EditorGUI.EndChangeCheck()) SaveProjectPreferences();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Output", GetPackageFileName(composition), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!ready))
                    if (GUILayout.Button("Export", GUILayout.Width(92f), GUILayout.Height(26f))) ExportSingle(composition);
            }
        }
    }

    private static void DrawStatus(string label, bool ready, bool warning, string tooltip)
    {
        Color originalColor = GUI.color;
        GUI.color = warning ? new Color(1f, 0.78f, 0.35f) : ready ? new Color(0.55f, 1f, 0.55f) : new Color(1f, 0.62f, 0.55f);
        GUILayout.Label(new GUIContent(label, tooltip), EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
        GUI.color = originalColor;
    }

    private static string BuildStatusTooltip(string status, string[] issues, string[] warnings)
    {
        if (status == "Checking...")
            return "Export is disabled while Unity is compiling, importing, or entering Play Mode.";
        if (issues != null && issues.Length != 0)
            return BuildStatusDetails("Export unavailable:", issues);
        if (warnings != null && warnings.Length != 0)
            return BuildStatusDetails("Export is available, but local Core changes will be included:", warnings);
        return "All required assets are available.";
    }

    private static string BuildStatusDetails(string heading, string[] details)
    {
        const int maximumDetails = 4;
        string[] summaries = details.Take(maximumDetails)
            .Select(detail => "• " + detail.Replace("\r", " ").Replace("\n", " "))
            .ToArray();
        string remaining = details.Length > maximumDetails ? "\n...and " + (details.Length - maximumDetails) + " more." : string.Empty;
        return heading + "\n" + string.Join("\n", summaries) + remaining;
    }

    private void DrawExportOptions()
    {
        showExportOptions = EditorGUILayout.Foldout(showExportOptions, "Export Options", true);
        if (!showExportOptions) return;
        EditorGUI.indentLevel++;
        EditorGUI.BeginChangeCheck();
        includeSamples = EditorGUILayout.ToggleLeft("Include available demos and sample scripts", includeSamples);
        includeNetworkSdk = EditorGUILayout.ToggleLeft("Bundle network SDKs in Main/Combined", includeNetworkSdk);
        if (EditorGUI.EndChangeCheck())
        {
            SaveProjectPreferences();
            InvalidatePreflight();
        }
        EditorGUI.indentLevel--;
    }

    private void DrawPreflightDetails()
    {
        EnsurePreflight();
        using (new EditorGUILayout.HorizontalScope())
        {
            showPreflightDetails = EditorGUILayout.Foldout(showPreflightDetails, "Preflight Details", true);
            if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(64f)))
            {
                InvalidatePreflight();
                EnsurePreflight();
            }
        }
        if (!showPreflightDetails) return;
        EditorGUI.indentLevel++;
        if (IsBusy()) EditorGUILayout.HelpBox("Wait for compilation/import to finish and leave Play Mode before exporting.", MessageType.Info);
        foreach (PackageComposition composition in Enum.GetValues(typeof(PackageComposition)))
        {
            var issues = exportIssues[composition];
            EditorGUILayout.LabelField(composition + ": " + (issues.Length == 0 ? "Ready" : issues.Length + " blocker" + (issues.Length == 1 ? "" : "s")), EditorStyles.miniBoldLabel);
            foreach (string issue in issues) EditorGUILayout.LabelField("• " + issue, EditorStyles.wordWrappedMiniLabel);
        }
        if (coreWarnings.Length != 0)
        {
            EditorGUILayout.LabelField("Local modifications", EditorStyles.miniBoldLabel);
            foreach (string warning in coreWarnings) EditorGUILayout.LabelField("• " + warning, EditorStyles.wordWrappedMiniLabel);
        }
        if (includeSamples) EditorGUILayout.LabelField("Optional demos are included when available.", EditorStyles.miniLabel);
        EditorGUI.indentLevel--;
    }

    private void DrawExportAllReady()
    {
        var ready = ReadyCompositions();
        using (new EditorGUI.DisabledScope(ready.Length == 0 || IsBusy()))
            if (GUILayout.Button("Export All Ready (" + ready.Length + ")", GUILayout.Height(38f))) ExportAllReady();
    }

    private static bool IsBusy() => EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode;

    private PackageComposition[] ReadyCompositions()
    {
        EnsurePreflight();
        return Enum.GetValues(typeof(PackageComposition)).Cast<PackageComposition>()
            .Where(composition => exportIssues[composition].Length == 0).ToArray();
    }

    private void ExportAllReady()
    {
        AdsCallerAssets.Refresh();
        RefreshPreflight();
        PackageComposition[] compositions = ReadyCompositions();
        if (compositions.Length == 0 || !EnsureOutputFolder()) return;

        string[] duplicateNames = compositions.GroupBy(GetPackageFileName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicateNames.Length != 0)
        {
            EditorUtility.DisplayDialog("Package names must be unique",
                "These ready packages resolve to the same filename:\n\n" + string.Join("\n", duplicateNames), "OK");
            return;
        }

        if (coreWarnings.Length != 0 && !EditorUtility.DisplayDialog("Export modified release sources?",
            "These files differ from CoreRelease.json. Their current contents will be exported. The release baseline will not be rewritten.\n\n" + string.Join("\n", coreWarnings),
            "Export Current Contents", "Cancel")) return;

        List<string> existingOutputs = new List<string>();
        foreach (PackageComposition composition in compositions)
        {
            string candidate = Path.Combine(outputFolder, GetPackageFileName(composition));
            if (File.Exists(candidate)) existingOutputs.Add(candidate);
        }

        if (existingOutputs.Count > 0 &&
            !EditorUtility.DisplayDialog(
                "Replace existing packages?",
                    "These ready package files already exist and will be replaced:\n\n" + string.Join("\n", existingOutputs),
                "Replace All",
                "Cancel"))
        {
            return;
        }

        List<string> outputs = new List<string>();
        try
        {
            for (int i = 0; i < compositions.Length; i++)
            {
                PackageComposition composition = compositions[i];
                EditorUtility.DisplayProgressBar(
                    "Exporting AdsCaller packages",
                    "Exporting " + composition + "...",
                    (float)i / compositions.Length);

                string output = ExportPackage(composition, false);
                if (string.IsNullOrEmpty(output))
                    return;

                outputs.Add(output);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.Refresh();
        EditorUtility.RevealInFinder(outputs[0]);
        EditorUtility.DisplayDialog(
            "Export Complete",
            "Successfully exported all ready packages:\n\n" + string.Join("\n", outputs),
            "OK");
    }

    private void ExportSingle(PackageComposition composition)
    {
        if (!ValidateComposition(composition, true) || !EnsureOutputFolder())
            return;

        string output = ExportPackage(composition, true);
        if (string.IsNullOrEmpty(output))
            return;

        AssetDatabase.Refresh();
        EditorUtility.RevealInFinder(output);
        EditorUtility.DisplayDialog("Export Complete", "Successfully exported:\n\n" + output, "OK");
    }

    private string ExportPackage(PackageComposition composition, bool confirmOverwrite)
    {
        string outputPath = Path.Combine(outputFolder, GetPackageFileName(composition));

        if (File.Exists(outputPath) && confirmOverwrite &&
            !EditorUtility.DisplayDialog(
                "Replace existing package?",
                outputPath + "\n\nalready exists. Replace it?",
                "Replace",
                "Cancel"))
        {
            return null;
        }

        string[] assets = BuildAssetList(composition);
        try
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);

            AssetDatabase.ExportPackage(assets, outputPath, ExportPackageOptions.Recurse);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "Export Failed",
                "Could not export " + composition + ":\n\n" + exception.Message,
                "OK");
            return null;
        }

        if (!File.Exists(outputPath))
        {
            EditorUtility.DisplayDialog(
                "Export Failed",
                "Unity did not create the expected package:\n\n" + outputPath,
                "OK");
            return null;
        }

        Debug.Log("[Ads Caller Exporter] Exported " + composition + " to " + outputPath);
        return outputPath;
    }

    private string[] BuildAssetList(PackageComposition composition)
    {
        HashSet<string> assets = new HashSet<string>(CoreAssets.Select(AdsCallerAssets.Resolve), StringComparer.Ordinal);

        if (composition == PackageComposition.Main || composition == PackageComposition.Combined)
        {
            AddAssets(assets, NetworkAssets);
            if (includeSamples) AddAssets(assets, NetworkSampleAssets.Where(AssetExists));
            if (includeNetworkSdk) AddAssets(assets, NetworkSdkAssets);
        }

        if (composition == PackageComposition.CrossPromo || composition == PackageComposition.Combined)
        {
            AddAssets(assets, CrossPromoAssets);
            if (includeSamples) AddAssets(assets, CrossPromoSampleAssets.Where(AssetExists));
        }

        if (composition == PackageComposition.Combined && AssetDatabase.IsValidFolder("Assets/AdsCaller/Bootstrap/Combined")) assets.Add("Assets/AdsCaller/Bootstrap/Combined");
        List<string> sortedAssets = new List<string>(assets);
        sortedAssets.Sort(StringComparer.Ordinal);
        return sortedAssets.ToArray();
    }

    private string[] CollectCompositionIssues(PackageComposition composition, IEnumerable<string> coreErrors)
    {
        List<string> missing = new List<string>(coreErrors);
        try
        {
            CollectMissing(CoreAssets, missing);

            if (composition == PackageComposition.Main || composition == PackageComposition.Combined)
            {
                CollectMissing(NetworkAssets, missing);
                if (includeNetworkSdk) CollectMissing(NetworkSdkAssets, missing);
            }

            if (composition == PackageComposition.CrossPromo || composition == PackageComposition.Combined)
            {
                CollectMissing(CrossPromoAssets, missing);
            }
        }
        catch (Exception exception) { missing.Add("Cannot read provider manifest: " + exception.Message); }
        return missing.Distinct().ToArray();
    }

    private bool ValidateComposition(PackageComposition composition, bool showDialog)
    {
        // Recheck on click; never trust a cached window snapshot when writing a package.
        AdsCallerAssets.Refresh();
        RefreshPreflight();
        var issues = exportIssues[composition];
        if (issues.Length != 0)
        {
            if (showDialog)
                EditorUtility.DisplayDialog("Package is incomplete", "Cannot export " + composition + ":\n\n" + string.Join("\n", issues), "OK");
            else foreach (string issue in issues) Debug.LogError("[Ads Caller Exporter] " + issue);
            return false;
        }
        if (coreWarnings.Length != 0)
        {
            if (showDialog)
                return EditorUtility.DisplayDialog("Export modified release sources?",
                    "These files differ from CoreRelease.json. Their current contents will be exported. The release baseline will not be rewritten.\n\n" + string.Join("\n", coreWarnings), "Export Current Contents", "Cancel");
            foreach (string warning in coreWarnings) Debug.LogWarning("[Ads Caller Exporter] " + warning);
        }
        return true;
    }

    [Serializable] private sealed class CoreRelease { public CoreFile[] files; }
    [Serializable] private sealed class CoreFile { public string path; public string sha256; }
    private static string Hash(byte[] bytes)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static void CollectCoreIssues(List<string> errors, List<string> warnings)
    {
        try
        {
            foreach (KeyValuePair<string, string> expected in ExpectedCoreGuids)
            {
                if (!AssetExists(expected.Key))
                {
                    errors.Add("Missing: " + expected.Key);
                    continue;
                }

                string actualGuid = AssetDatabase.AssetPathToGUID(AdsCallerAssets.Resolve(expected.Key));
                if (!string.Equals(actualGuid, expected.Value, StringComparison.Ordinal))
                {
                    errors.Add(
                        "GUID mismatch: " + expected.Key +
                        "\n  Expected: " + expected.Value +
                        "\n  Actual:   " + actualGuid);
                }
            }

            string contractsPath = ReadablePath("Assets/AdsCaller/Scripts/Core/IOutAdsProvider.cs");
            if (File.Exists(contractsPath) && !File.ReadAllText(contractsPath).Contains(RequiredCoreApiMarker))
            {
                errors.Add("Core API mismatch: expected AdsCoreApi Version 3. Reimport Core from the same release as both providers.");
            }

            if (AndroidModule != null && AndroidModule.coreApi != 3)
                errors.Add("Android provider and Core API versions do not match.");
            string hashesPath = ReadablePath("Assets/AdsCaller/CoreRelease.json");
            if (File.Exists(hashesPath))
            {
                var release = JsonUtility.FromJson<CoreRelease>(File.ReadAllText(hashesPath));
                if (release?.files == null || release.files.Length == 0)
                    throw new InvalidDataException("CoreRelease.json contains no file entries.");
                foreach (var entry in release.files)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.path) || string.IsNullOrEmpty(entry.sha256))
                        throw new InvalidDataException("CoreRelease.json contains an invalid file entry.");
                    string physicalPath = ReadablePath(entry.path);
                    if (!File.Exists(physicalPath)) errors.Add("Missing Core file: " + entry.path);
                    else if (Hash(File.ReadAllBytes(physicalPath)) != entry.sha256)
                        warnings.Add("Modified Core file: " + entry.path);
                }
            }
            else errors.Add("Missing CoreRelease.json");
        }
        catch (Exception exception) { errors.Add("Cannot validate Core: " + exception.Message); }
    }

    private static string ReadablePath(string path)
    {
        path = AdsCallerAssets.Resolve(path);
        if (!path.StartsWith("Packages/", StringComparison.Ordinal)) return path;
        var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
            .FirstOrDefault(p => path.StartsWith(p.assetPath + "/", StringComparison.Ordinal));
        return package == null ? path : package.resolvedPath + path.Substring(package.assetPath.Length);
    }

    private bool EnsureOutputFolder()
    {
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            EditorUtility.DisplayDialog("Output folder required", "Select an export folder first.", "OK");
            return false;
        }

        try
        {
            outputFolder = Path.GetFullPath(outputFolder);
            Directory.CreateDirectory(outputFolder);
            SaveProjectPreferences();
            return true;
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog(
                "Invalid output folder",
                "Could not use the selected output folder:\n\n" + exception.Message,
                "OK");
            return false;
        }
    }

    private string GetPackageFileName(PackageComposition composition)
    {
        string safeVersion = SanitizeFilePart(packageVersion);
        string configuredName = GetPackageName(composition);
        if (string.IsNullOrWhiteSpace(configuredName)) configuredName = GetDefaultPackageName(composition);
        string packageName = SanitizeFilePart(configuredName);
        if (packageName.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
            packageName = packageName.Substring(0, packageName.Length - ".unitypackage".Length).TrimEnd();
        if (string.IsNullOrEmpty(packageName)) packageName = GetDefaultPackageName(composition);
        return packageName + "-v" + safeVersion + ".unitypackage";
    }

    private static string SanitizeFilePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "1.0.0";

        char[] invalid = Path.GetInvalidFileNameChars();
        foreach (char invalidCharacter in invalid)
            value = value.Replace(invalidCharacter, '-');

        value = value.Trim();
        return string.IsNullOrEmpty(value) ? "1.0.0" : value;
    }

    private static bool AreAssetsAvailable(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (!AssetExists(path))
                return false;
        }

        return true;
    }

    private static void CollectMissing(IEnumerable<string> paths, ICollection<string> missing)
    {
        foreach (string path in paths)
        {
            if (!AssetExists(path))
                missing.Add(path);
        }
    }

    private static bool AssetExists(string path)
    {
        path = AdsCallerAssets.Resolve(path);
        return AssetDatabase.IsValidFolder(path) ||
               !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path));
    }

    private static void AddAssets(ISet<string> destination, IEnumerable<string> paths)
    {
        foreach (string path in paths)
            destination.Add(AdsCallerAssets.Resolve(path));
    }

    private string GetPackageName(PackageComposition composition)
    {
        switch (composition)
        {
            case PackageComposition.Main: return mainPackageName;
            case PackageComposition.CrossPromo: return crossPromoPackageName;
            default: return combinedPackageName;
        }
    }

    private void SetPackageName(PackageComposition composition, string value)
    {
        switch (composition)
        {
            case PackageComposition.Main: mainPackageName = value; break;
            case PackageComposition.CrossPromo: crossPromoPackageName = value; break;
            default: combinedPackageName = value; break;
        }
    }

    private static string GetDefaultPackageName(PackageComposition composition)
    {
        switch (composition)
        {
            case PackageComposition.Main: return AndroidModule != null ? "AdsCaller-Android" : "AdsCaller-Main";
            case PackageComposition.CrossPromo: return "AdsCaller-CrossPromo";
            default: return AndroidModule != null ? "AdsCaller-Android-CrossPromo" : "AdsCaller-Combined";
        }
    }

    private void LoadProjectPreferences()
    {
        ProjectPreferences preferences = null;
        string path = GetProjectPreferencesPath();
        if (File.Exists(path))
        {
            try { preferences = JsonUtility.FromJson<ProjectPreferences>(File.ReadAllText(path)); }
            catch (Exception exception) { Debug.LogWarning("[Ads Caller Exporter] Could not read project settings: " + exception.Message); }
        }
        if (preferences == null)
        {
            preferences = new ProjectPreferences
            {
                outputFolder = EditorPrefs.GetString(OutputFolderPref, EditorPrefs.GetString("GSSAdsCaller_PackageExporter_OutputFolder", EditorPrefs.GetString("OutAdsCaller_PackageExporter_OutputFolder", GetDefaultOutputFolder()))),
                packageVersion = EditorPrefs.GetString(VersionPref, EditorPrefs.GetString("GSSAdsCaller_PackageExporter_Version", EditorPrefs.GetString("OutAdsCaller_PackageExporter_Version", "3.1.1"))),
                includeSamples = EditorPrefs.GetBool(IncludeSamplesPref, EditorPrefs.GetBool("GSSAdsCaller_PackageExporter_IncludeSamples", EditorPrefs.GetBool("OutAdsCaller_PackageExporter_IncludeSamples", true))),
                includeNetworkSdk = EditorPrefs.GetBool(IncludeNetworkSdkPref, EditorPrefs.GetBool("GSSAdsCaller_PackageExporter_IncludeNetworkSdk", EditorPrefs.GetBool("OutAdsCaller_PackageExporter_IncludeNetworkSdk", true)))
            };
        }
        outputFolder = ExpandOutputFolder(string.IsNullOrWhiteSpace(preferences.outputFolder) ? GetDefaultOutputFolder() : preferences.outputFolder);
        packageVersion = string.IsNullOrWhiteSpace(preferences.packageVersion) ? "3.1.1" : preferences.packageVersion;
        mainPackageName = string.IsNullOrWhiteSpace(preferences.mainPackageName) ? GetDefaultPackageName(PackageComposition.Main) : preferences.mainPackageName;
        crossPromoPackageName = string.IsNullOrWhiteSpace(preferences.crossPromoPackageName) ? GetDefaultPackageName(PackageComposition.CrossPromo) : preferences.crossPromoPackageName;
        combinedPackageName = string.IsNullOrWhiteSpace(preferences.combinedPackageName) ? GetDefaultPackageName(PackageComposition.Combined) : preferences.combinedPackageName;
        includeSamples = preferences.includeSamples;
        includeNetworkSdk = preferences.includeNetworkSdk;
        if (!File.Exists(path)) SaveProjectPreferences();
    }

    private void SaveProjectPreferences()
    {
        try
        {
            var preferences = new ProjectPreferences
            {
                outputFolder = StoreOutputFolder(outputFolder), packageVersion = packageVersion,
                mainPackageName = mainPackageName, crossPromoPackageName = crossPromoPackageName,
                combinedPackageName = combinedPackageName, includeSamples = includeSamples,
                includeNetworkSdk = includeNetworkSdk
            };
            string path = GetProjectPreferencesPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(preferences, true) + Environment.NewLine);
        }
        catch (Exception exception) { Debug.LogWarning("[Ads Caller Exporter] Could not save project settings: " + exception.Message); }
    }

    private static string GetProjectRoot() => Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
    private static string GetProjectPreferencesPath() => Path.Combine(GetProjectRoot(), ProjectPreferencesPath.Replace('/', Path.DirectorySeparatorChar));

    private static string DisplayOutputFolder(string value)
    {
        string relative;
        return TryProjectRelative(value, out relative) ? (string.IsNullOrEmpty(relative) ? "<Project>" : "<Project>/" + relative.Replace('\\', '/')) : value;
    }

    private static string ExpandOutputFolder(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return GetDefaultOutputFolder();
        value = value.Trim();
        try
        {
            if (value.Equals("<Project>", StringComparison.OrdinalIgnoreCase)) return GetProjectRoot();
            const string prefix = "<Project>/";
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(Path.Combine(GetProjectRoot(), value.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar)));
            return Path.IsPathRooted(value) ? Path.GetFullPath(value) : Path.GetFullPath(Path.Combine(GetProjectRoot(), value));
        }
        catch { return value; }
    }

    private static string StoreOutputFolder(string value)
    {
        string relative;
        return TryProjectRelative(value, out relative) ? relative.Replace('\\', '/') : value;
    }

    private static bool TryProjectRelative(string value, out string relative)
    {
        relative = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string root = Path.GetFullPath(GetProjectRoot()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full;
        try { full = Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return false; }
        if (string.Equals(root, full, StringComparison.OrdinalIgnoreCase)) { relative = string.Empty; return true; }
        string prefix = root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        relative = full.Substring(prefix.Length); return true;
    }

    private static bool PathsEqual(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left).TrimEnd('\\', '/'), Path.GetFullPath(right).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static string GetDefaultOutputFolder()
    {
        return Path.Combine(GetProjectRoot(), "PackageExports");
    }
}
