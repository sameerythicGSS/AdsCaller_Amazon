using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PanelAdTreatment : MonoBehaviour
{
    public EnumsJar.ToDo treatBanner;
    public EnumsJar.ToDo treatLargeBanner;

    public float delay = 1f;

    [Header("Independent Disable Treatments")]
    public bool invertBannerOnDisable;
    public bool invertLargeBannerOnDisable;

    private void OnEnable()
    {
        _ = TreatAds();
    }
    private void Start()
    {
        _ = TreatAds();
    }

    public async Awaitable TreatAds()
    {
        await Awaitable.WaitForSecondsAsync(delay);

        switch (treatBanner)
        {
            case EnumsJar.ToDo.Hide:
                AdsMediator.Instance.HideBanner();
                break;
            case EnumsJar.ToDo.Show:
                AdsMediator.Instance.ShowBanner();
                break;
        }

        switch (treatLargeBanner)
        {
            case EnumsJar.ToDo.Hide:
                AdsMediator.Instance.HideLargeBanner();
                break;
            case EnumsJar.ToDo.Show:
                AdsMediator.Instance.ShowLargeBanner();
                break;
        }
    }

    private void OnDisable()
    {
        if (invertBannerOnDisable)
        {
            switch (treatBanner)
            {
                case EnumsJar.ToDo.Hide:
                    AdsMediator.Instance.ShowBanner();
                    break;
                case EnumsJar.ToDo.Show:
                    AdsMediator.Instance.HideBanner();
                    break;
            }
        }

        if (invertLargeBannerOnDisable)
        {
            switch (treatLargeBanner)
            {
                case EnumsJar.ToDo.Hide:
                    AdsMediator.Instance.ShowLargeBanner();
                    break;
                case EnumsJar.ToDo.Show:
                    AdsMediator.Instance.HideLargeBanner();
                    break;
            }
        }
    }
}