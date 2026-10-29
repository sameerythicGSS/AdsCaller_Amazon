using UnityEngine;
using System;

/// <summary>
/// Holds all global configurations, keys, and settings for the AdsCaller.
/// </summary>
[CreateAssetMenu(fileName = "AdsCallerConfig", menuName = "Ads Caller/AdsCallerConfig")]
public class AdsCallerConfig : ScriptableObject
{
    #region Configuration - Keys & IDs
    [Header("SDK Keys")]
    public string MaxSdkKey;
    public string InterstitialAdUnitId;
    public string RewardedAdUnitId;
    public string BannerAdUnitId;
    public string LargeBannerAdUnitId;
#if USE_ADMOB
    public string AdMobAppId;
    public string AppOpenAdUnitId;
#endif
    #endregion

    #region Configuration - Settings
    [Header("Rewarded availability feedback")]
    [Tooltip("Show AdNotAvailable for two real-time seconds when a rewarded request cannot be shown.")]
    public bool showAdNotAvailablePopup = false;
    [Tooltip("Optional override. When empty, the shared AdNotAvailable Resources prefab is used.")]
    public GameObject adNotAvailablePopup;

    [Header("General Settings")]
    public bool initializeOnStart = true;
    public bool isTestLog = false;
    public bool showMediationDebugger = false;

#if USE_ADMOB
    [Header("App Open Strategy")]
    public bool canShowAppOpen = true;
    public EnumsJar.AppOpenStrategy appOpenStrategy = EnumsJar.AppOpenStrategy.Standard;
    public float AppOpenCooldown = 5f;
#endif

    [Header("Banner & MREC Settings")]
    public bool canShowBanner = true;
    public bool canShowLargeBanner = true;
    public MaxSdkBase.AdViewPosition defaultBannerPosition = MaxSdkBase.AdViewPosition.BottomCenter;
    public MaxSdkBase.AdViewPosition defaultLargeBannerPosition = MaxSdkBase.AdViewPosition.Centered;
    public bool showBannerOnSceneChange = true;
    public float bannerDelay = 2f;

    [Header("Interstitial Settings")]
    public bool canShowInterstitial = true;
    public float interstitialCooldown = 5f;
    public float interstitialShowingDelay = 0.5f;
    #endregion
}