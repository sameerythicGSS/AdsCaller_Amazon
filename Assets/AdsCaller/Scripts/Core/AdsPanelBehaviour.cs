using System.Collections;
using UnityEngine;

/// <summary>Provider-neutral ad treatment for a panel's enable/disable lifecycle.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Ads Caller/Panel Ad Behaviour")]
public sealed class AdsPanelBehaviour : MonoBehaviour
{
    public enum Treatment { None, Show, Hide }
    public Treatment banner;
    public Treatment largeBanner;
    public bool revertBannerOnDisable;
    public bool revertLargeBannerOnDisable;
    public bool showInterstitial;
    [Min(0f)] public float delaySeconds = 0.5f;
    private Coroutine pending;
    private AdsMediator owner;
    private Treatment appliedBanner, appliedLargeBanner;

    private void OnEnable() { pending = StartCoroutine(Apply()); }
    private IEnumerator Apply()
    {
        // Let scene Awake/Start finish and keep working with late bootstrap scenes.
        yield return null;
        if (delaySeconds > 0f) yield return new WaitForSecondsRealtime(delaySeconds);
        while (!AdsMediator.TryGetInstance(out owner) || !owner.isActiveAndEnabled) yield return null;
        if (banner != Treatment.None) { Treat(owner, banner, false); appliedBanner = banner; }
        if (largeBanner != Treatment.None) { Treat(owner, largeBanner, true); appliedLargeBanner = largeBanner; }
        if (showInterstitial) owner.ShowInterstitial();
        pending = null;
    }
    private void OnDisable()
    {
        if (pending != null) StopCoroutine(pending);
        pending = null;
        if (owner != null && owner.isActiveAndEnabled)
        {
            if (revertBannerOnDisable) Treat(owner, Invert(appliedBanner), false);
            if (revertLargeBannerOnDisable) Treat(owner, Invert(appliedLargeBanner), true);
        }
        appliedBanner = appliedLargeBanner = Treatment.None;
        owner = null;
    }
    private static Treatment Invert(Treatment value) => value == Treatment.Show ? Treatment.Hide : value == Treatment.Hide ? Treatment.Show : Treatment.None;
    private static void Treat(AdsMediator mediator, Treatment value, bool large)
    {
        if (value == Treatment.Show) { if (large) mediator.ShowLargeBanner(); else mediator.ShowBanner(); }
        if (value == Treatment.Hide) { if (large) mediator.HideLargeBanner(); else mediator.HideBanner(); }
    }
}
