using System;
using UnityEngine;

public static class AdsCoreApi
{
    public const int Version = 3;
}

public enum OutAdFormat
{
    Banner,
    LargeBanner,
    Interstitial,
    Rewarded,
    AppOpen
}

public enum AdAvailabilityState
{
    Disabled,
    Initializing,
    Loading,
    Ready,
    NotReady,
    ProviderUnavailable
}

public enum AdShowResult
{
    Completed,
    Closed,
    Started,
    NotReady,
    Removed,
    Disabled,
    Capped,
    Busy,
    Failed,
    TimedOut,
    ProviderUnavailable
}

public readonly struct AdAvailability
{
    public readonly OutAdFormat Format;
    public readonly AdAvailabilityState State;
    public readonly string Reason;

    public bool CanShow => State == AdAvailabilityState.Ready;

    public AdAvailability(OutAdFormat format, AdAvailabilityState state, string reason = null)
    {
        Format = format;
        State = state;
        Reason = reason ?? string.Empty;
    }
}

public readonly struct AdShowOutcome
{
    public readonly AdShowResult Result;
    public readonly OutAdFormat Format;
    public readonly AdsProviderKind Provider;
    public readonly string Reason;

    public bool DidOpen => Result == AdShowResult.Started || Result == AdShowResult.Completed || Result == AdShowResult.Closed;

    public AdShowOutcome(AdShowResult result, OutAdFormat format, AdsProviderKind provider, string reason = null)
    {
        Result = result;
        Format = format;
        Provider = provider;
        Reason = reason ?? string.Empty;
    }

    public override string ToString() => $"{Provider}/{Format}: {Result}. {Reason}";
}

public interface IOutAdsProvider
{
    bool IsBannerAdShowing { get; }
    void ShowInterstitial();

    void ShowBanner();
    void HideBanner();

    void ShowLargeBanner();
    void HideLargeBanner();

    void ShowRewardedAd(Action onSuccess, Action onFail = null);
}

/// <summary>
/// Optional capability implemented by providers that support choosing a banner position per request.
/// Keeping this separate lets providers without runtime positioning remain compatible with the core API.
/// </summary>
public interface IPositionedOutAdsProvider
{
    void ShowBanner(EnumsJar_Salon.AdPosition adPos);
    void ShowLargeBanner(EnumsJar_Salon.AdPosition adPos);
}

/// <summary>Optional provider capability. Core routing uses it without requiring every provider to implement it.</summary>
public interface IAdsAvailabilityProvider
{
    bool IsInitialized { get; }
    event Action<AdAvailability> AvailabilityChanged;
    AdAvailability GetAvailability(OutAdFormat format);
}

/// <summary>Optional result API used for reliable fallback and fullscreen locking.</summary>
public interface IResultOutAdsProvider
{
    void ShowInterstitial(Action<AdShowOutcome> onResult);
    void ShowRewardedAd(Action onSuccess, Action<AdShowOutcome> onResult);
}

/// <summary>Provider owns the shared lease, including when called directly instead of through the mediator.</summary>
public interface IManagedFullscreenProvider { }

/// <summary>One request, one reward and one terminal result. All calls are made on Unity's main thread.</summary>
public sealed class AdsShowSession
{
    private readonly Action reward;
    private readonly Action<AdShowOutcome> result;
    private int lease;
    public readonly OutAdFormat Format;
    public readonly AdsProviderKind Provider;
    public bool IsCompleted { get; private set; }
    public bool RewardEarned { get; private set; }
    public bool DidOpen { get; private set; }

    private AdsShowSession(int lease, OutAdFormat format, AdsProviderKind provider, Action reward, Action<AdShowOutcome> result)
    { this.lease = lease; Format = format; Provider = provider; this.reward = reward; this.result = result; }

    public static bool TryBegin(OutAdFormat format, AdsProviderKind provider, Action reward,
        Action<AdShowOutcome> result, out AdsShowSession session)
    {
        session = null;
        if (!AdsFullscreenCoordinator.TryAcquire(provider.ToString(), format, out int lease, out string reason))
        { InvokeSafely(() => result?.Invoke(new AdShowOutcome(AdShowResult.Busy, format, provider, reason))); return false; }
        session = new AdsShowSession(lease, format, provider, reward, result);
        return true;
    }

    public void Open() { if (!IsCompleted) DidOpen = true; }
    public void EarnReward()
    {
        if (IsCompleted || RewardEarned || Format != OutAdFormat.Rewarded) return;
        RewardEarned = true;
        InvokeSafely(reward);
    }

    public void Complete(AdShowResult outcome, string reason = null, bool releaseLease = true)
    {
        if (releaseLease) Release();
        if (IsCompleted) return;
        IsCompleted = true;
        if (RewardEarned) outcome = AdShowResult.Completed;
        InvokeSafely(() => result?.Invoke(new AdShowOutcome(outcome, Format, Provider, reason)));
    }

    // An uncertain native show may finish its caller while retaining the lease until a native close/failure arrives.
    public void Release() { AdsFullscreenCoordinator.Release(lease); lease = 0; }
    public static void InvokeSafely(Action callback)
    {
        try { callback?.Invoke(); }
        catch (Exception exception) { Debug.LogException(exception); }
    }
}

/// <summary>
/// Shared, provider-neutral fullscreen lock. It prevents Network and CrossPromo fullscreen ads from overlapping.
/// </summary>
public static class AdsFullscreenCoordinator
{
    private static int nextLease;
    private static int activeLease;
    private static string activeOwner;
    private static OutAdFormat activeFormat;

    public static bool IsBusy => activeLease != 0;
    public static string ActiveDescription => IsBusy ? $"{activeOwner}/{activeFormat}" : "Idle";

    public static bool TryAcquire(string owner, OutAdFormat format, out int lease, out string reason)
    {
        if (IsBusy)
        {
            lease = 0;
            reason = $"another fullscreen ad is active ({ActiveDescription})";
            return false;
        }

        activeLease = ++nextLease;
        if (activeLease == 0) activeLease = ++nextLease;
        activeOwner = string.IsNullOrWhiteSpace(owner) ? "UnknownProvider" : owner;
        activeFormat = format;
        lease = activeLease;
        reason = string.Empty;
        return true;
    }

    public static void Release(int lease)
    {
        if (lease == 0 || lease != activeLease) return;
        activeLease = 0;
        activeOwner = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        nextLease = 0;
        activeLease = 0;
        activeOwner = null;
    }
}

/// <summary>Consent/remote-config extension point. Network ads are allowed by default for backward compatibility.</summary>
public static class AdsRuntimeGate
{
    public static bool NetworkAdsAllowed { get; private set; } = true;
    public static string Reason { get; private set; } = "Allowed by default";
    public static event Action Changed;

    public static void SetNetworkAdsAllowed(bool allowed, string reason)
    {
        NetworkAdsAllowed = allowed;
        Reason = string.IsNullOrWhiteSpace(reason) ? (allowed ? "Allowed" : "Blocked") : reason;
        Changed?.Invoke();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        NetworkAdsAllowed = true;
        Reason = "Allowed by default";
        Changed = null;
    }
}
