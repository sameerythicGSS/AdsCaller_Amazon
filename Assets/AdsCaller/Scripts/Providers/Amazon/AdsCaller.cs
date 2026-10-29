#if USE_ADMOB
using GoogleMobileAds.Api;
#endif
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A robust Singleton manager responsible for handling ad initialization, display, and revenue tracking (MAX/AppLovin).
/// <para>
/// <b>IMPORTANT:</b> AdMob is disabled by default to prevent compilation errors in projects without the SDK. 
/// To enable AdMob, go to <b>Edit > Project Settings > Player > Other Settings > Script Compilation</b> 
/// and add <c>USE_ADMOB</c> to the <b>Scripting Define Symbols</b> list.
/// </para>
/// </summary>
public class AdsCaller : MonoBehaviour, IOutAdsProvider, IAdsAvailabilityProvider, IResultOutAdsProvider, IManagedFullscreenProvider, IRewardedUnavailableFeedback
{
    public static AdsCaller Instance { get; private set; }

    [SerializeField] private AdsCallerConfig Database;

    #region Internal State
    [HideInInspector]
    public static bool isSceneLoaded;

    public static bool IsInterstitialShowing;

    [HideInInspector]
    public bool isShowingBanner = false;
    public bool IsBannerAdShowing => isShowingBanner;
    public bool IsInitialized => isMaxInitialized;
    public event Action<AdAvailability> AvailabilityChanged;

#if USE_ADMOB
    private AppOpenAd appOpenAd;
    private bool isAppOpenLoading = false;
    private bool isShowingAppOpenAd = false;
    private DateTime appOpenExpireTime;
    private readonly TimeSpan APP_OPEN_TIMEOUT = TimeSpan.FromHours(4);
    private float lastAppOpenCheckTime = 0f;
#endif

    private int interstitialRetryAttempt;
    private bool isInterstitialLoading = false;
    private string lastInterstitialLoadFailure;
    private float lastInterstitialTime = -Mathf.Infinity;

    private int rewardedRetryAttempt;
    private bool isRewardedLoading = false;
    private string lastRewardedLoadFailure;
    private AdsShowSession interstitialSession;
    private AdsShowSession rewardedSession;
#if USE_ADMOB
    private AdsShowSession appOpenSession;
#endif
    private const float ShowStartTimeoutSeconds = 20f;
    private const float FullscreenWatchdogSeconds = 300f;

    private bool isMaxInitialized = false;
    private bool isAdmobInitialized = false;
    private bool isFirstAdShown = false;
    private bool bannerActiveStatus = false;

    public static bool _pauseAppOpen = false;
    public static bool userEnteredGameplay = false;

    public static event Action OnInterstitialOpened;
    public static event Action OnInterstitialClosed;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (Database == null)
        {
            Debug.LogError("AdsCallerConfig is missing!");
            return;
        }

        if (Database.initializeOnStart)
        {
            _ = StartInitializingAdsAsync();
        }

        PlayerPrefs.SetInt("UserEnteredGameplay", PlayerPrefs.GetInt("UserEnteredGameplay", 0) + 1);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnEnable()
    {
        if (Instance == this)
            AdsProviderRegistry.Register(AdsProviderKind.Online, this);
        AdsRuntimeGate.Changed += HandleRuntimeGateChanged;
    }

    private void OnDisable()
    {
        AdsRuntimeGate.Changed -= HandleRuntimeGateChanged;
        AdsProviderRegistry.Unregister(AdsProviderKind.Online, this);
    }

