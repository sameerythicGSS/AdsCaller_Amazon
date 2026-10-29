#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A sequential popup wizard that collects SDK keys and Ad IDs during the AdsCaller setup flow.
/// </summary>
public class AdsCredentialsPopup : EditorWindow
{
    #region Variables
    private Action _onComplete;
    private AdsCallerConfig _config;

    private int _currentStep = 0;
    private string _currentInput = "";
    private bool _isComplete = false;

    private string[] _stepTitles;
    private string[] _stepDescriptions;
    private Action<string>[] _stepSaveActions;
    #endregion

    #region Initialization
    public static void ShowWindow(AdsCallerConfig config, Action onComplete)
    {
        var window = GetWindow<AdsCredentialsPopup>(true, "Ads Credentials Setup", true);
        window.minSize = new Vector2(400, 150);
        window.maxSize = new Vector2(400, 150);

        // Center the window on screen for better UX
        window.position = new Rect(Screen.currentResolution.width / 2f - 200f, Screen.currentResolution.height / 2f - 75f, 400, 150);

        window._config = config;
        window._onComplete = onComplete;
        window.InitSteps();

        window.ShowUtility();
    }

    private void InitSteps()
    {
#if USE_ADMOB
        int totalSteps = 7;
#else
        int totalSteps = 5;
#endif
        _stepTitles = new string[totalSteps];
        _stepDescriptions = new string[totalSteps];
        _stepSaveActions = new Action<string>[totalSteps];

        _stepTitles[0] = "AppLovin MAX SDK Key";
        _stepDescriptions[0] = "Enter your MAX SDK Key:";
        _stepSaveActions[0] = (val) => _config.MaxSdkKey = val;

        _stepTitles[1] = "Interstitial Ad Unit ID";
        _stepDescriptions[1] = "Enter your MAX Interstitial Ad ID:";
        _stepSaveActions[1] = (val) => _config.InterstitialAdUnitId = val;

        _stepTitles[2] = "Rewarded Ad Unit ID";
        _stepDescriptions[2] = "Enter your MAX Rewarded Ad ID:";
        _stepSaveActions[2] = (val) => _config.RewardedAdUnitId = val;

        _stepTitles[3] = "Banner Ad Unit ID";
        _stepDescriptions[3] = "Enter your MAX Banner Ad ID:";
        _stepSaveActions[3] = (val) => _config.BannerAdUnitId = val;

        _stepTitles[4] = "Large Banner (MREC) Ad Unit ID";
        _stepDescriptions[4] = "Enter your MAX MREC Ad ID:";
        _stepSaveActions[4] = (val) => _config.LargeBannerAdUnitId = val;

#if USE_ADMOB
        _stepTitles[5] = "AdMob App ID";
        _stepDescriptions[5] = "Enter your Google AdMob App ID:";
        _stepSaveActions[5] = (val) => _config.AdMobAppId = val;

        _stepTitles[6] = "App Open Ad Unit ID";
        _stepDescriptions[6] = "Enter your AdMob App Open Ad ID:";
        _stepSaveActions[6] = (val) => _config.AppOpenAdUnitId = val;
#endif
    }
    #endregion

    #region GUI Loop
    private void OnGUI()
    {
        if (_config == null)
        {
            Close();
            return;
        }

        GUILayout.Space(10);
        EditorGUILayout.LabelField($"Step {_currentStep + 1} of {_stepTitles.Length}: {_stepTitles[_currentStep]}", EditorStyles.boldLabel);
        GUILayout.Space(5);
        EditorGUILayout.LabelField(_stepDescriptions[_currentStep]);
        GUILayout.Space(5);

        GUI.SetNextControlName("IdInput");
        _currentInput = EditorGUILayout.TextField(_currentInput);

        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        // Check for Return/Enter key presses
        bool hitEnter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

        if (GUILayout.Button("Go", GUILayout.Width(80), GUILayout.Height(30)) || hitEnter)
        {
            if (hitEnter) Event.current.Use(); // Consume the event so it doesn't bleed into other editor panels
            AdvanceStep();
        }

        EditorGUILayout.EndHorizontal();
        GUILayout.Space(10);

        // Keep the text field permanently focused so you can type immediately
        GUI.FocusControl("IdInput");
    }

