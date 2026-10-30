using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OutBuild
{
    public static class BuildJobs
    {
        public static bool Busy { get; private set; }
        public static event Action<BuildReport> BuildCompleted;
        public static event Action<BuildRecord> ArtifactPublished;

        public static BuildTarget Target { get { return EditorUserBuildSettings.activeBuildTarget; } }
        public static BuildTargetGroup Group { get { return BuildPipeline.GetBuildTargetGroup(Target); } }
        public static string Package { get { return PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.FromBuildTargetGroup(Group)); } }

        public static string[] Scenes()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
#if UNITY_6000_0_OR_NEWER
            var profile = UnityEditor.Build.Profile.BuildProfile.GetActiveBuildProfile();
            if (profile != null && profile.overrideGlobalScenes) scenes = profile.scenes;
#endif
            return scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        }

        public static List<string> Validate(BuildPreset preset, string version, int code, bool run, bool hasReadyDevices, string adb, string outputOverride = null)
        {
            var errors = new List<string>();
            if (Busy || BuildPipeline.isBuildingPlayer) errors.Add("Another build is in progress.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) errors.Add("Wait for Unity to finish compiling/importing.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) errors.Add("Exit Play Mode before building.");
            if (!BuildPipeline.IsBuildTargetSupported(Group, Target)) errors.Add("Install the " + Target + " build module using Unity Hub.");
            string[] scenes = Scenes();
            if (scenes.Length == 0) errors.Add("Enable at least one scene in Build Settings / the active Build Profile.");
            if (scenes.Any(s => !File.Exists(s))) errors.Add("A selected build scene is missing. Review the scene list.");
            if (string.IsNullOrWhiteSpace(version) || !Regex.IsMatch(version, @"^\d+(\.\d+){0,3}$")) errors.Add("Use a numeric version, for example 1.2.3.");
            if (Target == BuildTarget.Android && (code < 1 || code > 2100000000)) errors.Add("Android version code must be between 1 and 2100000000.");
            if (Target == BuildTarget.Android && code < PlayerSettings.Android.bundleVersionCode) errors.Add("Version code cannot decrease below the current project value.");
            if (run && Target != BuildTarget.WebGL && Target != BuildTarget.Android && Target != BuildTarget.StandaloneWindows && Target != BuildTarget.StandaloneWindows64 && Target != BuildTarget.StandaloneOSX && Target != BuildTarget.StandaloneLinux64)
                errors.Add("Build is available for this target. Automatic launch is supported for Android and desktop players.");
            if (preset == null) { errors.Add("Select a build preset."); return errors; }
            if (Target == BuildTarget.WebGL && preset.web != null && preset.web.overrideSettings)
            {
                if (!Enum.IsDefined(typeof(WebGLCompressionFormat), preset.web.compression)) errors.Add("Select a valid WebGL compression format.");
                if (!Enum.IsDefined(typeof(WebGLExceptionSupport), preset.web.exceptions)) errors.Add("Select valid WebGL exception support.");
            }
            try { BuildStorage.BuildOutputRoot(preset, outputOverride); }
            catch (Exception e) { errors.Add("Invalid output folder: " + e.Message); }
            if (string.IsNullOrWhiteSpace(preset.namingTemplate)) errors.Add("Enter an output naming template.");
            if (Target == BuildTarget.Android)
            {
                if (!Regex.IsMatch(Package, @"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$")) errors.Add("Set a valid Android application identifier in Player Settings.");
                if (EditorUserBuildSettings.exportAsGoogleAndroidProject) errors.Add("Disable Export Project in Android Build Settings to generate an APK/AAB.");
                if (!preset.appBundle && PlayerSettings.Android.buildApkPerCpuArchitecture) errors.Add("Disable Split APKs by target architecture; deployment requires one complete APK.");
#if UNITY_6000_0_OR_NEWER
                bool splitBinary = PlayerSettings.Android.splitApplicationBinary;
#else
                bool splitBinary = PlayerSettings.Android.useAPKExpansionFiles;
#endif
                if (!preset.appBundle && splitBinary) errors.Add("Disable Split Application Binary for a standalone APK. OBB deployment is not supported by this tool.");
                if (EditorUserBuildSettings.buildScriptsOnly) errors.Add("Disable Scripts Only Build; this tool creates complete standalone artifacts.");
                if (EditorUserBuildSettings.androidBuildSystem != AndroidBuildSystem.Gradle) errors.Add("Use the Gradle Android build system.");
                if (PlayerSettings.Android.useCustomKeystore)
                {
                    var keystore = BuildStorage.InspectKeystore();
                    if (!string.IsNullOrEmpty(keystore.Error)) errors.Add(keystore.Error);
                    if (string.IsNullOrWhiteSpace(PlayerSettings.Android.keyaliasName)) errors.Add("Select a signing key alias in Player Settings.");
                    if (string.IsNullOrEmpty(PlayerSettings.Android.keystorePass) || string.IsNullOrEmpty(PlayerSettings.Android.keyaliasPass)) errors.Add("Enter this session's signing passwords under Advanced.");
                }
                if (run && preset.appBundle) errors.Add("Build & Run needs an APK. Turn off Android App Bundle under Advanced.");
                if (run && string.IsNullOrWhiteSpace(adb)) errors.Add("ADB was not found. Install Android SDK tools or select an ADB executable under Advanced.");
                if (run && !hasReadyDevices) errors.Add("Select at least one ready device. Authorize or reconnect unavailable devices first.");
            }
            return errors;
        }

        public static BuildRecord Build(BuildPreset preset, string version, int code, bool autoRunDesktop, string outputOverride = null, string[] copyFolders = null)
        {
            if (Busy || BuildPipeline.isBuildingPlayer) throw new BuildFailedException("Another build is in progress.");
            string recovery = BuildRecovery.Recover();
            if (recovery.StartsWith("Build recovery needs attention", StringComparison.Ordinal)) throw new BuildFailedException(recovery);
            string root = BuildStorage.BuildOutputRoot(preset, outputOverride);
            string[] copyRoots = BuildStorage.CopyOutputRoots(root, copyFolders ?? BuildStorage.MachineStore.Read().additionalOutputFolders.ToArray());
            var errors = Validate(preset, version, code, false, false, "", root);
            if (errors.Count > 0) throw new BuildFailedException(string.Join("\n", errors));
            Busy = true;
            string oldVersion = PlayerSettings.bundleVersion;
            int oldCode = PlayerSettings.Android.bundleVersionCode;
            string oldIosCode = PlayerSettings.iOS.buildNumber;
            bool oldBundle = EditorUserBuildSettings.buildAppBundle;
            bool oldDevelopment = EditorUserBuildSettings.development;
            WebBuildSettings oldWeb = Target == BuildTarget.WebGL ? WebBuildSettings.Capture() : null;
            bool succeeded = false;
            BuildJournal journal = null;
            var record = new BuildRecord
            {
                id = Guid.NewGuid().ToString("N"), utc = DateTime.UtcNow.ToString("o"),
                package = Package, target = Target.ToString(), version = version, code = code, preset = preset.name,
                minimumSdk = (int)PlayerSettings.Android.minSdkVersion, abis = AndroidAbis(), result = "Failed"
            };
            try
            {
                string name = BuildStorage.SafeName(preset.namingTemplate.Replace("{package}", record.package).Replace("{version}", version)
                    .Replace("{code}", code.ToString()).Replace("{preset}", preset.name).Replace("{target}", record.target));
                // The selected output folder is the actual destination. A stable name means
                // rebuilding the same target/version replaces that artifact instead of creating
                // another preset/timestamp directory.
                Directory.CreateDirectory(root);
                record.path = Path.Combine(root, name + Extension(Target, preset.appBundle));
                if (Target == BuildTarget.WebGL) WebPreview.Stop();
                journal = BuildRecovery.Begin(root);
                string stagedPath = Path.Combine(BuildRecovery.Stage(journal), Path.GetFileName(record.path));
                // Changes occur only after validation and output creation. Failed builds roll versioning back.
                PlayerSettings.bundleVersion = version;
                if (Target == BuildTarget.Android) PlayerSettings.Android.bundleVersionCode = code;
                if (Target == BuildTarget.iOS) PlayerSettings.iOS.buildNumber = code.ToString();
                if (Target == BuildTarget.Android) EditorUserBuildSettings.buildAppBundle = preset.appBundle;
                EditorUserBuildSettings.development = preset.development;
                if (oldWeb != null)
                {
                    if (preset.web != null && preset.web.overrideSettings) preset.web.Apply();
                    record.web = WebBuildSettings.Capture();
                    record.web.budgetMB = preset.web == null ? 30 : preset.web.budgetMB;
                }
                var options = preset.development ? BuildOptions.Development : BuildOptions.None;

                // Use the active platform's effective Player Settings and explicit scenes/options.
                // Unity 6 profile scene overrides are resolved above; no platform switching during a job.
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = Scenes(), target = Target, targetGroup = Group,
                    locationPathName = stagedPath, options = options
                });
                record.result = report.summary.result.ToString();
                record.seconds = report.summary.totalTime.TotalSeconds;
                record.bytes = (long)report.summary.totalSize;
                // Existing IPostprocessBuildWithReport integrations continue to work without a hard dependency.
                try { BuildCompleted?.Invoke(report); }
                catch (Exception e) { Debug.LogWarning("[Build Automator] Optional build listener failed: " + e.Message); }
                if (report.summary.result == BuildResult.Succeeded)
                {
                    if (!File.Exists(stagedPath) && !Directory.Exists(stagedPath)) throw new IOException("Unity reported success but the artifact is missing.");
                    if (File.Exists(stagedPath)) { record.bytes = new FileInfo(BuildStorage.FilePath(stagedPath)).Length; record.sha256 = BuildStorage.HashFile(stagedPath); }
                    else if (Target == BuildTarget.WebGL) record.bytes = WebArtifacts.Analyze(stagedPath, System.Threading.CancellationToken.None).bytes;
                    BuildStorage.Write(stagedPath + ".build-manifest.json", record);
                    BuildRecovery.Publish(journal);
                    BuildRecovery.Commit(journal);
                    succeeded = true;
                    try { ArtifactPublished?.Invoke(record); }
                    catch (Exception e) { Debug.LogWarning("[Build Automator] Optional artifact listener failed: " + e.Message); }
                }
                if (succeeded && autoRunDesktop)
                {
                    try
                    {
                        if (Target == BuildTarget.WebGL)
                        {
                            WebPreview.Start(record.path, 0, false);
                            Application.OpenURL(WebPreview.Url);
                        }
                        else if (Target == BuildTarget.StandaloneOSX) System.Diagnostics.Process.Start("/usr/bin/open", Adb.Quote(record.path));
                        else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(record.path) { UseShellExecute = true, WorkingDirectory = root });
                        record.deployment = Target == BuildTarget.WebGL ? "Browser preview requested: " + WebPreview.Url : "Player launch requested.";
                    }
                    catch (Exception e) { record.deployment = "Build succeeded; player launch failed: " + e.Message; }
                }
                if (succeeded)
                {
                    foreach (string destination in copyRoots)
                    {
                        try
                        {
                            EditorUtility.DisplayProgressBar("Build Automator", "Copying build to " + destination, 0.95f);
                            BuildStorage.CopyPublishedBuild(record.path, destination);
                            record.copiedLocations.Add(Path.Combine(destination, Path.GetFileName(record.path)));
                        }
                        catch (Exception e)
                        {
                            string error = destination + ": " + e.Message;
                            record.copyErrors.Add(error);
                            Debug.LogWarning("[Build Automator] Build succeeded; copy failed: " + error);
                        }
                        finally { EditorUtility.ClearProgressBar(); }
                    }
                }
                return record;
            }
            finally
            {
                if (!succeeded && record.result == "Succeeded") record.result = "Failed";
                if (!succeeded)
                {
                    PlayerSettings.bundleVersion = oldVersion;
                    PlayerSettings.Android.bundleVersionCode = oldCode;
                    PlayerSettings.iOS.buildNumber = oldIosCode;
                }
                EditorUserBuildSettings.buildAppBundle = oldBundle;
                EditorUserBuildSettings.development = oldDevelopment;
                if (oldWeb != null) oldWeb.Apply();
                try { BuildRecovery.Finish(journal); }
                catch (Exception e) { Debug.LogError("[Build Automator] Recovery remains pending: " + e.Message); }
                Busy = false;
                try
                {
                    if (succeeded && !string.IsNullOrEmpty(record.path)) BuildStorage.Write(record.path + ".build-manifest.json", record);
                    var state = BuildStorage.Read<LocalState>(BuildStorage.LocalPath) ?? new LocalState();
                    if (state.history == null) state.history = new List<BuildRecord>();
                    state.history.Insert(0, record);
                    state.history = state.history.Take(100).ToList();
                    BuildStorage.Write(BuildStorage.LocalPath, state);
                }
                catch (Exception e) { Debug.LogWarning("[Build Automator] Build metadata could not be saved: " + e.Message); }
            }
        }

        private static string[] AndroidAbis()
        {
            var list = new List<string>();
            var architectures = PlayerSettings.Android.targetArchitectures;
            if ((architectures & AndroidArchitecture.ARMv7) != 0) list.Add("armeabi-v7a");
            if ((architectures & AndroidArchitecture.ARM64) != 0) list.Add("arm64-v8a");
#if !UNITY_6000_0_OR_NEWER
            if ((architectures & AndroidArchitecture.X86) != 0) list.Add("x86");
#endif
            if ((architectures & AndroidArchitecture.X86_64) != 0) list.Add("x86_64");
            return list.ToArray();
        }

        private static string Extension(BuildTarget target, bool bundle)
        {
            switch (target)
            {
                case BuildTarget.Android: return bundle ? ".aab" : ".apk";
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64: return ".exe";
                case BuildTarget.StandaloneOSX: return ".app";
                case BuildTarget.StandaloneLinux64: return ".x86_64";
                default: return "";
            }
        }

        // Unity -batchmode -quit -projectPath <project> -buildTarget Android
        //       -executeMethod OutBuild.BuildJobs.BuildFromCommandLine -logFile <log>
        public static void BuildFromCommandLine()
        {
            var presets = BuildStorage.Read<PresetFile>(BuildStorage.PresetPath);
            string invalid = BuildStorage.ValidatePresets(presets);
            if (invalid.Length > 0) throw new BuildFailedException(invalid);
            string selected = Environment.GetEnvironmentVariable("OUT_BUILD_PRESET") ?? "Release";
            var preset = presets.presets.FirstOrDefault(p => string.Equals(p.name, selected, StringComparison.OrdinalIgnoreCase));
            if (preset == null) throw new BuildFailedException("Unknown OUT_BUILD_PRESET: " + selected);
            string output = Environment.GetEnvironmentVariable("OUT_BUILD_OUTPUT");
            // An explicit CI output takes precedence without changing the machine preference or preset.
            string version = Environment.GetEnvironmentVariable("OUT_BUILD_VERSION") ?? PlayerSettings.bundleVersion;
            int currentCode = Target == BuildTarget.iOS && int.TryParse(PlayerSettings.iOS.buildNumber, out int ios) ? ios : PlayerSettings.Android.bundleVersionCode;
            int code = checked(currentCode + (preset.incrementVersionCode ? 1 : 0));
            string codeInput = Environment.GetEnvironmentVariable("OUT_BUILD_CODE");
            if (codeInput != null && !int.TryParse(codeInput, out code)) throw new BuildFailedException("OUT_BUILD_CODE must be an integer.");
            string oldKey = PlayerSettings.Android.keystorePass, oldAlias = PlayerSettings.Android.keyaliasPass;
            try
            {
                PlayerSettings.Android.keystorePass = Environment.GetEnvironmentVariable("OUT_BUILD_KEYSTORE_PASS") ?? oldKey;
                PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable("OUT_BUILD_ALIAS_PASS") ?? oldAlias;
                var record = Build(preset, version, code, false, output);
                if (record.result != "Succeeded") throw new BuildFailedException("Build result: " + record.result);
                Debug.Log("[Build Automator] Build succeeded: " + record.path);
            }
            finally
            {
                PlayerSettings.Android.keystorePass = oldKey;
                PlayerSettings.Android.keyaliasPass = oldAlias;
            }
        }
    }
}