    private void OnDestroy()
    {
        interstitialSession?.Complete(AdShowResult.Disabled, "Provider destroyed");
        rewardedSession?.Complete(AdShowResult.Disabled, "Provider destroyed");
#if USE_ADMOB
        appOpenSession?.Complete(AdShowResult.Disabled, "Provider destroyed");
#endif
        AdsProviderRegistry.Unregister(AdsProviderKind.Online, this);
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (Instance == this)
            Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Database.showBannerOnSceneChange && Database.canShowBanner)
        {
            _ = ShowBannerWithDelayAsync(Database.bannerDelay);
        }
    }
    #endregion

    #region Initialization
    private async Awaitable StartInitializingAdsAsync()
    {
        await Awaitable.WaitForSecondsAsync(0.5f);
        if (this == null) return;

        if (!AdsRuntimeGate.NetworkAdsAllowed)
        {
            Logger.Warn($"[AdsCaller/Network] SDK initialization blocked. Reason: {AdsRuntimeGate.Reason}");
            NotifyAllAvailability();
            return;
        }

        InitializeMaxAds();

#if USE_ADMOB
        InitializeGoogleMobileAds();
#endif

        if (Database.showMediationDebugger)
        {
            await Awaitable.WaitForSecondsAsync(1f);
            if (this != null) ShowDebugMediationManager();
        }
    }

    private void InitializeMaxAds()
    {
        MaxSdkCallbacks.OnSdkInitializedEvent += sdkConfiguration =>
        {
            isMaxInitialized = true;
            PrintLog("MAX SDK Initialized");
            Logger.Note("[AdsCaller/Network] MAX initialized. Network provider is active.");
            InitializeInterstitialAds();
            InitializeBannerAds();
            InitializeLargeBannerAds();
            InitializeRewardedAds();
            NotifyAllAvailability();
        };

        MaxSdk.SetSdkKey(Database.MaxSdkKey);
        MaxSdk.InitializeSdk();
    }

#if USE_ADMOB
    private void InitializeGoogleMobileAds()
    {
        MobileAds.SetiOSAppPauseOnBackground(true);
        MobileAds.RaiseAdEventsOnUnityMainThread = true;

        MobileAds.Initialize((InitializationStatus initstatus) =>
        {
            isAdmobInitialized = true;
            PrintLog("AdMob Initialized");

            if (Database.appOpenStrategy != EnumsJar.AppOpenStrategy.Delayed)
                CheckAndRequestAppOpen();
            else
                _ = CheckAndRequestAppOpenWithDelayAsync(15f);
        });
    }
