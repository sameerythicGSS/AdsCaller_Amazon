using UnityEngine;
using UnityEngine.Events;

/// <summary>Serializable UnityEvent bridge to AdsMediator, independent of any provider SDK.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Ads Caller/Button Ad Action")]
public sealed class AdsButtonAction : MonoBehaviour
{
    public enum Action { ShowInterstitial, ShowRewarded, ShowBanner, HideBanner, ShowLargeBanner, HideLargeBanner, HideAllBanners }
    public Action action;
    [Tooltip("Wire the actual reward here. Button clicks alone never grant a reward.")]
    public UnityEvent onRewardEarned = new UnityEvent();
    public UnityEvent onRewardFailed = new UnityEvent();

    public void Execute()
    {
        if (!AdsMediator.TryGetInstance(out var mediator) || !mediator.isActiveAndEnabled)
        {
            Debug.LogWarning("[Ads Caller] No active AdsMediator. Run Ads Caller > Setup Wizard in the initialization scene.", this);
            if (action == Action.ShowRewarded) onRewardFailed.Invoke();
            return;
        }
        switch (action)
        {
            case Action.ShowInterstitial: mediator.ShowInterstitial(); break;
            case Action.ShowRewarded:
                mediator.ShowRewardedAd(() => { if (this != null) onRewardEarned.Invoke(); }, () => { if (this != null) onRewardFailed.Invoke(); });
                break;
            case Action.ShowBanner: mediator.ShowBanner(); break;
            case Action.HideBanner: mediator.HideBanner(); break;
            case Action.ShowLargeBanner: mediator.ShowLargeBanner(); break;
            case Action.HideLargeBanner: mediator.HideLargeBanner(); break;
            case Action.HideAllBanners: mediator.HideAllBanners(); break;
        }
    }
}
