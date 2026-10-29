using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Compact modal with a separate review step for provider removal.</summary>
public sealed class AdsCallerProviderWindow : EditorWindow
{
    private AdsCallerProviderRemoval.Plan plan;
    private Vector2 scroll;
    private string error;
    private bool remembered;
    private GUIStyle heading, body, eyebrow, cardTitle, cardBody, button, quiet;
    private static readonly Color Background = new Color32(26, 36, 48, 255);
    private static readonly Color Surface = new Color32(39, 53, 69, 255);
    private static readonly Color Accent = new Color32(111, 183, 230, 255);
    private static readonly Color Text = new Color32(233, 240, 247, 255);
    private static readonly Color Muted = new Color32(175, 193, 209, 255);

    [MenuItem("Ads Caller/Manage Providers", false, 20)]
    public static void Open()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Ads Caller", "Exit Play Mode before managing providers.", "OK"); return; }
        var existing = Resources.FindObjectsOfTypeAll<AdsCallerProviderWindow>().FirstOrDefault();
        if (existing != null) { existing.Focus(); return; }
        var window = CreateInstance<AdsCallerProviderWindow>();
        window.titleContent = new GUIContent("Ads Caller · Providers");
        window.minSize = new Vector2(570, 640);
        window.maxSize = new Vector2(740, 820);
        window.position = new Rect(180, 140, 600, 660);
        window.ShowModalUtility();
    }

    private void Styles()
    {
        if (heading != null) return;
        heading = new GUIStyle(EditorStyles.label) { fontSize = 25, fontStyle = FontStyle.Bold, wordWrap = true };
        body = new GUIStyle(EditorStyles.label) { fontSize = 12, wordWrap = true };
        eyebrow = new GUIStyle(body) { fontSize = 10, fontStyle = FontStyle.Bold };
        cardTitle = new GUIStyle(body) { fontSize = 15, fontStyle = FontStyle.Bold };
        cardBody = new GUIStyle(body) { fontSize = 11 };
        button = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold, fixedHeight = 34 };
        quiet = new GUIStyle(body) { alignment = TextAnchor.MiddleCenter, fixedHeight = 30 };
        heading.normal.textColor = cardTitle.normal.textColor = Text;
        body.normal.textColor = cardBody.normal.textColor = Muted;
        eyebrow.normal.textColor = quiet.normal.textColor = Accent;
    }

    private void OnGUI()
    {
        Styles();
        EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), Background);
        EditorGUI.DrawRect(new Rect(0, 0, position.width, 3), Accent);
        GUILayout.BeginVertical();
        GUILayout.Space(24);
        GUILayout.BeginHorizontal(); GUILayout.Space(26);
        GUILayout.BeginVertical();
        GUILayout.Label("ADS CALLER  /  PROVIDERS", eyebrow);
        GUILayout.Space(8);
        GUILayout.Label(plan == null ? "Your ads. Your choice." : "Review your changes", heading);
        GUILayout.Space(8);
        if (plan == null) Choices(); else Review();
        if (!string.IsNullOrEmpty(error)) { GUILayout.Space(8); EditorGUILayout.HelpBox(error, MessageType.Error); }
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(plan == null ? "Decide later" : "Back to providers", quiet))
        {
            if (plan != null) { plan = null; error = null; }
            else { AdsCallerProviderManager.Remember("Deferred"); remembered = true; Close(); GUIUtility.ExitGUI(); }
        }
        GUILayout.Space(16);
        GUILayout.EndVertical(); GUILayout.Space(26); GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    private void Choices()
    {
        bool online = AdsCallerProviderManager.HasOnline, cross = AdsCallerProviderManager.HasCrossPromo;
        GUILayout.Label(online && cross ? "Two ad providers are installed. Choose what belongs in this project."
            : online ? "Online ads are installed. Cross Promo is not installed."
            : cross ? "Cross Promo is installed. Online ads are not installed." : "No ad providers are installed yet.", body);
        GUILayout.Space(20);
        if (!(online && cross))
        {
            Card(online ? "Online ads" : cross ? "Cross Promo" : "Ready when you are",
                online || cross ? "This provider stays installed. Import the other provider to use hybrid ads." : "Import an Ads Caller provider package to get started.",
                "Done", true, () => { AdsCallerProviderManager.Remember("Reviewed"); remembered = true; Close(); GUIUtility.ExitGUI(); }, true);
            return;
        }
        Card("Keep both", "Use hybrid ads. Keep your current configuration and both providers.", "Keep both · Hybrid", online && cross, () =>
        { AdsCallerProviderManager.KeepBoth(); remembered = true; Close(); GUIUtility.ExitGUI(); }, true);
        Card("Online ads only", "Remove Cross Promo and its provider assets.", "Review Cross Promo removal", online && cross, () => Preview(false));
        Card("Cross Promo only", "Remove Online ads and the SDK dependencies used only by it.", "Review Online removal", online && cross, () => Preview(true));
        GUILayout.Space(8);
        GUILayout.Label("Available anytime from Ads Caller > Manage Providers.", cardBody);
    }

    private void Card(string title, string description, string action, bool enabled, Action clicked, bool primary = false)
    {
        var rect = GUILayoutUtility.GetRect(0, 110, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, Surface);
        if (primary) EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3, rect.height), Accent);
        GUI.Label(new Rect(rect.x + 16, rect.y + 11, rect.width - 32, 23), title, cardTitle);
        GUI.Label(new Rect(rect.x + 16, rect.y + 36, rect.width - 32, 30), description, cardBody);
        using (new EditorGUI.DisabledScope(!enabled || EditorApplication.isCompiling || EditorApplication.isUpdating))
        {
            var previous = GUI.backgroundColor;
            GUI.backgroundColor = primary ? Accent : new Color32(103, 128, 151, 255);
            bool pressed = GUI.Button(new Rect(rect.x + 16, rect.y + 70, rect.width - 32, 30), action, button);
            GUI.backgroundColor = previous;
            if (pressed) clicked();
        }
        GUILayout.Space(10);
    }

    private void Preview(bool online)
    {
        try { plan = AdsCallerProviderRemoval.Preview(online); error = null; scroll = Vector2.zero; }
        catch (Exception e) { error = e.GetBaseException().Message; }
        GUIUtility.ExitGUI();
    }

    private void Review()
    {
        GUILayout.Label(plan.removeOnline ? "Cross Promo stays. Online ads will be removed." : "Online ads stay. Cross Promo will be removed.", body);
        GUILayout.Space(12);
        GUILayout.Label("A recovery backup is saved before any files change.", cardBody);
        GUILayout.Space(10);
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(200));
        Section("REMOVE", plan.assets.Concat(plan.packages.Select(p => "Package: " + p)).ToArray());
        Section("UPDATE SCENES & PREFABS", plan.reconcile.ToArray());
        Section("KEEP · SHARED DEPENDENCIES", plan.retained.ToArray());
        if (plan.blockers.Count > 0)
        {
            GUILayout.Space(8);
            GUILayout.Label("Resolve these references first", cardTitle);
            foreach (var issue in plan.blockers) EditorGUILayout.HelpBox(issue, MessageType.Warning);
        }
        EditorGUILayout.EndScrollView();
        GUILayout.Space(12);
        using (new EditorGUI.DisabledScope(!plan.CanApply || EditorApplication.isCompiling || EditorApplication.isUpdating))
        {
            if (GUILayout.Button(plan.CanApply ? "Remove provider & apply" : "Resolve references to continue", button))
            {
                try
                {
                    string backup = AdsCallerProviderRemoval.Apply(plan);
                    if (backup != null) { remembered = true; Close(); GUIUtility.ExitGUI(); }
                }
                catch (ExitGUIException) { throw; }
                catch (Exception e) { error = e.GetBaseException().Message; Debug.LogException(e); }
            }
        }
    }

    private void Section(string title, string[] items)
    {
        if (items.Length == 0) return;
        GUILayout.Space(8); GUILayout.Label(title, eyebrow); GUILayout.Space(4);
        foreach (var item in items) GUILayout.Label(item, cardBody);
    }

    private void OnDisable()
    {
        // Closing the native modal has the same non-destructive behavior as deciding later.
        if (!remembered && !EditorApplication.isCompiling) AdsCallerProviderManager.Remember("Deferred");
        EditorApplication.delayCall += AdsCallerSetup.ShowIfNeeded;
    }
}