#endif

    public void ShowDebugMediationManager()
    {
        MaxSdk.ShowMediationDebugger();
    }
    #endregion

    #region Interstitial Ads
    private void InitializeInterstitialAds()
    {
        MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += OnInterstitialLoadedEvent;
        MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += OnInterstitialFailedEvent;
        MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += OnInterstitialDisplayedEvent;
        MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += InterstitialFailedToDisplayEvent;
        MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += OnInterstitialDismissedEvent;
        MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += OnInterstitialRevenuePaidEvent;
        LoadInterstitial();
    }

    private void LoadInterstitial()
    {
        if (MaxSdk.IsInterstitialReady(Database.InterstitialAdUnitId) || isInterstitialLoading || !Database.canShowInterstitial) return;
        isInterstitialLoading = true;
        MaxSdk.LoadInterstitial(Database.InterstitialAdUnitId);
    }

    private void OnInterstitialLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
    {
        PrintLog($"Interstitial loaded; ad unit accepted: {adUnitId}");
        isInterstitialLoading = false;
        interstitialRetryAttempt = 0;
        lastInterstitialLoadFailure = null;
        NotifyAvailability(OutAdFormat.Interstitial);
    }

    private void OnInterstitialFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
    {
        isInterstitialLoading = false;
        interstitialRetryAttempt++;
        double retryDelay = Math.Pow(2, Math.Min(6, interstitialRetryAttempt));

        lastInterstitialLoadFailure = LogNetworkFailure(OutAdFormat.Interstitial, "load", adUnitId, errorInfo, retryDelay);
        NotifyAvailability(OutAdFormat.Interstitial);
        _ = LoadInterstitialWithDelayAsync((float)retryDelay);
    }

    private void InterstitialFailedToDisplayEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo, MaxSdkBase.AdInfo adInfo)
    {
        isInterstitialLoading = false;
        PrintLog("Interstitial failed to display with error code: " + errorInfo.Code);
        string reason = DescribeError(errorInfo, adUnitId);
        Logger.Error($"[AdsCaller/Network] Interstitial did not appear. Display failed: {reason}");
        FinishInterstitial(AdShowResult.Failed, reason);
        LoadInterstitial();
    }

    private void OnInterstitialDisplayedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
    {
        if (interstitialSession == null) return;
        interstitialSession.Open();
        OnInterstitialOpened?.Invoke();
    }

    private void OnInterstitialDismissedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
    {
        PrintLog("Interstitial dismissed");
        FinishInterstitial(AdShowResult.Closed, "user closed the network interstitial");
        LoadInterstitial();
    }

    private void OnInterstitialRevenuePaidEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
    {
        PrintLog("Interstitial revenue paid");
        TrackMaxRevenue(adInfo);
    }

    public void ShowInterstitial()
    {
        ShowInterstitial(null);
    }

    public void ShowInterstitial(Action<AdShowOutcome> onResult)
    {
        if (Database == null) { FailInterstitial(onResult, AdShowResult.ProviderUnavailable, "AdsCallerConfig is missing"); return; }
        if (!AdsRuntimeGate.NetworkAdsAllowed) { FailInterstitial(onResult, AdShowResult.Disabled, $"network gate blocked ads: {AdsRuntimeGate.Reason}"); return; }
        if (AreAdsRemoved()) { FailInterstitial(onResult, AdShowResult.Removed, "ads were removed by the user"); return; }
        if (!Database.canShowInterstitial) { FailInterstitial(onResult, AdShowResult.Disabled, "interstitials are disabled in AdsCallerConfig"); return; }
        float remaining = Database.interstitialCooldown - (Time.time - lastInterstitialTime);
        if (remaining > 0f) { FailInterstitial(onResult, AdShowResult.Capped, $"network interstitial cooldown has {remaining:0.0}s remaining"); return; }
        if (!MaxSdk.IsInterstitialReady(Database.InterstitialAdUnitId))
        {
            FailInterstitial(onResult, AdShowResult.NotReady, GetInterstitialUnavailableReason());
            LoadInterstitial();
            return;
        }
        if (!AdsShowSession.TryBegin(OutAdFormat.Interstitial, AdsProviderKind.Online, null, onResult, out AdsShowSession session)) return;
        interstitialSession = session;
        _ = ShowInterstitialAsync(session);
    }

    private async Awaitable ShowInterstitialAsync(AdsShowSession session)
    {
        await Awaitable.WaitForSecondsAsync(Database.interstitialShowingDelay);
        if (this == null || interstitialSession != session || session.IsCompleted) return;
        if (!AdsRuntimeGate.NetworkAdsAllowed || AreAdsRemoved() || !Database.canShowInterstitial)
        {
            FinishInterstitial(AdShowResult.Disabled, "interstitial eligibility changed before show");
            return;
        }
        if (MaxSdk.IsInterstitialReady(Database.InterstitialAdUnitId))
        {
            _pauseAppOpen = true;
            lastInterstitialTime = Time.time;
            IsInterstitialShowing = true;
            try
            {
                MaxSdk.ShowInterstitial(Database.InterstitialAdUnitId);
                _ = WatchInterstitialAsync(session);
            }
            catch (Exception exception) { FinishInterstitial(AdShowResult.Failed, exception.Message); }
        }
        else
        {
            FinishInterstitial(AdShowResult.NotReady, "interstitial became unavailable before show");
            LoadInterstitial();
        }
    }

    private void FinishInterstitial(AdShowResult result, string reason)
    {
        AdsShowSession session = interstitialSession;
        interstitialSession = null;
        bool notifyClosed = IsInterstitialShowing || session != null && session.DidOpen;
        _pauseAppOpen = false;
        IsInterstitialShowing = false;
        if (notifyClosed) OnInterstitialClosed?.Invoke();
        session?.Complete(result, reason);
    }

    private async Awaitable WatchInterstitialAsync(AdsShowSession session)
    {
        await Awaitable.WaitForSecondsAsync(ShowStartTimeoutSeconds);
        if (this == null || interstitialSession != session || session.IsCompleted) return;
        if (!session.DidOpen)
        {
            FinishInterstitial(AdShowResult.TimedOut, "MAX interstitial did not report opening");
            LoadInterstitial();
            return;
        }
        await Awaitable.WaitForSecondsAsync(FullscreenWatchdogSeconds - ShowStartTimeoutSeconds);
        if (this != null && interstitialSession == session && !session.IsCompleted)
        {
            FinishInterstitial(AdShowResult.TimedOut, "MAX interstitial did not report closing");
            LoadInterstitial();
        }
    }

    #endregion

    #region Rewarded Ads

    private void InitializeRewardedAds()
    {
        MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += OnRewardedAdLoadedEvent;
        MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += OnRewardedAdLoadFailedEvent;
        MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += OnRewardedAdDisplayFailedEvent;
        MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent += (adUnitId, adInfo) => rewardedSession?.Open();

        MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += (adUnitId, reward, adInfo) =>
        {
            rewardedSession?.EarnReward();
            PrintLog("Reward earned from MAX reward callback.");
        };

        MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += (adUnitId, adInfo) =>
        {
            _pauseAppOpen = false;

            AdsShowSession session = rewardedSession;
            rewardedSession = null;
            bool completed = session != null && session.RewardEarned;
            session?.Complete(AdShowResult.Closed,
                completed ? "MAX reward callback received" : "rewarded ad closed before a reward was earned");

            LoadRewardedAd();
        };

        MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent += (adUnitId, adInfo) => TrackMaxRevenue(adInfo);

        LoadRewardedAd();
    }

    private void LoadRewardedAd()
    {
        if (MaxSdk.IsRewardedAdReady(Database.RewardedAdUnitId) || isRewardedLoading) return;
        isRewardedLoading = true;
        MaxSdk.LoadRewardedAd(Database.RewardedAdUnitId);
    }

    private void OnRewardedAdLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
    {
        isRewardedLoading = false;
        rewardedRetryAttempt = 0;
        lastRewardedLoadFailure = null;
        PrintLog($"Rewarded ad loaded; ad unit accepted: {adUnitId}");
        NotifyAvailability(OutAdFormat.Rewarded);
    }

    private void OnRewardedAdLoadFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
    {
        isRewardedLoading = false;
        rewardedRetryAttempt++;
        double retryDelay = Math.Pow(2, Math.Min(6, rewardedRetryAttempt));

        lastRewardedLoadFailure = LogNetworkFailure(OutAdFormat.Rewarded, "load", adUnitId, errorInfo, retryDelay);
        NotifyAvailability(OutAdFormat.Rewarded);
        _ = LoadRewardedAdWithDelayAsync((float)retryDelay);
    }

    private void OnRewardedAdDisplayFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo, MaxSdkBase.AdInfo adInfo)
    {
        isRewardedLoading = false;
        _pauseAppOpen = false;
        PrintLog($"Rewarded ad failed to display with error: {errorInfo.Code}");
        Logger.Error($"[AdsCaller/Network] Rewarded ad did not appear. Display failed: {DescribeError(errorInfo, adUnitId)}");
        AdsShowSession session = rewardedSession;
        rewardedSession = null;
        session?.Complete(AdShowResult.Failed, DescribeError(errorInfo, adUnitId));
        LoadRewardedAd();
    }

    public void ShowRewardedAd(Action onSuccess, Action onFail = null)
    {
        ShowRewardedAd(onSuccess, outcome => { if (outcome.Result != AdShowResult.Completed) onFail?.Invoke(); });
    }

    public void ShowRewardedUnavailablePopup() => AdNotAvailablePopup.Show(Database != null && Database.showAdNotAvailablePopup, Database != null ? Database.adNotAvailablePopup : null);

    public void ShowRewardedAd(Action onSuccess, Action<AdShowOutcome> onResult)
    {
        onResult = AdNotAvailablePopup.WithFeedback(onResult, ShowRewardedUnavailablePopup);
        if (Database == null)
        {
            string reason = "AdsCallerConfig is missing";
            Logger.Warn($"[AdsCaller/Network] Rewarded ad did not appear. Reason: {reason}.");
            onResult?.Invoke(new AdShowOutcome(AdShowResult.ProviderUnavailable, OutAdFormat.Rewarded, AdsProviderKind.Online, reason));
            return;
        }
        if (!AdsRuntimeGate.NetworkAdsAllowed)
        {
            string reason = $"network gate blocked ads: {AdsRuntimeGate.Reason}";
            Logger.Warn($"[AdsCaller/Network] Rewarded ad did not appear. Reason: {reason}.");
            onResult?.Invoke(new AdShowOutcome(AdShowResult.Disabled, OutAdFormat.Rewarded, AdsProviderKind.Online, reason));
            return;
        }
        if (MaxSdk.IsRewardedAdReady(Database.RewardedAdUnitId))
        {
            if (!AdsShowSession.TryBegin(OutAdFormat.Rewarded, AdsProviderKind.Online, onSuccess, onResult, out AdsShowSession session)) return;
            rewardedSession = session;
            _pauseAppOpen = true;
            PrintLog("Showing rewarded ad...");
            try { MaxSdk.ShowRewardedAd(Database.RewardedAdUnitId); }
            catch (Exception exception)
            {
                rewardedSession = null;
                _pauseAppOpen = false;
                session.Complete(AdShowResult.Failed, exception.Message);
            }
            if (rewardedSession == session) _ = WatchRewardedAsync(session);
        }
        else
        {
            string reason = GetRewardedUnavailableReason();
            Logger.Warn($"[AdsCaller/Network] Rewarded ad did not appear. Reason: {reason}.");
            onResult?.Invoke(new AdShowOutcome(AdShowResult.NotReady, OutAdFormat.Rewarded, AdsProviderKind.Online, reason));
            LoadRewardedAd();
        }
    }

    private async Awaitable WatchRewardedAsync(AdsShowSession session)
    {
        await Awaitable.WaitForSecondsAsync(ShowStartTimeoutSeconds);
        if (this == null || rewardedSession != session || session.IsCompleted) return;
        if (!session.DidOpen)
        {
            rewardedSession = null;
            _pauseAppOpen = false;
            session.Complete(AdShowResult.TimedOut, "MAX rewarded ad did not report opening");
            LoadRewardedAd();
            return;
        }
        await Awaitable.WaitForSecondsAsync(FullscreenWatchdogSeconds - ShowStartTimeoutSeconds);
        if (this != null && rewardedSession == session && !session.IsCompleted)
        {
            rewardedSession = null;
            _pauseAppOpen = false;
            session.Complete(AdShowResult.TimedOut, "MAX rewarded ad did not report closing");
            LoadRewardedAd();
        }
    }
    #endregion

    #region BANNER
    private void InitializeBannerAds()
    {
        if (!Database.canShowBanner) return;
        MaxSdkCallbacks.Banner.OnAdLoadedEvent += (adUnitId, adInfo) => NotifyAvailability(OutAdFormat.Banner);
        MaxSdkCallbacks.Banner.OnAdLoadFailedEvent += (adUnitId, errorInfo) =>
            Logger.Warn($"[AdsCaller/Network] Banner did not appear because its load failed. {DescribeError(errorInfo, adUnitId)}");
        var pos = new MaxSdk.AdViewConfiguration(Database.defaultBannerPosition);
        MaxSdk.CreateBanner(Database.BannerAdUnitId, pos);
        MaxSdk.SetBannerBackgroundColor(Database.BannerAdUnitId, Color.black);
    }

    public void ShowBanner() { if (!AreAdsRemoved() && Database.canShowBanner) { isShowingBanner = true; MaxSdk.ShowBanner(Database.BannerAdUnitId); } }
    public void HideBanner() { isShowingBanner = false; MaxSdk.HideBanner(Database.BannerAdUnitId); }

    private async Awaitable ShowBannerWithDelayAsync(float delay)
    {
        await Awaitable.WaitForSecondsAsync(delay);
        if (this != null) ShowBanner();
    }
    #endregion

    #region LARGE BANNER (MREC)
    private void InitializeLargeBannerAds()
    {
        if (!Database.canShowLargeBanner) return;
        var pos = new MaxSdk.AdViewConfiguration(Database.defaultLargeBannerPosition);
        MaxSdk.CreateMRec(Database.LargeBannerAdUnitId, pos);

        MaxSdkCallbacks.MRec.OnAdLoadedEvent += OnLargeBannerLoadedEvent;
        MaxSdkCallbacks.MRec.OnAdLoadFailedEvent += OnLargeBannerLoadFailedEvent;
        MaxSdkCallbacks.MRec.OnAdClickedEvent += OnLargeBannerClickedEvent;
        MaxSdkCallbacks.MRec.OnAdRevenuePaidEvent += OnLargeBannerRevenuePaidEvent;
        MaxSdkCallbacks.MRec.OnAdExpandedEvent += OnLargeBannerExpandedEvent;
        MaxSdkCallbacks.MRec.OnAdCollapsedEvent += OnLargeBannerCollapsedEvent;
    }

    private void OnLargeBannerLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo) => Debug.Log("MREC loaded successfully.");
    private void OnLargeBannerLoadFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo) => Logger.Warn($"[AdsCaller/Network] MREC did not appear because its load failed. {DescribeError(errorInfo, adUnitId)}");
    private void OnLargeBannerClickedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo) => Debug.Log("MREC clicked.");
    private void OnLargeBannerRevenuePaidEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
    {
        Debug.Log("MRec ad revenue paid");
        TrackMaxRevenue(adInfo);
    }
    private void OnLargeBannerExpandedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo) => Debug.Log("MREC expanded.");
    private void OnLargeBannerCollapsedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo) => Debug.Log("MREC collapsed.");

    public void ShowLargeBanner() { if (!AreAdsRemoved() && Database.canShowLargeBanner) MaxSdk.ShowMRec(Database.LargeBannerAdUnitId); }
    public void HideLargeBanner() => MaxSdk.HideMRec(Database.LargeBannerAdUnitId);
    #endregion

    #region App Open (AdMob Strategy)
