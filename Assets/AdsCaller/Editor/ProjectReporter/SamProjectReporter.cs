using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SamProjectReporting
{
    [Serializable] public sealed class ProjectReport
    {
        public int schemaVersion = 2;
        public string networkVariant;
        public string[] installedProviders, adsConfiguration;
        public string projectPath, productName, packageName, version, unityVersion, writtenUtc;
        public int versionCode;
        public string rewardedBypassed = "", popAdAdded = "";
        public string[] inspectedScenes = new string[0];
        public string buildResult = "", buildVersion = "", buildUtc = "", buildTarget = "", buildOutput = "";
        public int buildVersionCode;
    }

    // Local file only. No network, account, setup window, or runtime dependencies.
    [InitializeOnLoad]
    public static class Reporter
    {
        public const string RelativeReport = ".samtracker/report.json";
        private static double next;
        private static string lastContent;
        private static bool warned;
        static Reporter()
        {
            EditorApplication.update += Update;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorApplication.delayCall += Publish;
        }
        private static void OnSceneSaved(Scene scene) { lastContent = null; next = 0; }
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        private static void Update()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 30;
            Publish();
        }
        private static ProjectReport Collect()
        {
            string path = Path.Combine(Root, RelativeReport);
            ProjectReport report = null;
            try { if (File.Exists(path)) report = JsonUtility.FromJson<ProjectReport>(File.ReadAllText(path)); } catch { }
            if (report == null || !string.Equals(report.projectPath, Root, StringComparison.OrdinalIgnoreCase)) report = new ProjectReport();
            report.schemaVersion = 2;
            report.networkVariant = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "AdsCaller.Android")
                || File.Exists("Assets/AdsCaller/AndroidNetworkModule.json") ? "Android" : "Amazon/CrossPromo";
            report.installedProviders = new[] { "AdsCaller", "AdsCallerSalon" }
                .Where(name => AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetType(name) != null)).ToArray();
            // Configuration status only. Never serialize credentials, SDK keys or device identifiers.
            report.adsConfiguration = AssetDatabase.FindAssets("t:AdsCallerConfig").Select(guid =>
            {
                string configPath = AssetDatabase.GUIDToAssetPath(guid);
                var config = new SerializedObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(configPath));
                string[] flags = { "isAdsBypass", "useGoogleTestAds", "requestConsentOnInitialize", "canShowBanner", "canShowLargeBanner", "canShowInterstitial", "isUsingRewardedAd", "isUsingAppOpenAd" };
                return configPath + ": " + string.Join(", ", flags.Select(name => config.FindProperty(name))
                    .Where(p => p != null).Select(p => p.name + "=" + p.boolValue));
            }).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            report.projectPath = Root;
            report.productName = PlayerSettings.productName;
            report.packageName = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            report.version = PlayerSettings.bundleVersion;
            report.versionCode = PlayerSettings.Android.bundleVersionCode;
            report.unityVersion = Application.unityVersion;
            var scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
                .Where(s => s.isLoaded && !s.isDirty && !string.IsNullOrEmpty(s.path) &&
                    s.path.IndexOf("Demo", StringComparison.OrdinalIgnoreCase) < 0).ToArray();
            report.inspectedScenes = scenes.Select(s => s.path).OrderBy(s => s, StringComparer.Ordinal).ToArray();
            var components = scenes.SelectMany(s => s.GetRootGameObjects()).SelectMany(g => g.GetComponentsInChildren<MonoBehaviour>(false))
                .Where(c => c != null && c.enabled && c.gameObject.activeInHierarchy).ToArray();
            var values = components.Where(c => c.GetType().Name == "AdsMediator")
                .Select(c => new SerializedObject(c).FindProperty("bypassRewardedAd")).Where(p => p != null).Select(p => p.boolValue).Distinct().ToArray();
            var buildScenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            bool allCovered = buildScenes.Length > 0 && buildScenes.All(s => report.inspectedScenes.Contains(s));
            report.rewardedBypassed = values.Length == 1 && (values[0] || allCovered) ? (values[0] ? "true" : "false") : "";
            report.popAdAdded = components.Any(c => c.GetType().Name == "SalonPopAd") ? "true" : "";
            return report;
        }
        [MenuItem("Ads Caller/Project Reporter/Write Local Report")]
        public static void Publish() { Save(null); }
        public static void RecordBuild(BuildReport build)
        {
            if (build == null) return;
            Save(build);
        }
        private static void Save(BuildReport build)
        {
            try
            {
                var report = Collect();
                if (build != null)
                {
                    report.buildResult = build.summary.result.ToString();
                    report.buildVersion = PlayerSettings.bundleVersion;
                    report.buildVersionCode = PlayerSettings.Android.bundleVersionCode;
                    report.buildUtc = DateTime.UtcNow.ToString("O");
                    report.buildTarget = build.summary.platform.ToString();
                    report.buildOutput = build.summary.outputPath;
                }
                // Reuse a stable payload to avoid writes every frame or editor cycle.
                report.writtenUtc = "";
                string content = JsonUtility.ToJson(report, true);
                if (build == null && content == lastContent) return;
                report.writtenUtc = DateTime.UtcNow.ToString("O");
                string dir = Path.Combine(Root, ".samtracker");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(Root, RelativeReport);
                File.WriteAllText(path + ".tmp", JsonUtility.ToJson(report, true), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(path + ".tmp", path, path + ".bak"); else File.Move(path + ".tmp", path);
                if (!File.Exists(Path.Combine(dir, ".gitignore"))) File.WriteAllText(Path.Combine(dir, ".gitignore"), "*\n");
                lastContent = content;
                warned = false;
                // Remove only the retired tracker secret for this exact project.
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    EditorPrefs.DeleteKey("AdsCallerWorkTracker.Token." + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Root))).Replace("-", ""));
            }
            catch (Exception e)
            {
                if (!warned) Debug.LogWarning("[SamProjectReporter] Local report could not be saved: " + e.GetType().Name);
                warned = true;
            }
        }
    }
    public sealed class BuildReporter : IPostprocessBuildWithReport
    {
        public int callbackOrder { get { return int.MaxValue; } }
        public void OnPostprocessBuild(BuildReport report) { Reporter.RecordBuild(report); }
    }
}
