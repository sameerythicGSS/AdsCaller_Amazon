using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Identifies an optional ad provider without requiring the core assembly to reference its concrete type.
/// </summary>
public enum AdsProviderKind
{
    Online = 0,
    CrossPromo = 1
}

/// <summary>
/// Runtime registry shared by the mediator and independently installed provider modules.
/// </summary>
public static class AdsProviderRegistry
{
    private static readonly Dictionary<AdsProviderKind, IOutAdsProvider> Providers =
        new Dictionary<AdsProviderKind, IOutAdsProvider>();

    public static event Action ProvidersChanged;

    public static void Register(AdsProviderKind kind, IOutAdsProvider provider)
    {
        if (provider == null) return;

        if (Providers.TryGetValue(kind, out IOutAdsProvider current) && ReferenceEquals(current, provider))
            return;

        Providers[kind] = provider;
        ProvidersChanged?.Invoke();
    }

    public static void Unregister(AdsProviderKind kind, IOutAdsProvider provider)
    {
        if (!Providers.TryGetValue(kind, out IOutAdsProvider current) || !ReferenceEquals(current, provider))
            return;

        Providers.Remove(kind);
        ProvidersChanged?.Invoke();
    }

    public static bool TryGet(AdsProviderKind kind, out IOutAdsProvider provider)
    {
        if (Providers.TryGetValue(kind, out provider) && IsAlive(provider))
            return true;

        Providers.Remove(kind);
        provider = null;
        return false;
    }

    private static bool IsAlive(IOutAdsProvider provider)
    {
        if (provider == null) return false;
        return !(provider is UnityEngine.Object unityObject) || unityObject != null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        Providers.Clear();
        ProvidersChanged = null;
    }
}
