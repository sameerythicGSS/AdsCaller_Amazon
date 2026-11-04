using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AdsMediator)), CanEditMultipleObjects]
public sealed class AdsMediatorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        foreach (AdsMediator mediator in targets)
        {
            bool online = mediator.adsMode == EnumsJar_Salon.AdsMode.Online ||
                (mediator.useAdvancedRouting && new[] { mediator.bannerProvider, mediator.largeBannerProvider,
                    mediator.interstitialProvider, mediator.rewardedProvider }.Contains(AdsProviderKind.Online));
            if (!online) continue;
            var callerType = AdsCallerAssets.FindType("AdsCaller");
            Component caller = callerType == null ? null : mediator.gameObject.scene.IsValid()
                ? mediator.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren(callerType, true)).FirstOrDefault()
                : mediator.GetComponentInParent(callerType, true);
            if (caller == null)
                EditorGUILayout.HelpBox("Online is selected. Add an AdsCaller provider and assign its configuration to use Online ads.", MessageType.Warning);
            UnityEngine.Object config = (caller != null ? callerType.GetField("config")?.GetValue(caller) : null) as UnityEngine.Object;
            if (caller != null && config == null)
                EditorGUILayout.HelpBox("Online is selected, but AdsCaller has no configuration assigned.", MessageType.Warning);
            AdsCallerSdkStatus.Draw(config != null ? new SerializedObject(config) : null);
        }
    }
}
