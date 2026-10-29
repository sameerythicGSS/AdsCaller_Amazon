using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(AdsCallerConfig))]
public class AdsCallerConfigEditor : Editor
{
    // Keys & IDs
    SerializedProperty MaxSdkKey;
    SerializedProperty InterstitialAdUnitId;
    SerializedProperty RewardedAdUnitId;
    SerializedProperty BannerAdUnitId;
    SerializedProperty LargeBannerAdUnitId;

#if USE_ADMOB
    SerializedProperty AppOpenAdUnitId;
#endif

    // General
    SerializedProperty initializeOnStart;
    SerializedProperty isTestLog;
    SerializedProperty showMediationDebugger;

    // App Open
#if USE_ADMOB
    SerializedProperty canShowAppOpen;
    SerializedProperty appOpenStrategy; 
    SerializedProperty AppOpenCooldown;
    
    private const string GoogleTestAppOpenId = "ca-app-pub-3940256099942544/9257395921";
#endif

    // Banner
    SerializedProperty canShowBanner;
    SerializedProperty defaultBannerPosition;
    SerializedProperty showBannerOnSceneChange;
    SerializedProperty bannerDelay;

    // Large Banner (MREC)
    SerializedProperty canShowLargeBanner;
    SerializedProperty defaultLargeBannerPosition;

    // Interstitial Logic
    SerializedProperty canShowInterstitial;
    SerializedProperty interstitialCooldown;
    SerializedProperty interstitialShowingDelay;

