using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Shared scene authoring commands. Optional provider types are discovered without assembly references.</summary>
public static class AdsCallerHierarchyMenu
{
    private const string Root = "GameObject/Ads Caller/";
    private const string PopPath = "Assets/AdsCaller/SalonAds/Prefabs/AdBodies/PopAd.prefab";
    private static GameObject Target(MenuCommand command) => command?.context as GameObject ?? Selection.activeGameObject;
    private static bool SceneTarget(MenuCommand command)
    {
        var target = Target(command);
        return target != null && !EditorUtility.IsPersistent(target) && target.scene.IsValid() && target.scene.isLoaded
            && Selection.gameObjects.Length <= 1;
    }
    private static bool CanEdit(MenuCommand command) => !EditorApplication.isPlayingOrWillChangePlaymode && SceneTarget(command);
    private static IEnumerable<MonoBehaviour> Components(Scene scene) => AdsCallerAssets.Components(scene);
    private static AdsMediator Mediator(Scene scene) => Components(scene).OfType<AdsMediator>().FirstOrDefault()
        ?? (Application.isPlaying && AdsMediator.TryGetInstance(out var live) ? live : null);

    [MenuItem(Root + "CrossPromo/Add PopAd as Child", false, 10)]
    private static void AddPop(MenuCommand command)
    {
        try { AddPopAd(Target(command)); }
        catch (Exception e) { EditorUtility.DisplayDialog("Ads Caller — Add PopAd", e.Message, "OK"); }
    }
    [MenuItem(Root + "CrossPromo/Add PopAd as Child", true)]
    private static bool CanAddPop(MenuCommand command) => CanEdit(command) && AdsCallerProviderManager.HasCrossPromo
        && AdsCallerAssets.FindType("SalonPopAd") != null && AdsCallerAssets.Load<GameObject>(PopPath) != null;

