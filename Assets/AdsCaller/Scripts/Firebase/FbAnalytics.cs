using System;
using System.Threading.Tasks;

using UnityEngine;
//using Firebase;
//using Firebase.Analytics;
//using Firebase.Extensions;

namespace GSS.AdsCaller
{
/// <summary>
/// This is a mock class for Firebase Analytics. It simulates the behavior of Firebase Analytics for testing purposes without requiring an actual Firebase setup.
/// </summary>
[UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "", sourceAssembly: "AdsCaller.Core", sourceClassName: "FbAnalytics")]
public class FbAnalytics : MonoBehaviour
{
    //DependencyStatus dependencyStatus = DependencyStatus.UnavailableOther;
    public bool firebaseInitialized = false;
    public bool IsLogEnabled = true;
    public static FbAnalytics Instance;

    public bool isFirebaseRemoteConfigInitialized = false;

    public static int AppOpenStrategy = -1; // -1 If not Configured, -> current settings
                                            // 0 is current Setting, -> current settings
                                            // 1 is to display on First Open too,
                                            // 2 is to not to display on anytime on first load and also not load initially as well


    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        AdsAnalytics.EventLogged += HandleAnalyticsEvent;
    }

    private void OnDisable()
    {
        AdsAnalytics.EventLogged -= HandleAnalyticsEvent;
    }

    private void OnDestroy()
    {
        AdsAnalytics.EventLogged -= HandleAnalyticsEvent;
        if (Instance == this) Instance = null;
    }

    private void HandleAnalyticsEvent(string eventName, AdsAnalyticsDestination destination)
    {
        if ((destination & AdsAnalyticsDestination.Firebase) != 0)
            LogEvent(eventName);
    }
    private void Start()
    {


        DontDestroyOnLoad(this);
    }

    public void DebugLog(string s)
    {
        if (IsLogEnabled)
            print(s);
    }

    /// <summary>
    /// any event detail of Analytics will pass through here
    /// </summary>
    /// <param name="info"></param>
    public void LogEvent(string info)
    {
        if (firebaseInitialized)
        {
            //FirebaseAnalytics.LogEvent(info);

            DebugLog("Firebase Mock Log Event: " + info);
        }
    }
}
}