    private void OnEnable()
    {
        // Keys & IDs
        MaxSdkKey = serializedObject.FindProperty("MaxSdkKey");
        InterstitialAdUnitId = serializedObject.FindProperty("InterstitialAdUnitId");
        RewardedAdUnitId = serializedObject.FindProperty("RewardedAdUnitId");
        BannerAdUnitId = serializedObject.FindProperty("BannerAdUnitId");
        LargeBannerAdUnitId = serializedObject.FindProperty("LargeBannerAdUnitId");

#if USE_ADMOB
        AppOpenAdUnitId = serializedObject.FindProperty("AppOpenAdUnitId");
        canShowAppOpen = serializedObject.FindProperty("canShowAppOpen");
        appOpenStrategy = serializedObject.FindProperty("appOpenStrategy"); 
        AppOpenCooldown = serializedObject.FindProperty("AppOpenCooldown");
#endif

        // General Settings
        initializeOnStart = serializedObject.FindProperty("initializeOnStart");
        isTestLog = serializedObject.FindProperty("isTestLog");
        showMediationDebugger = serializedObject.FindProperty("showMediationDebugger");

        // Banner Settings
        canShowBanner = serializedObject.FindProperty("canShowBanner");
        defaultBannerPosition = serializedObject.FindProperty("defaultBannerPosition");
        showBannerOnSceneChange = serializedObject.FindProperty("showBannerOnSceneChange");
        bannerDelay = serializedObject.FindProperty("bannerDelay");

        // Large Banner (MREC)
        canShowLargeBanner = serializedObject.FindProperty("canShowLargeBanner");
        defaultLargeBannerPosition = serializedObject.FindProperty("defaultLargeBannerPosition");

        // Interstitial Logic
        canShowInterstitial = serializedObject.FindProperty("canShowInterstitial");
        interstitialCooldown = serializedObject.FindProperty("interstitialCooldown");
        interstitialShowingDelay = serializedObject.FindProperty("interstitialShowingDelay");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // ---------------------------------------------------------------------
        // 1. SDK Keys & IDs
        // ---------------------------------------------------------------------
        EditorGUILayout.LabelField("SDK Configuration", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Enter your AppLovin MAX and Google AdMob Ad Unit IDs.", MessageType.Info);

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(MaxSdkKey);
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            SyncAppLovinSdkKey(MaxSdkKey.stringValue);
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.PropertyField(InterstitialAdUnitId);
        EditorGUILayout.PropertyField(RewardedAdUnitId);
        EditorGUILayout.PropertyField(BannerAdUnitId);
        EditorGUILayout.PropertyField(LargeBannerAdUnitId, new GUIContent("MREC Ad Unit ID"));

        EditorGUILayout.Space(5);

#if USE_ADMOB
        EditorGUILayout.PropertyField(AppOpenAdUnitId);

        bool isCurrentlyTestId = AppOpenAdUnitId.stringValue == GoogleTestAppOpenId;
        string storageKey = $"AdsConfig_TempAppOpenID_{target.GetInstanceID()}";

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(EditorGUIUtility.labelWidth);

        bool useTestId = EditorGUILayout.ToggleLeft("Use Google Test AppOpen ID", isCurrentlyTestId);

        if (useTestId != isCurrentlyTestId)
        {
            if (useTestId)
            {
                if (AppOpenAdUnitId.stringValue != GoogleTestAppOpenId)
                {
                    EditorPrefs.SetString(storageKey, AppOpenAdUnitId.stringValue);
                }
                AppOpenAdUnitId.stringValue = GoogleTestAppOpenId;
            }
            else
            {
                if (EditorPrefs.HasKey(storageKey))
                {
                    AppOpenAdUnitId.stringValue = EditorPrefs.GetString(storageKey);
                }
                else
                {
                    AppOpenAdUnitId.stringValue = "";
                }
            }
        }
        EditorGUILayout.EndHorizontal();
#endif

        EditorGUILayout.Space(15);

        // ---------------------------------------------------------------------
        // 2. General Settings
        // ---------------------------------------------------------------------
        EditorGUILayout.LabelField("General Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(initializeOnStart);
        EditorGUILayout.PropertyField(isTestLog);
        EditorGUILayout.Space(5);
        EditorGUILayout.PropertyField(showMediationDebugger);
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Rewarded availability feedback", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("showAdNotAvailablePopup"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("adNotAvailablePopup"));

        EditorGUILayout.Space(15);

        // ---------------------------------------------------------------------
        // 3. App Open Strategy
        // ---------------------------------------------------------------------
        EditorGUILayout.LabelField("App Open Strategy", EditorStyles.boldLabel);

#if USE_ADMOB
        EditorGUILayout.PropertyField(canShowAppOpen);
        EditorGUILayout.PropertyField(appOpenStrategy);
        EditorGUILayout.PropertyField(AppOpenCooldown);

        if (appOpenStrategy != null)
        {
            if (appOpenStrategy.enumValueIndex == 0)
                EditorGUILayout.HelpBox("Strategy 0: Show if UserEnteredGameplay > 1 (Start & Resume).", MessageType.None);
            else if (appOpenStrategy.enumValueIndex == 1)
                EditorGUILayout.HelpBox("Strategy 1: Standard Logic (Shows immediately after load).", MessageType.None);
            else if (appOpenStrategy.enumValueIndex == 2)
                EditorGUILayout.HelpBox("Strategy 2: Delayed (Initial load check is triggered after 15s).", MessageType.None);
        }
#else
        EditorGUILayout.HelpBox("AdMob App Open features are currently disabled. Add 'USE_ADMOB' to your Scripting Define Symbols in Player Settings to enable them.", MessageType.Warning);
#endif

        EditorGUILayout.Space(15);

        // ---------------------------------------------------------------------
        // 4. Banner Settings
        // ---------------------------------------------------------------------
        EditorGUILayout.LabelField("Banner Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(canShowBanner);

        if (canShowBanner.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(defaultBannerPosition);
            EditorGUILayout.PropertyField(showBannerOnSceneChange);
            if (showBannerOnSceneChange.boolValue)
            {
                EditorGUILayout.PropertyField(bannerDelay);
            }
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(15);

        // ---------------------------------------------------------------------
        // 5. Large Banner (MREC) Settings
        // ---------------------------------------------------------------------
        EditorGUILayout.LabelField("MREC Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(canShowLargeBanner);

        if (canShowLargeBanner.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(defaultLargeBannerPosition);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(15);

        // ---------------------------------------------------------------------
        // 6. Interstitial Settings
        // ---------------------------------------------------------------------
        EditorGUILayout.LabelField("Interstitial Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(canShowInterstitial);
        EditorGUILayout.PropertyField(interstitialCooldown);
        EditorGUILayout.PropertyField(interstitialShowingDelay);

        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>
    /// Uses AssetDatabase and Reflection logic to automatically find and sync 
    /// the AppLovin Integration Manager Settings without hardcoded namespace dependencies.
    /// </summary>
    private void SyncAppLovinSdkKey(string newKey)
    {
        string[] guids = AssetDatabase.FindAssets("t:AppLovinSettings");
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            ScriptableObject settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);

            if (settings != null)
            {
                SerializedObject serializedSettings = new SerializedObject(settings);
                SerializedProperty sdkKeyProp = serializedSettings.FindProperty("sdkKey") ??
                                                serializedSettings.FindProperty("SdkKey") ??
                                                serializedSettings.FindProperty("_sdkKey");

                if (sdkKeyProp != null && sdkKeyProp.stringValue != newKey)
                {
                    sdkKeyProp.stringValue = newKey;
                    serializedSettings.ApplyModifiedProperties();
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"Automatically synced MAX SDK Key in AppLovin Integration Manager.");
                }
            }
        }
    }
}