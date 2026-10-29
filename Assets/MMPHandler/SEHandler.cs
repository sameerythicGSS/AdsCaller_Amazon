using UnityEngine;

/// <summary>
/// Optional SolarEngine analytics adapter. The ads systems remain functional when it is not installed in a scene.
/// </summary>
public class SEHandler : MonoBehaviour
{
    private static SEHandler _instance;

    public static SEHandler Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindFirstObjectByType<SEHandler>();

            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        AdsAnalytics.EventLogged += HandleAnalyticsEvent;
        AdsAnalytics.RevenueLogged += HandleRevenue;
    }

    private void OnDisable()
    {
        AdsAnalytics.EventLogged -= HandleAnalyticsEvent;
        AdsAnalytics.RevenueLogged -= HandleRevenue;
    }

    private void OnDestroy()
    {
        AdsAnalytics.EventLogged -= HandleAnalyticsEvent;
        AdsAnalytics.RevenueLogged -= HandleRevenue;
        if (_instance == this) _instance = null;
    }

    private void HandleAnalyticsEvent(string eventName, AdsAnalyticsDestination destination)
    {
        if ((destination & AdsAnalyticsDestination.SolarEngine) != 0)
            LogEvent(eventName);
    }

    private void HandleRevenue(AdRevenueInfo revenueInfo)
    {
        Debug.Log(
            $"SEHandler: Mock Logging {revenueInfo.MediationSource.ToUpperInvariant()} Ad Revenue: " +
            $"{revenueInfo.NetworkName}, {revenueInfo.AdFormat}, {revenueInfo.Revenue} {revenueInfo.Currency}");
    }

    public void LogEvent(string eventName)
    {
        Debug.Log($"SEHandler: Logging SE Event: {eventName}");
    }
}