    private void PropagateKeysToSDKSettings()
    {
        // 1. Propagate to AppLovin MAX
        try
        {
            // Find the AppLovinSettings class (it might be in a specific assembly depending on the SDK version)
            Type appLovinType = Type.GetType("AppLovinSettings, AppLovin.IntegrationManager")
                             ?? Type.GetType("AppLovinSettings, Assembly-CSharp-Editor")
                             ?? Type.GetType("AppLovinSettings");

            if (appLovinType != null)
            {
                PropertyInfo instanceProp = appLovinType.GetProperty("Instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                if (instanceProp != null)
                {
                    object instance = instanceProp.GetValue(null);
                    if (instance != null)
                    {
                        PropertyInfo sdkKeyProp = appLovinType.GetProperty("SdkKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                        if (sdkKeyProp != null)
                        {
                            sdkKeyProp.SetValue(instance, _config.MaxSdkKey);
                            EditorUtility.SetDirty((UnityEngine.Object)instance);
                            Debug.Log("[Ads Setup] MAX SDK Key successfully propagated to AppLovinSettings.");
                        }
                    }
                }
            }
        }
        catch (Exception e) { Debug.LogWarning("[Ads Setup] Could not propagate MAX SDK Key: " + e.Message); }

        // 2. Propagate to Google AdMob
#if USE_ADMOB
        try 
        {
            // AdMob's settings class is internal, so we must access it via Reflection
            Type admobType = Type.GetType("GoogleMobileAds.Editor.GoogleMobileAdsSettings, GoogleMobileAds.Editor") 
                          ?? Type.GetType("GoogleMobileAds.Editor.GoogleMobileAdsSettings, Assembly-CSharp-Editor");

            if (admobType != null)
            {
                // Depending on the version, AdMob uses LoadInstance() or an Instance property
                System.Reflection.MethodInfo loadInstanceMethod = admobType.GetMethod("LoadInstance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                System.Reflection.PropertyInfo instanceProp = admobType.GetProperty("Instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                
                object instance = null;
                if (loadInstanceMethod != null) instance = loadInstanceMethod.Invoke(null, null);
                else if (instanceProp != null) instance = instanceProp.GetValue(null);

                if (instance != null)
                {
                    System.Reflection.PropertyInfo androidProp = admobType.GetProperty("GoogleMobileAdsAndroidAppId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                    System.Reflection.PropertyInfo iosProp = admobType.GetProperty("GoogleMobileAdsIOSAppId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

                    if (androidProp != null) androidProp.SetValue(instance, _config.AdMobAppId);
                    if (iosProp != null) iosProp.SetValue(instance, _config.AdMobAppId);

                    EditorUtility.SetDirty((UnityEngine.Object)instance);
                    Debug.Log("[Ads Setup] AdMob App ID successfully propagated to GoogleMobileAdsSettings.");
                }
            }
        }
        catch (Exception e) { Debug.LogWarning("[Ads Setup] Could not propagate AdMob App ID: " + e.Message); }
#endif

        AssetDatabase.SaveAssets();
    }

    private void AdvanceStep()
    {
        _stepSaveActions[_currentStep]?.Invoke(_currentInput);
        _currentStep++;
        _currentInput = "";

        if (_currentStep >= _stepTitles.Length)
        {
            _isComplete = true;
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();

            // --> Call the propagation method here
            PropagateKeysToSDKSettings();

            _onComplete?.Invoke();
            Close();
        }
        else
        {
            Repaint();
        }
    }
    #endregion

    #region Failsafe
    private void OnDestroy()
    {
        // If the wizard is closed forcefully before completion, resume the setup flow anyway
        if (!_isComplete && _onComplete != null)
        {
            Debug.LogWarning("[Ads Caller] Wizard cancelled before finishing. Resuming scene injection.");
            _onComplete.Invoke();
        }
    }
    #endregion
}
#endif