#if USE_ADMOB
    private void LoadAppOpen()
    {
        if (appOpenAd != null) { appOpenAd.Destroy(); appOpenAd = null; }
        isAppOpenLoading = true;
        AppOpenAd.Load(Database.AppOpenAdUnitId, new AdRequest(), (ad, error) =>
        {
            if (error != null || ad == null) { isAppOpenLoading = false; return; }
            appOpenAd = ad;
            appOpenExpireTime = DateTime.Now + APP_OPEN_TIMEOUT;
            isAppOpenLoading = false;

            ad.OnAdPaid += (adValue) => TrackAdMobRevenue(adValue, "AppOpen");
            ad.OnAdFullScreenContentOpened += () => appOpenSession?.Open();
            ad.OnAdFullScreenContentClosed += () => FinishAppOpen(AdShowResult.Closed, "app-open ad closed");
            ad.OnAdFullScreenContentFailed += error => FinishAppOpen(AdShowResult.Failed, error.ToString());

            OnAppOpenLoaded();
        });
    }

    private void OnAppOpenLoaded()
    {
        if (isFirstAdShown || userEnteredGameplay) return;

        bool shouldShow = (Database.appOpenStrategy == EnumsJar.AppOpenStrategy.StartAndResume || Database.appOpenStrategy == EnumsJar.AppOpenStrategy.Standard);
        if (Database.appOpenStrategy == EnumsJar.AppOpenStrategy.StartAndResume && PlayerPrefs.GetInt("UserEnteredGameplay", 0) <= 1) shouldShow = false;

        if (shouldShow)
        {
            isFirstAdShown = true;
            if (Database.canShowAppOpen) ShowAppOpen();
        }
    }

    public void ShowAppOpen()
    {
        if (AreAdsRemoved() || isShowingAppOpenAd || appOpenAd == null || !appOpenAd.CanShowAd() || DateTime.Now >= appOpenExpireTime) return;
        if (!AdsShowSession.TryBegin(OutAdFormat.AppOpen, AdsProviderKind.Online, null, null, out AdsShowSession session)) return;

        appOpenSession = session;
        bannerActiveStatus = isShowingBanner;
        HideBanner();
        isShowingAppOpenAd = true;
        try { appOpenAd.Show(); _ = WatchAppOpenAsync(session); }
        catch (Exception exception) { FinishAppOpen(AdShowResult.Failed, exception.Message); }
    }

    private void FinishAppOpen(AdShowResult result, string reason)
    {
        AdsShowSession session = appOpenSession;
        appOpenSession = null;
        isShowingAppOpenAd = false;
        session?.Complete(result, reason);
        CheckAndRequestAppOpen();
        if (bannerActiveStatus) ShowBanner();
    }

    private async Awaitable WatchAppOpenAsync(AdsShowSession session)
    {
        await Awaitable.WaitForSecondsAsync(ShowStartTimeoutSeconds);
        if (this == null || appOpenSession != session || session.IsCompleted) return;
        if (!session.DidOpen)
        {
            FinishAppOpen(AdShowResult.TimedOut, "AdMob app-open ad did not report opening");
            return;
        }
        await Awaitable.WaitForSecondsAsync(FullscreenWatchdogSeconds - ShowStartTimeoutSeconds);
        if (this != null && appOpenSession == session && !session.IsCompleted)
            FinishAppOpen(AdShowResult.TimedOut, "AdMob app-open ad did not report closing");
    }

    private void CheckAndRequestAppOpen()
    {
        if (!isShowingAppOpenAd && !isAppOpenLoading && (appOpenAd == null || !appOpenAd.CanShowAd()))
            LoadAppOpen();
    }
