using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Provider-neutral facade with hybrid routing, readiness-aware fallback and fullscreen coordination.</summary>
public class AdsMediator : MonoBehaviour
{
    private static AdsMediator _instance;
    public static AdsMediator Instance
    {
        get
        {
            if (_instance == null)
            {
                Logger.Warn("[AdsMediator] Missing in scene. Spawning a no-ad ghost.");
                GameObject ghost = new GameObject("AdsMediator_Ghost");
                _instance = ghost.AddComponent<AdsMediator>();
                _instance.isAutoCreatedGhost = true;
                _instance.adsMode = EnumsJar_Salon.AdsMode.None;
            }
            return _instance;
        }
    }

    [Header("UI References")] public Text textStatus;
    [Header("Legacy Configuration")]
    public EnumsJar_Salon.AdsMode adsMode = EnumsJar_Salon.AdsMode.Online;
    public bool testAds;
    public bool bypassRewardedAd;
    public bool useAdsCallerRewarded = true;

    [Header("Hybrid Routing")]
    [Tooltip("Disabled preserves adsMode/useAdsCallerRewarded routing.")]
    public bool useAdvancedRouting;
    public AdsProviderKind bannerProvider = AdsProviderKind.CrossPromo;
    public AdsProviderKind largeBannerProvider = AdsProviderKind.CrossPromo;
    public AdsProviderKind interstitialProvider = AdsProviderKind.Online;
    public AdsProviderKind rewardedProvider = AdsProviderKind.Online;
    public bool allowProviderFallback = true;

    [Header("Shared Pacing")]
    [Min(0f)] public float startupInterstitialGraceSeconds;
    [Min(0f)] public float minimumFullscreenIntervalSeconds;
    [Min(0)] public int maximumInterstitialsPerSession;
    [Space] public string removeAdsPlayerPrefKey = "RemoveAds";

