using System;
using System.Collections;
using UnityEngine;

/// <summary>Shared, non-blocking feedback for an unavailable rewarded request.</summary>
public sealed class AdNotAvailablePopup : MonoBehaviour
{
    private static AdNotAvailablePopup instance;
    private static int deferredProviderFeedback;
    private Coroutine lifetime;

    public static void Show(bool enabled, GameObject prefab)
    {
        if (!enabled) return;
        if (instance == null)
        {
            if (prefab == null) prefab = Resources.Load<GameObject>("AdsCallerUI/AdNotAvailable");
            if (prefab == null) { Logger.Warn("[Ads Caller] AdNotAvailable prefab is missing."); return; }
            var popup = Instantiate(prefab);
            instance = popup.GetComponent<AdNotAvailablePopup>();
            if (instance == null) { Destroy(popup); Logger.Warn("[Ads Caller] Popup prefab needs AdNotAvailablePopup."); return; }
            popup.SetActive(true);
            DontDestroyOnLoad(popup);
        }
        instance.RestartLifetime();
    }

    private void OnEnable() => RestartLifetime();
    private void RestartLifetime()
    {
        if (lifetime != null) StopCoroutine(lifetime);
        lifetime = StartCoroutine(CloseAfterDelay());
    }
    private IEnumerator CloseAfterDelay()
    {
        yield return new WaitForSecondsRealtime(2f);
        Destroy(gameObject);
    }
    private void OnDestroy() { if (instance == this) instance = null; }

    public static bool IsUnavailable(AdShowOutcome outcome) => outcome.Format == OutAdFormat.Rewarded &&
        !outcome.DidOpen && outcome.Result != AdShowResult.Removed && outcome.Result != AdShowResult.Capped;

    // Capture routing at request time so delayed failures also wait for the mediator's fallback.
    public static Action<AdShowOutcome> WithFeedback(Action<AdShowOutcome> callback, Action showFeedback)
    {
        bool deferred = deferredProviderFeedback > 0;
        return outcome =>
        {
            if (!deferred && IsUnavailable(outcome)) showFeedback();
            AdsShowSession.InvokeSafely(() => callback?.Invoke(outcome));
        };
    }

    public static IDisposable DeferProviderFeedback() => new FeedbackDeferral();
    private sealed class FeedbackDeferral : IDisposable
    {
        private bool disposed;
        public FeedbackDeferral() { deferredProviderFeedback++; }
        public void Dispose() { if (disposed) return; disposed = true; deferredProviderFeedback--; }
    }
}

public interface IRewardedUnavailableFeedback
{
    void ShowRewardedUnavailablePopup();
}