#else
    public void ShowAppOpen() { PrintLog("ShowAppOpen called, but USE_ADMOB is disabled."); }
#endif

    private void OnApplicationPause(bool isPaused)
    {
        if (!isPaused)
        {
#if USE_ADMOB
            if (Time.time - lastAppOpenCheckTime < Database.AppOpenCooldown) return;
            lastAppOpenCheckTime = Time.time;

            if (!_pauseAppOpen && appOpenAd != null && appOpenAd.CanShowAd())
            {
                if (Database.canShowAppOpen) ShowAppOpen();
            }
            else
            {
                CheckAndRequestAppOpen();
            }
#endif
        }
    }
    #endregion

    #region Analytics & Revenue Tracking
    private void TrackMaxRevenue(MaxSdkBase.AdInfo adInfo)
    {
        AdsAnalytics.LogRevenue(new AdRevenueInfo(
            adInfo.NetworkName,
            "max",
            adInfo.AdUnitIdentifier,
            adInfo.AdFormat,
            adInfo.Revenue));
    }

#if USE_ADMOB
    private void TrackAdMobRevenue(AdValue adValue, string adType)
    {
        AdsAnalytics.LogEvent($"AdMob {adType} Revenue: {adValue.Value}", AdsAnalyticsDestination.SolarEngine);
    }
