using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Shared serialized inspector diagnostics; no dependency on a specific Online provider.</summary>
public static class AdsCallerSdkStatus
{
    public struct Status
    {
        public string name;
        public bool installed, required, enabled;
        public string Label => installed ? "Installed" : required ? "Absent — required" : enabled ? "Absent — optional" : "Absent — not used";
    }

    public static IEnumerable<Status> Inspect(SerializedObject config)
    {
        bool bypass = Bool(config, "isAdsBypass");
        bool max = false, google = false;
        var formats = new[] { "Banner", "LargeBanner", "Interstitial", "Rewarded", "AppOpen" };
        var flags = new[] { "canShowBanner", "canShowLargeBanner", "canShowInterstitial", "isUsingRewardedAd", "isUsingAppOpenAd" };
        var sources = new[] { "bannerSource", "largeBannerSource", "interstitialSource", "rewardedSource", "appOpenSource" };
        for (int i = 0; i < formats.Length; i++)
        {
            if (!Bool(config, flags[i], config == null)) continue;
            var source = config?.FindProperty(sources[i]);
            string selected = source != null && source.propertyType == SerializedPropertyType.Enum ? source.enumValueIndex >= 0 && source.enumValueIndex < source.enumNames.Length ? source.enumNames[source.enumValueIndex] : "" : "";
            max |= selected == "MAX" || config == null || (source != null && source.hasMultipleDifferentValues);
            google |= selected == "AdMob" || config == null || (source != null && source.hasMultipleDifferentValues);
            if ((i == 2 && Bool(config, "allowInterstitialNetworkFallback")) || (i == 3 && Bool(config, "allowRewardedNetworkFallback")))
            {
                // A fallback is used only when it has an ID (matching AdsCallerConfig.Uses).
                max |= !string.IsNullOrWhiteSpace(config.FindProperty("Max" + formats[i] + "Id")?.stringValue);
                google |= HasGoogleId(config, formats[i]);
            }
        }
        max &= !bypass; google &= !bypass;
        bool ump = !bypass && (max || google) && Bool(config, "requestConsentOnInitialize", true);
        yield return Item("AppLovin MAX", "MaxSdk", max, max);
        yield return Item("Google Mobile Ads", "GoogleMobileAds.Api.MobileAds", google, google);
        yield return Item("Google UMP consent", "GoogleMobileAds.Ump.Api.ConsentInformation", ump, ump);
        yield return Item("External Dependency Manager", "GooglePlayServices.PlayServicesResolver", max || google, max || google);
        yield return Item("Firebase Analytics", "Firebase.Analytics.FirebaseAnalytics", false, Bool(config, "enableFirebase"));
        yield return Item("AppsFlyer revenue", "AppsFlyerSDK.AppsFlyerAdRevenue", false, Bool(config, "enableAppsFlyer"));
    }

    private static bool HasGoogleId(SerializedObject config, string format)
    {
        if (Bool(config, "useGoogleTestAds")) return true;
        string name = "AdMob" + format + "Id";
        var strategy = config.FindProperty("adMobStrategy");
        var suffixes = strategy != null && strategy.enumValueIndex != 0 ? new[] { "", "_Medium", "_Low" } : new[] { "" };
        foreach (string suffix in suffixes)
            if (!string.IsNullOrWhiteSpace(config.FindProperty(name + suffix)?.stringValue)) return true;
        return false;
    }
    private static bool Bool(SerializedObject config, string name, bool fallback = false)
    {
        var property = config?.FindProperty(name);
        return property == null ? fallback : property.hasMultipleDifferentValues || property.boolValue;
    }
    private static Status Item(string name, string type, bool required, bool enabled) =>
        new Status { name = name, installed = AdsCallerAssets.FindType(type) != null, required = required, enabled = enabled };

    public static void Draw(SerializedObject config)
    {
        EditorGUILayout.LabelField("Third-party SDKs", EditorStyles.boldLabel);
        foreach (var status in Inspect(config))
        {
            EditorGUILayout.LabelField(status.name, status.Label);
            if (status.required && !status.installed)
                EditorGUILayout.HelpBox(status.name + " is absent. Install the official SDK to use the selected Online configuration.", MessageType.Error);
            else if (status.enabled && !status.installed)
                EditorGUILayout.HelpBox(status.name + " is absent. Its enabled analytics integration will be skipped.", MessageType.Warning);
        }
    }
}