    public static GameObject AddPopAd(GameObject parent)
    {
        if (parent == null || EditorUtility.IsPersistent(parent) || !parent.scene.IsValid() || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Select a scene GameObject in Edit Mode.");
        if (parent.GetComponentInParent<Canvas>(true) == null)
            throw new InvalidOperationException("Select a UI object inside a Canvas. PopAd will be added directly under that object.");
        var prefab = AdsCallerAssets.Load<GameObject>(PopPath);
        var type = AdsCallerAssets.FindType("SalonPopAd");
        if (!AdsCallerProviderManager.HasCrossPromo || prefab == null || type == null)
            throw new InvalidOperationException("Install the matching Ads Caller CrossPromo module to add a PopAd.");
        if (prefab.GetComponentsInChildren<Transform>(true).Any(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
            || prefab.GetComponentsInChildren(type, true).Length != 1 || !(prefab.transform is RectTransform))
            throw new InvalidOperationException("PopAd prefab is incomplete. Resolve missing scripts and references before adding it.");
        var used = new HashSet<int>(Components(parent.scene).Where(c => c.GetType() == type)
            .Select(c => new SerializedObject(c).FindProperty("popAdSlotID").intValue));
        int slot = 0; while (used.Contains(slot)) slot++;
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Add Ads Caller PopAd");
        GameObject instance = null;
        try
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
            Undo.RegisterCreatedObjectUndo(instance, "Add Ads Caller PopAd");
            instance.name = GameObjectUtility.GetUniqueNameForSibling(parent.transform, prefab.name);
            var rect = (RectTransform)instance.transform;
            rect.localRotation = Quaternion.identity; rect.localScale = Vector3.one; rect.anchoredPosition3D = Vector3.zero;
            var component = instance.GetComponentInChildren(type, true);
            var so = new SerializedObject(component); so.FindProperty("popAdSlotID").intValue = slot; so.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            EditorSceneManager.MarkSceneDirty(parent.scene);
            Selection.activeGameObject = instance;
            Undo.CollapseUndoOperations(group);
            return instance;
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    [MenuItem(Root + "Add Panel Ad Behaviour", false, 20)]
    private static void AddPanel(MenuCommand command)
    {
        var target = Target(command);
        var panel = target.GetComponent<AdsPanelBehaviour>() ?? Undo.AddComponent<AdsPanelBehaviour>(target);
        Selection.activeGameObject = target; EditorGUIUtility.PingObject(panel);
    }
    [MenuItem(Root + "Add Panel Ad Behaviour", true)] private static bool CanAddPanel(MenuCommand command) => CanEdit(command);

    [MenuItem(Root + "Bind Button/Show Interstitial", false, 30)] private static void Interstitial(MenuCommand c) => Bind(c, AdsButtonAction.Action.ShowInterstitial);
    [MenuItem(Root + "Bind Button/Show Rewarded Ad…", false, 31)] private static void Rewarded(MenuCommand c) => Bind(c, AdsButtonAction.Action.ShowRewarded);
    [MenuItem(Root + "Bind Button/Show Banner", false, 32)] private static void ShowBanner(MenuCommand c) => Bind(c, AdsButtonAction.Action.ShowBanner);
    [MenuItem(Root + "Bind Button/Hide Banner", false, 33)] private static void HideBanner(MenuCommand c) => Bind(c, AdsButtonAction.Action.HideBanner);
    [MenuItem(Root + "Bind Button/Show Large Banner", false, 34)] private static void ShowLarge(MenuCommand c) => Bind(c, AdsButtonAction.Action.ShowLargeBanner);
    [MenuItem(Root + "Bind Button/Hide Large Banner", false, 35)] private static void HideLarge(MenuCommand c) => Bind(c, AdsButtonAction.Action.HideLargeBanner);
    [MenuItem(Root + "Bind Button/Hide All Banners", false, 36)] private static void HideAll(MenuCommand c) => Bind(c, AdsButtonAction.Action.HideAllBanners);
    [MenuItem(Root + "Bind Button/Show Interstitial", true)]
    [MenuItem(Root + "Bind Button/Show Rewarded Ad…", true)]
    [MenuItem(Root + "Bind Button/Show Banner", true)]
    [MenuItem(Root + "Bind Button/Hide Banner", true)]
    [MenuItem(Root + "Bind Button/Show Large Banner", true)]
    [MenuItem(Root + "Bind Button/Hide Large Banner", true)]
    [MenuItem(Root + "Bind Button/Hide All Banners", true)]
    private static bool CanBind(MenuCommand command) => CanEdit(command) && Target(command).GetComponent<Button>() != null;
    private static void Bind(MenuCommand command, AdsButtonAction.Action action)
    {
        var bridge = BindButton(Target(command).GetComponent<Button>(), action);
        Selection.activeGameObject = bridge.gameObject; EditorGUIUtility.PingObject(bridge);
        if (action == AdsButtonAction.Action.ShowRewarded)
            EditorUtility.DisplayDialog("Ads Caller — Rewarded Button", "In Ads Button Action on the selected object, wire On Reward Earned to grant the reward and On Reward Failed to handle cancellation or unavailability. Existing Button On Click listeners are preserved; remove any old listener that grants rewards directly.", "OK");
    }
    public static AdsButtonAction BindButton(Button button, AdsButtonAction.Action action)
    {
        if (button == null || EditorUtility.IsPersistent(button) || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Select a scene UI Button in Edit Mode.");
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Bind Ads Caller Button");
        var bridge = button.GetComponent<AdsButtonAction>() ?? Undo.AddComponent<AdsButtonAction>(button.gameObject);
        Undo.RecordObject(bridge, "Configure ad action"); bridge.action = action;
        Undo.RecordObject(button, "Bind ad action");
        var bindings = Enumerable.Range(0, button.onClick.GetPersistentEventCount())
            .Where(i => button.onClick.GetPersistentTarget(i) == bridge && button.onClick.GetPersistentMethodName(i) == nameof(AdsButtonAction.Execute)).ToArray();
        // Rebinding changes the action without adding duplicate requests or removing unrelated listeners.
        if (bindings.Length == 0) UnityEventTools.AddPersistentListener(button.onClick, bridge.Execute);
        else
        {
            button.onClick.SetPersistentListenerState(bindings[0], UnityEngine.Events.UnityEventCallState.RuntimeOnly);
            for (int i = bindings.Length - 1; i > 0; i--) UnityEventTools.RemovePersistentListener(button.onClick, bindings[i]);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(bridge); PrefabUtility.RecordPrefabInstancePropertyModifications(button);
        EditorUtility.SetDirty(bridge); EditorUtility.SetDirty(button);
        EditorSceneManager.MarkSceneDirty(button.gameObject.scene); Undo.CollapseUndoOperations(group);
        return bridge;
    }

    [MenuItem(Root + "Select Ads Manager", false, 50)]
    private static void SelectManager(MenuCommand c) { Selection.activeGameObject = Mediator(Target(c).scene).gameObject; }
    [MenuItem(Root + "Select Ads Manager", true)]
    private static bool CanSelectManager(MenuCommand c) => SceneTarget(c) && Mediator(Target(c).scene) != null;
    private static UnityEngine.Object[] Configurations(GameObject root)
    {
        var components = root.GetComponentsInChildren<MonoBehaviour>(true).Concat(root.GetComponentsInParent<MonoBehaviour>(true)).Where(c => c != null).Distinct();
        var configs = new HashSet<UnityEngine.Object>();
        foreach (var component in components)
        {
            string type = component.GetType().Name;
            if (type != "AdsCaller" && type != "AdsCallerSalon" && type != "CatalogLoader") continue;
            var prop = new SerializedObject(component).GetIterator();
            while (prop.Next(true))
                if (prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue is ScriptableObject)
                    configs.Add(prop.objectReferenceValue);
        }
        return configs.ToArray();
    }
    [MenuItem(Root + "Open Assigned Configuration", false, 51)]
    private static void OpenConfig(MenuCommand c) { Selection.objects = Configurations(Target(c)); if (Selection.activeObject != null) EditorGUIUtility.PingObject(Selection.activeObject); }
    [MenuItem(Root + "Open Assigned Configuration", true)]
    private static bool CanOpenConfig(MenuCommand c) => SceneTarget(c) && Configurations(Target(c)).Length > 0;

    [MenuItem(Root + "Validate Selected Hierarchy", false, 60)]
    private static void Validate(MenuCommand c)
    {
        var root = Target(c); var issues = ValidateHierarchy(root);
        string result = issues.Count == 0 ? "No authoring issues found." : string.Join("\n", issues);
        Debug.Log("[Ads Caller] " + root.name + " — " + result, root);
        EditorUtility.DisplayDialog("Ads Caller — Hierarchy Validation", result, "OK");
    }
    [MenuItem(Root + "Validate Selected Hierarchy", true)] private static bool CanValidate(MenuCommand c) => SceneTarget(c);
    public static List<string> ValidateHierarchy(GameObject root)
    {
        var issues = new List<string>();
        if (root == null) { issues.Add("Select a scene GameObject."); return issues; }
        var all = Components(root.scene).ToArray();
        var selected = root.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null).ToArray();
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0) issues.Add(transform.name + ": missing script.");
        var mediators = all.OfType<AdsMediator>().ToArray();
        if (mediators.Length == 0) issues.Add("No AdsMediator in this scene. It must be present in a loaded initialization scene at runtime.");
        if (mediators.Length > 1) issues.Add("Multiple AdsMediator components in this scene.");
        foreach (string name in new[] { "AdsCaller", "AdsCallerSalon", "CatalogLoader" })
            if (all.Count(c => c.GetType().Name == name) > 1) issues.Add("Multiple " + name + " components in this scene.");
        if (!AdsCallerProviderManager.HasOnline && !AdsCallerProviderManager.HasCrossPromo) issues.Add("Core only: install an Android, Amazon, or CrossPromo provider before running ads.");
        foreach (var component in selected)
        {
            string name = component.GetType().Name;
            if (name == "AdsCaller" || name == "AdsCallerSalon")
            {
                var config = new SerializedObject(component).FindProperty("config");
                if (config == null || config.objectReferenceValue == null) issues.Add(component.name + ": missing provider configuration.");
            }
            if (name == "SalonPopAd")
            {
                var so = new SerializedObject(component); int id = so.FindProperty("popAdSlotID").intValue;
                if (all.Count(c => c.GetType().Name == name && new SerializedObject(c).FindProperty("popAdSlotID").intValue == id) > 1)
                    issues.Add(component.name + ": duplicate PopAd slot ID " + id + ".");
                foreach (string field in new[] { "popImage", "popButton" })
                    if (so.FindProperty(field)?.objectReferenceValue == null) issues.Add(component.name + ": missing " + field + ".");
                if (component.GetComponentInParent<Canvas>(true) == null) issues.Add(component.name + ": PopAd needs a Canvas ancestor.");
                if (!all.Any(c => c.GetType().Name == "AdsCallerSalon")) issues.Add("PopAd requires AdsCallerSalon in a loaded initialization scene at runtime.");
            }
            if (component is AdsButtonAction bridge && bridge.action == AdsButtonAction.Action.ShowRewarded && bridge.onRewardEarned.GetPersistentEventCount() == 0)
                issues.Add(bridge.name + ": rewarded action has no On Reward Earned listener.");
        }
        return issues.Distinct().ToList();
    }

    [MenuItem(Root + "Setup Ads Caller in Scene…", false, 70)] private static void Setup() => AdsCallerSetup.ShowWindow();
    [MenuItem(Root + "Setup Ads Caller in Scene…", true)] private static bool CanSetup() => !EditorApplication.isPlayingOrWillChangePlaymode;
    [MenuItem(Root + "Play Mode/Log Diagnostics", false, 80)] private static void Diagnostics(MenuCommand c) => Mediator(Target(c).scene).LogDiagnostics();
    [MenuItem(Root + "Play Mode/Log Diagnostics", true)]
    private static bool CanDiagnose(MenuCommand c) => EditorApplication.isPlaying && SceneTarget(c) && Mediator(Target(c).scene) != null;
}