    private IOutAdsProvider activeProvider;
    private int backPressCount;
    private int interstitialsThisSession;
    private float lastFullscreenTime = -Mathf.Infinity;
    private float sessionStartTime;
    private bool isAutoCreatedGhost;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            if (_instance.isAutoCreatedGhost)
            {
                AdsMediator ghost = _instance;
                _instance = this;
                Destroy(ghost.gameObject);
            }
            else { Destroy(gameObject); return; }
        }
        _instance = this;
        sessionStartTime = Time.realtimeSinceStartup;
        DontDestroyOnLoad(gameObject);
        AdsProviderRegistry.ProvidersChanged += InitializeProviders;
    }

    private void Start() => InitializeProviders();

    private void OnDestroy()
    {
        AdsProviderRegistry.ProvidersChanged -= InitializeProviders;
        if (_instance != this) return;
        _instance = null;
    }

    private void InitializeProviders()
    {
        activeProvider = null;
        if (adsMode == EnumsJar_Salon.AdsMode.None) return;
        AdsProviderKind primary = adsMode == EnumsJar_Salon.AdsMode.CrossPromo ? AdsProviderKind.CrossPromo : AdsProviderKind.Online;
        TryGetProvider(primary, out activeProvider, out _);
        if (activeProvider == null && allowProviderFallback) TryGetProvider(Other(primary), out activeProvider, out _);
        if (activeProvider == null)
            Logger.Warn($"[AdsMediator] No ad provider is registered. Mode={adsMode}. The provider prefab may be absent, disabled, or not imported.");
    }

    public void ShowInterstitial()
    {
        if (adsMode == EnumsJar_Salon.AdsMode.None) return;
        if (IsAdsRemoved()) { LogUnavailable(OutAdFormat.Interstitial, "ads were removed by the user"); return; }
        if (Time.realtimeSinceStartup - sessionStartTime < startupInterstitialGraceSeconds)
        { LogUnavailable(OutAdFormat.Interstitial, $"startup grace period is active ({startupInterstitialGraceSeconds:0.#}s)"); return; }
        if (maximumInterstitialsPerSession > 0 && interstitialsThisSession >= maximumInterstitialsPerSession)
        { LogUnavailable(OutAdFormat.Interstitial, $"session cap reached ({maximumInterstitialsPerSession})"); return; }
        if (Time.realtimeSinceStartup - lastFullscreenTime < minimumFullscreenIntervalSeconds)
        {
            float remaining = minimumFullscreenIntervalSeconds - (Time.realtimeSinceStartup - lastFullscreenTime);
            LogUnavailable(OutAdFormat.Interstitial, $"shared fullscreen cooldown has {remaining:0.0}s remaining");
            return;
        }

        AdsProviderKind primary = GetPrimaryKind(OutAdFormat.Interstitial);
        AdsAnalytics.LogLifecycle(new AdsLifecycleInfo(AdsLifecycleStage.Requested, OutAdFormat.Interstitial, primary));
        TryShowInterstitial(primary, false);
    }

    private void TryShowInterstitial(AdsProviderKind kind, bool isFallback)
    {
        if (!TryGetReadyProvider(kind, OutAdFormat.Interstitial, out IOutAdsProvider provider, out string reason))
        {
            if (!isFallback && allowProviderFallback) { LogFallback(OutAdFormat.Interstitial, kind, reason); TryShowInterstitial(Other(kind), true); }
            else LogUnavailable(OutAdFormat.Interstitial, $"{kind} provider unavailable: {reason}");
            return;
        }
        if (!TryReserveProvider(provider, kind, OutAdFormat.Interstitial, out int lease, out reason))
        { LogUnavailable(OutAdFormat.Interstitial, reason); return; }

        if (provider is IResultOutAdsProvider resultProvider)
        {
            bool finished = false;
            Action<AdShowOutcome> finish = outcome =>
            {
                if (finished || outcome.Result == AdShowResult.Started) return;
                finished = true;
                AdsFullscreenCoordinator.Release(lease);
                if (outcome.Result == AdShowResult.Closed || outcome.Result == AdShowResult.Completed)
                {
                    lastFullscreenTime = Time.realtimeSinceStartup;
                    interstitialsThisSession++;
                    AdsAnalytics.LogLifecycle(new AdsLifecycleInfo(AdsLifecycleStage.Closed, OutAdFormat.Interstitial, kind, outcome.Reason));
                }
                else if (!isFallback && allowProviderFallback && CanFallback(outcome.Result))
                { LogFallback(OutAdFormat.Interstitial, kind, outcome.Reason); TryShowInterstitial(Other(kind), true); }
                else LogUnavailable(OutAdFormat.Interstitial, outcome.ToString());
            };
            try { resultProvider.ShowInterstitial(finish); }
            catch (Exception e) { finish(new AdShowOutcome(AdShowResult.Failed, OutAdFormat.Interstitial, kind, e.Message)); }
        }
        else
        {
            Logger.Warn($"[AdsMediator] {kind} uses the legacy interstitial API; completion locking cannot be guaranteed.");
            try { provider.ShowInterstitial(); } finally { AdsFullscreenCoordinator.Release(lease); }
        }
    }

    public void ShowRewardedAd(Action success, Action failure = null)
    {
        if (bypassRewardedAd) { Logger.Note("[AdsMediator] Reward bypass enabled."); RewardedSuccess(success); return; }
        if (adsMode == EnumsJar_Salon.AdsMode.None) { RewardedFailure(failure); return; }
        TryShowRewarded(GetPrimaryKind(OutAdFormat.Rewarded), false, success, failure);
    }

    private void TryShowRewarded(AdsProviderKind kind, bool isFallback, Action success, Action failure, IOutAdsProvider feedbackProvider = null)
    {
        if (feedbackProvider == null) AdsProviderRegistry.TryGet(kind, out feedbackProvider);
        if (!TryGetReadyProvider(kind, OutAdFormat.Rewarded, out IOutAdsProvider provider, out string reason))
        {
            if (!isFallback && allowProviderFallback) { LogFallback(OutAdFormat.Rewarded, kind, reason); TryShowRewarded(Other(kind), true, success, failure, feedbackProvider); }
            else { LogUnavailable(OutAdFormat.Rewarded, $"{kind} provider unavailable: {reason}"); RewardedFailure(failure, feedbackProvider ?? provider); }
            return;
        }
        if (!TryReserveProvider(provider, kind, OutAdFormat.Rewarded, out int lease, out reason))
        { LogUnavailable(OutAdFormat.Rewarded, reason); RewardedFailure(failure, feedbackProvider ?? provider); return; }

        bool granted = false;
        bool finished = false;
        Action grantOnce = () => { if (granted || finished) return; granted = true; RewardedSuccess(success); };
        if (provider is IResultOutAdsProvider resultProvider)
        {
            Action<AdShowOutcome> finish = outcome =>
            {
                if (finished || outcome.Result == AdShowResult.Started) return;
                finished = true;
                AdsFullscreenCoordinator.Release(lease);
                if (outcome.DidOpen) lastFullscreenTime = Time.realtimeSinceStartup;
                if (granted) { }
                else if (!isFallback && allowProviderFallback && CanFallback(outcome.Result))
                { LogFallback(OutAdFormat.Rewarded, kind, outcome.Reason); TryShowRewarded(Other(kind), true, success, failure, feedbackProvider); }
                else { LogUnavailable(OutAdFormat.Rewarded, outcome.ToString()); RewardedFailure(failure, feedbackProvider ?? provider, AdNotAvailablePopup.IsUnavailable(outcome)); }
            };
            try { using (AdNotAvailablePopup.DeferProviderFeedback()) resultProvider.ShowRewardedAd(grantOnce, finish); }
            catch (Exception e) { finish(new AdShowOutcome(AdShowResult.Failed, OutAdFormat.Rewarded, kind, e.Message)); }
        }
        else using (AdNotAvailablePopup.DeferProviderFeedback()) provider.ShowRewardedAd(
            () => { if (finished) return; grantOnce(); finished = true; AdsFullscreenCoordinator.Release(lease); },
            () => { if (finished) return; finished = true; AdsFullscreenCoordinator.Release(lease); RewardedFailure(failure, feedbackProvider ?? provider); });
    }

    public void ShowBanner() => ShowNonFullscreen(OutAdFormat.Banner, null);
    public void ShowBanner(EnumsJar_Salon.AdPosition position) => ShowNonFullscreen(OutAdFormat.Banner, position);
    public void ShowLargeBanner() => ShowNonFullscreen(OutAdFormat.LargeBanner, null);
    public void ShowLargeBanner(EnumsJar_Salon.AdPosition position) => ShowNonFullscreen(OutAdFormat.LargeBanner, position);

    private void ShowNonFullscreen(OutAdFormat format, EnumsJar_Salon.AdPosition? position)
    {
        if (adsMode == EnumsJar_Salon.AdsMode.None || IsAdsRemoved()) return;
        AdsProviderKind primary = GetPrimaryKind(format);
        if (!TryGetReadyProvider(primary, format, out IOutAdsProvider provider, out string reason))
        {
            string primaryReason = reason;
            if (!allowProviderFallback || !TryGetReadyProvider(Other(primary), format, out provider, out reason))
            { LogUnavailable(format, reason); return; }
            LogFallback(format, primary, primaryReason);
        }

        // Hide the previous provider before transferring banner ownership.
        HideFromBoth(p => { if (!ReferenceEquals(p, provider)) { if (format == OutAdFormat.Banner) p.HideBanner(); else p.HideLargeBanner(); } });
        if (format == OutAdFormat.Banner)
        {
            if (position.HasValue && provider is IPositionedOutAdsProvider positioned) positioned.ShowBanner(position.Value);
            else provider.ShowBanner();
        }
        else
        {
            if (position.HasValue && provider is IPositionedOutAdsProvider positioned) positioned.ShowLargeBanner(position.Value);
            else provider.ShowLargeBanner();
        }
    }

    public void HideBanner() => HideFromBoth(p => p.HideBanner());
    public void HideLargeBanner() => HideFromBoth(p => p.HideLargeBanner());
    public void HideAllBanners() => HideFromBoth(p => { p.HideBanner(); p.HideLargeBanner(); });

    public void SwitchBanners(bool largeBanner = false)
    {
        if (largeBanner) { HideBanner(); ShowLargeBanner(); }
        else { HideLargeBanner(); ShowBanner(); }
    }

    private static void HideFromBoth(Action<IOutAdsProvider> action)
    {
        if (AdsProviderRegistry.TryGet(AdsProviderKind.Online, out IOutAdsProvider online)) action(online);
        if (AdsProviderRegistry.TryGet(AdsProviderKind.CrossPromo, out IOutAdsProvider cross)) action(cross);
    }

    private static bool TryReserveProvider(IOutAdsProvider provider, AdsProviderKind kind, OutAdFormat format, out int lease, out string reason)
    {
        lease = 0; reason = string.Empty;
        if (provider is IManagedFullscreenProvider) return true;
        return AdsFullscreenCoordinator.TryAcquire(kind.ToString(), format, out lease, out reason);
    }

    private static bool TryGetReadyProvider(AdsProviderKind kind, OutAdFormat format, out IOutAdsProvider provider, out string reason)
    {
        if (!TryGetProvider(kind, out provider, out reason)) return false;
        if (provider is IAdsAvailabilityProvider availability)
        {
            AdAvailability status = availability.GetAvailability(format);
            if (!status.CanShow) { reason = $"state={status.State}; {status.Reason}"; return false; }
        }
        return true;
    }

    private static bool TryGetProvider(AdsProviderKind kind, out IOutAdsProvider provider, out string reason)
    {
        if (kind == AdsProviderKind.Online && !AdsRuntimeGate.NetworkAdsAllowed)
        { provider = null; reason = $"blocked by consent/runtime gate: {AdsRuntimeGate.Reason}"; return false; }
        if (!AdsProviderRegistry.TryGet(kind, out provider))
        { reason = "provider is not registered; prefab missing, disabled, or package absent"; return false; }
        reason = string.Empty;
        return true;
    }

    private AdsProviderKind GetPrimaryKind(OutAdFormat format)
    {
        if (useAdvancedRouting)
        {
            if (format == OutAdFormat.Banner) return bannerProvider;
            if (format == OutAdFormat.LargeBanner) return largeBannerProvider;
            if (format == OutAdFormat.Rewarded) return rewardedProvider;
            return interstitialProvider;
        }
        if (format == OutAdFormat.Rewarded && adsMode == EnumsJar_Salon.AdsMode.CrossPromo && useAdsCallerRewarded)
            return AdsProviderKind.Online;
        return adsMode == EnumsJar_Salon.AdsMode.CrossPromo ? AdsProviderKind.CrossPromo : AdsProviderKind.Online;
    }

    private static AdsProviderKind Other(AdsProviderKind kind) => kind == AdsProviderKind.Online ? AdsProviderKind.CrossPromo : AdsProviderKind.Online;
    private static bool CanFallback(AdShowResult result) => result == AdShowResult.NotReady || result == AdShowResult.Failed || result == AdShowResult.ProviderUnavailable;

    private static void LogFallback(OutAdFormat format, AdsProviderKind from, string reason)
    {
        Logger.Warn($"[AdsMediator] {from} {format} unavailable ({reason}). Trying fallback {Other(from)}.");
        AdsAnalytics.LogLifecycle(new AdsLifecycleInfo(AdsLifecycleStage.Fallback, format, from, reason));
    }

    private static void LogUnavailable(OutAdFormat format, string reason) =>
        Logger.Warn($"[AdsMediator] {format} did not appear. Reason: {reason}");

    private void RewardedSuccess(Action callback) { if (textStatus) textStatus.text = "Reward Granted"; AdsShowSession.InvokeSafely(callback); }
    private void RewardedFailure(Action callback, IOutAdsProvider provider = null, bool unavailable = true)
    {
        if (textStatus) textStatus.text = "Ad not available";
        if (unavailable && provider is IRewardedUnavailableFeedback feedback) feedback.ShowRewardedUnavailablePopup();
        AdsShowSession.InvokeSafely(callback);
    }

    public static bool IsAdsRemoved() => PlayerPrefs.GetInt(_instance != null ? _instance.removeAdsPlayerPrefKey : "RemoveAds", 0) == 1;
    public static void RemoveAds() { PlayerPrefs.SetInt(_instance != null ? _instance.removeAdsPlayerPrefKey : "RemoveAds", 1); PlayerPrefs.Save(); HideFromBoth(p => { p.HideBanner(); p.HideLargeBanner(); }); }

    public void BackBtnAd() { if (++backPressCount < 3) return; backPressCount = 0; ShowInterstitial(); }

    [ContextMenu("Log Ads Diagnostics")]
    public void LogDiagnostics()
    {
        Logger.Note($"[AdsDiagnostics] mode={adsMode}, advanced={useAdvancedRouting}, lock={AdsFullscreenCoordinator.ActiveDescription}, removed={IsAdsRemoved()}, networkGate={AdsRuntimeGate.NetworkAdsAllowed} ({AdsRuntimeGate.Reason}), online=[{Describe(AdsProviderKind.Online)}], crossPromo=[{Describe(AdsProviderKind.CrossPromo)}]");
    }

    private static string Describe(AdsProviderKind kind)
    {
        if (!AdsProviderRegistry.TryGet(kind, out IOutAdsProvider provider)) return "absent";
        if (!(provider is IAdsAvailabilityProvider status)) return "registered/legacy-status";
        return $"initialized={status.IsInitialized}, inter={status.GetAvailability(OutAdFormat.Interstitial).State}, rewarded={status.GetAvailability(OutAdFormat.Rewarded).State}";
    }
}

