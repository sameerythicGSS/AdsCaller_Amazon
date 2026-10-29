using System;
using UnityEngine;

[Flags]
public enum AdsAnalyticsDestination
{
    None = 0,
    Firebase = 1,
    SolarEngine = 2,
    All = Firebase | SolarEngine
}

public readonly struct AdRevenueInfo
{
    public readonly string NetworkName;
    public readonly string MediationSource;
    public readonly string AdUnitId;
    public readonly string AdFormat;
    public readonly double Revenue;
    public readonly string Currency;

    public AdRevenueInfo(
        string networkName,
        string mediationSource,
        string adUnitId,
        string adFormat,
        double revenue,
        string currency = "USD")
    {
        NetworkName = networkName;
        MediationSource = mediationSource;
        AdUnitId = adUnitId;
        AdFormat = adFormat;
        Revenue = revenue;
        Currency = currency;
    }
}

public enum AdsLifecycleStage
{
    Requested,
    Loading,
    Loaded,
    LoadFailed,
    ShowStarted,
    ShowFailed,
    Closed,
    RewardEarned,
    Fallback
}

public readonly struct AdsLifecycleInfo
{
    public readonly AdsLifecycleStage Stage;
    public readonly OutAdFormat Format;
    public readonly AdsProviderKind Provider;
    public readonly string Reason;

    public AdsLifecycleInfo(AdsLifecycleStage stage, OutAdFormat format, AdsProviderKind provider, string reason = null)
    {
        Stage = stage;
        Format = format;
        Provider = provider;
        Reason = reason ?? string.Empty;
    }
}

/// <summary>
/// Provider-neutral analytics bus. Analytics integrations are optional subscribers.
/// </summary>
public static class AdsAnalytics
{
    public static event Action<string, AdsAnalyticsDestination> EventLogged;
    public static event Action<AdRevenueInfo> RevenueLogged;
    public static event Action<AdsLifecycleInfo> LifecycleLogged;

    public static void LogEvent(string eventName, AdsAnalyticsDestination destination = AdsAnalyticsDestination.All)
    {
        if (string.IsNullOrWhiteSpace(eventName) || destination == AdsAnalyticsDestination.None) return;
        EventLogged?.Invoke(eventName, destination);
    }

    public static void LogRevenue(AdRevenueInfo revenueInfo)
    {
        RevenueLogged?.Invoke(revenueInfo);
    }

    public static void LogLifecycle(AdsLifecycleInfo lifecycleInfo)
    {
        LifecycleLogged?.Invoke(lifecycleInfo);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        EventLogged = null;
        RevenueLogged = null;
        LifecycleLogged = null;
    }
}
