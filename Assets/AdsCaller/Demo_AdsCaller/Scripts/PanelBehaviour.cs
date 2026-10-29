using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PanelBehaviour : MonoBehaviour
{
    [SerializeField]
    GameObject panel;

    [SerializeField]
    float panelWait = 3f;
    [SerializeField]
    float interstitialWait = 5f;

    [Space]
    public Button rewardButton;
    public GameObject reward;

    private void Start()
    {
        Invoke(nameof(TogglePanel), panelWait);
        Invoke(nameof(ShowInter), interstitialWait);

        rewardButton.GetComponent<Button>().onClick.AddListener(ShowRewarded);
    }

    void TogglePanel()
    {
        panel.SetActive(!panel.activeSelf);
        Invoke(nameof(TogglePanel), panelWait);
    }

    void ShowInter()
    {
        AdsMediator.Instance.ShowInterstitial();
    }

    void ShowRewarded()
    {
        AdsMediator.Instance.ShowRewardedAd(() =>{
            reward.SetActive(true);
        });
    }
}