#endif
    #endregion

    #region Utilities
    public bool AreAdsRemoved() => PlayerPrefs.GetInt("RemoveAds", 0) == 1;
    private void PrintLog(string msg) { if (Database != null && Database.isTestLog) Logger.Note(msg); }

    public AdAvailability GetAvailability(OutAdFormat format)
    {
        if (Database == null) return new AdAvailability(format, AdAvailabilityState.ProviderUnavailable, "AdsCallerConfig is missing");
        if (!AdsRuntimeGate.NetworkAdsAllowed) return new AdAvailability(format, AdAvailabilityState.Disabled, AdsRuntimeGate.Reason);
        if (!isMaxInitialized) return new AdAvailability(format, AdAvailabilityState.Initializing, "MAX SDK has not initialized");
        if (format == OutAdFormat.Interstitial)
        {
            if (!Database.canShowInterstitial) return new AdAvailability(format, AdAvailabilityState.Disabled, "disabled in AdsCallerConfig");
            if (MaxSdk.IsInterstitialReady(Database.InterstitialAdUnitId)) return new AdAvailability(format, AdAvailabilityState.Ready);
            return new AdAvailability(format, isInterstitialLoading ? AdAvailabilityState.Loading : AdAvailabilityState.NotReady, GetInterstitialUnavailableReason());
        }
        if (format == OutAdFormat.Rewarded)
        {
            if (MaxSdk.IsRewardedAdReady(Database.RewardedAdUnitId)) return new AdAvailability(format, AdAvailabilityState.Ready);
            return new AdAvailability(format, isRewardedLoading ? AdAvailabilityState.Loading : AdAvailabilityState.NotReady, GetRewardedUnavailableReason());
        }
        if (format == OutAdFormat.Banner) return new AdAvailability(format, Database.canShowBanner ? AdAvailabilityState.Ready : AdAvailabilityState.Disabled, Database.canShowBanner ? null : "disabled in AdsCallerConfig");
        if (format == OutAdFormat.LargeBanner) return new AdAvailability(format, Database.canShowLargeBanner ? AdAvailabilityState.Ready : AdAvailabilityState.Disabled, Database.canShowLargeBanner ? null : "disabled in AdsCallerConfig");
        return new AdAvailability(format, AdAvailabilityState.NotReady, "format is not supplied by MAX in this provider configuration");
    }

    private void HandleRuntimeGateChanged()
    {
        NotifyAllAvailability();
        if (AdsRuntimeGate.NetworkAdsAllowed && !isMaxInitialized && Database != null) _ = StartInitializingAdsAsync();
    }

    private void NotifyAvailability(OutAdFormat format) => AvailabilityChanged?.Invoke(GetAvailability(format));
    private void NotifyAllAvailability()
    {
        NotifyAvailability(OutAdFormat.Banner); NotifyAvailability(OutAdFormat.LargeBanner);
        NotifyAvailability(OutAdFormat.Interstitial); NotifyAvailability(OutAdFormat.Rewarded);
    }

    private void FailInterstitial(Action<AdShowOutcome> callback, AdShowResult result, string reason)
    {
        Logger.Warn($"[AdsCaller/Network] Interstitial did not appear. Reason: {reason}.");
        callback?.Invoke(new AdShowOutcome(result, OutAdFormat.Interstitial, AdsProviderKind.Online, reason));
    }

    private static string DescribeError(MaxSdkBase.ErrorInfo error, string adUnitId = null)
    {
        if (error == null) return "MAX returned no error details";

        string classification;
        switch (error.Code)
        {
            case MaxSdkBase.ErrorCode.NoFill:
                classification = "NO FILL (ad unit accepted) - no mediated network had an eligible ad";
                break;
            case MaxSdkBase.ErrorCode.InvalidAdUnitId:
                classification = "INVALID AD UNIT ID - check that it exists, is enabled, matches this app package, and is at least 30-60 minutes old";
                break;
            case MaxSdkBase.ErrorCode.AdLoadFailed:
                classification = "MEDIATED LOAD FAILURE - eligible ads existed, but every network load failed";
                break;
            case MaxSdkBase.ErrorCode.NoNetwork:
                classification = "NO NETWORK - the device is offline";
                break;
            case MaxSdkBase.ErrorCode.NetworkTimeout:
                classification = "NETWORK TIMEOUT - the ad request timed out";
                break;
            case MaxSdkBase.ErrorCode.NetworkError:
                classification = "NETWORK ERROR - the ad request failed at the network layer";
                break;
            default:
                classification = "MAX ERROR - inspect the code and message";
                break;
        }

        string adUnit = string.IsNullOrEmpty(adUnitId) ? string.Empty : $", adUnit={adUnitId}";
        return $"{classification}; code={error.Code}{adUnit}, message={error.Message}, mediatedCode={error.MediatedNetworkErrorCode}, mediatedMessage={error.MediatedNetworkErrorMessage}, loadInfo={error.AdLoadFailureInfo}, latencyMs={error.LatencyMillis}";
    }

    private static string LogNetworkFailure(OutAdFormat format, string phase, string adUnitId, MaxSdkBase.ErrorInfo error, double retryDelay)
    {
        string details = DescribeError(error, adUnitId);
        Logger.Warn($"[AdsCaller/Network] {format} {phase} failed; the ad will not appear now. {details}. Retrying in {retryDelay:0}s.");
        AdsAnalytics.LogLifecycle(new AdsLifecycleInfo(AdsLifecycleStage.LoadFailed, format, AdsProviderKind.Online, details));
        return details;
    }

    private string GetInterstitialUnavailableReason()
    {
        if (string.IsNullOrEmpty(lastInterstitialLoadFailure))
            return isInterstitialLoading ? "MAX is loading the interstitial" : "MAX has no ready interstitial; no load failure has been captured yet";

        return isInterstitialLoading
            ? $"MAX is retrying the interstitial; last load: {lastInterstitialLoadFailure}"
            : lastInterstitialLoadFailure;
    }

    private string GetRewardedUnavailableReason()
    {
        if (string.IsNullOrEmpty(lastRewardedLoadFailure))
            return isRewardedLoading ? "MAX is loading the rewarded ad" : "MAX has no ready rewarded ad; no load failure has been captured yet";

        return isRewardedLoading
            ? $"MAX is retrying the rewarded ad; last load: {lastRewardedLoadFailure}"
            : lastRewardedLoadFailure;
    }

    private async Awaitable LoadInterstitialWithDelayAsync(float delay)
    {
        await Awaitable.WaitForSecondsAsync(delay);
        if (this != null) LoadInterstitial();
    }

    private async Awaitable LoadRewardedAdWithDelayAsync(float delay)
    {
        await Awaitable.WaitForSecondsAsync(delay);
        if (this != null) LoadRewardedAd();
    }

#if USE_ADMOB
    private async Awaitable CheckAndRequestAppOpenWithDelayAsync(float delay)
    {
        await Awaitable.WaitForSecondsAsync(delay);
        if (this != null) CheckAndRequestAppOpen();
    }
#endif
    #endregion
}
