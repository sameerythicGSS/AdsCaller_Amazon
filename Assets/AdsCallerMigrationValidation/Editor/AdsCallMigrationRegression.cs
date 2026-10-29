using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using E = AdsCallMigrationEngine;

public static class AdsCallMigrationRegression
{
    private static readonly List<string> passed = new List<string>();
    private static void Check(bool value, string name) { if (!value) throw new Exception("AD_CALL_MIGRATION_FAIL: " + name); passed.Add(name); }
    private static E.Source Source(string text, string path = "Assets/Game.cs", params string[] defines) => new E.Source
    { text = text, bytes = new UTF8Encoding(false).GetBytes(text), path = path, writable = true, tree = AdsMigrationRoslyn.Parse(text, path, defines) };
    private static E.Scan Scan(params E.Source[] sources)
    {
        var scan = new E.Scan();
        var mediator = Source("public class AdsMediator { public static AdsMediator Instance; public void ShowInterstitial() {} public void ShowRewardedAd(System.Action success, System.Action failure = null) {} public void ShowBanner() {} public void HideBanner() {} public void ShowLargeBanner() {} public void HideLargeBanner() {} public void HideAllBanners() {} public void BackBtnAd() {} public static void RemoveAds() {} }", "Mediator.cs");
        mediator.writable = false;
        E.Analyze(scan, sources.Concat(new[] { mediator }).ToArray(), new[] { typeof(object).Assembly.Location, typeof(Action).Assembly.Location });
        return scan;
    }
    private static E.Rule Interstitial(E.CallSite call) => new E.Rule { key = call.key, action = E.AdAction.Interstitial };
    public static void Run()
    {
        passed.Clear();
        const string definitions = @"
using System;
using Old = Moneylitica;
class Moneylitica {
 public static Moneylitica Instance;
 public void ShowIntertitial(object placeholder, object format, object extra) {}
 public void OddlyNamed(int x) {}
 public void OddlyNamed(string x) {}
 public void Prize(int placement, Action success, Action failure = null) {}
 public bool Available() { return true; }
 public void InternalCall() { Instance.ShowIntertitial(null, null, null); }
}
class PlainWrapper { public void Zzz() {} }
class Game {
 Moneylitica field;
 void Award() {} void Failure() {}
 void Run(Moneylitica variable, PlainWrapper plain) {
";
        const string ending = "\n} }";
        var source = Source(definitions + @"
// Moneylitica.Instance.ShowIntertitial(ignored, ignored, ignored);
string s = ""Moneylitica.Instance.ShowIntertitial(a, b, c)"";
Moneylitica.Instance.ShowIntertitial(
 new object[] { 1, 2 }, ""a,b)"", new Action(() => { Award(); }));
Old.Instance.ShowIntertitial(null, null, null);
variable.ShowIntertitial(1, 2, 3);
Moneylitica.Instance.OddlyNamed(1);
Moneylitica.Instance.OddlyNamed(""value"");
Moneylitica.Instance.Prize(1, Award, Failure);
Moneylitica.Instance.Prize(failure: Failure, success: Award, placement: 2);
if (variable.Available()) Award();
variable?.ShowIntertitial(1, 2, 3);
plain.Zzz();
#if OTHER_PLATFORM
Moneylitica.Instance.OddlyNamed(9);
#endif
" + ending);
        var scan = Scan(source);
        var money = scan.calls.Where(c => c.manager == "Moneylitica").ToArray();
        Check(money.Length == 9, "group aliases, typed variables and misspelled methods under one manager; ignore comments, strings, own implementation and inactive branches");
        Check(scan.calls.Any(c => c.manager == "PlainWrapper" && !c.likely), "discover arbitrary wrapper and method names for Show all");
        Check(scan.references.Any(r => r.manager == "Moneylitica"), "inventory non-call type references");
        Check(money.Where(c => c.method == "OddlyNamed").Select(c => c.key).Distinct().Count() == 2, "separate resolved overloads");
        Check(money.Count(c => !string.IsNullOrEmpty(c.blocked)) == 2, "block conditions and conditional-access invocations");
        var rules = money.Select(Interstitial).ToList();
        var plan = E.BuildPlan(scan, new HashSet<string> { "Moneylitica" }, rules);
        Check(plan.count == 7 && plan.files.Count == 1 && plan.skipped.Count == 2, "bulk replacement counts with manual cases");
        Check(plan.errors.Count == 0, "compile replacement preview before writing");
        Check(plan.files[0].after.Contains("global::AdsMediator.Instance.ShowInterstitial();") && !plan.files[0].after.Contains("new object[] { 1, 2 }"), "replace whole multiline invocation and discard arbitrary arguments");
        Check(plan.files[0].after.Contains("// Moneylitica.Instance.ShowIntertitial") && plan.files[0].after.Contains("string s = \"Moneylitica.Instance.ShowIntertitial"), "preserve comments and string contents byte-for-byte");
        Check(plan.files[0].after.Contains("variable?.ShowIntertitial") && plan.files[0].after.Contains("if (variable.Available())"), "leave blocked calls intact");
        var reward = money.First(c => c.method == "Prize");
        var rewardRule = new E.Rule { key = reward.key, action = E.AdAction.Rewarded, successArgument = 1, failureArgument = 2 };
        var rewardPlan = E.BuildPlan(scan, new HashSet<string> { "Moneylitica" }, new[] { rewardRule });
        Check(rewardPlan.count == 2 && rewardPlan.files[0].after.Split(new[] { "ShowRewardedAd(Award, Failure)" }, StringSplitOptions.None).Length == 3, "preserve callbacks by parameter identity with reordered named arguments");
        rewardRule.successArgument = 0;
        Check(E.BuildPlan(scan, new HashSet<string> { "Moneylitica" }, new[] { rewardRule }).count == 0, "block incompatible reward arguments");
        var missing = Scan(Source("class G { void Run() { Moneylitica.Instance.ShowIntertitial(a, b, c); } }"));
        Check(missing.calls.Count == 1 && !missing.calls[0].resolved && missing.calls[0].manager == "Moneylitica", "support explicit singleton calls after old wrapper was removed");
        Check(E.BuildPlan(missing, new HashSet<string> { "Moneylitica" }, new[] { Interstitial(missing.calls[0]) }).count == 1, "allow explicit mapping for unresolved manager");
        var qualified = Scan(Source("class G { void Run() { legacy.Moneylitica.Instance.ShowIntertitial(a, b, c); } }"));
        Check(qualified.calls.Single().manager == "legacy.Moneylitica", "discover unresolved managers in lowercase namespaces");
        var dependent = Scan(Source("class G { void Run() { Moneylitica.Instance.ShowIntertitial(out var result); System.Console.WriteLine(result); } } class Moneylitica { public static Moneylitica Instance; public void ShowIntertitial(out int result) { result = 0; } }"));
        var unsafePlan = E.BuildPlan(dependent, new HashSet<string> { "Moneylitica" }, dependent.calls.Select(Interstitial).ToArray());
        Check(unsafePlan.errors.Any(e => e.Contains("result")), "block replacements that discard declarations still used by gameplay");
        bool refused = false;
        try { E.Apply(unsafePlan); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "compiler errors prohibit apply before touching files");
        var active = Scan(Source(definitions + "\n#if OTHER_PLATFORM\nMoneylitica.Instance.OddlyNamed(9);\n#endif" + ending, "Assets/Other.cs", "OTHER_PLATFORM"));
        Check(active.calls.Count == 1, "honor supplied platform defines");
        var invalid = Scan(Source("class Bad { void Run( {"));
        Check(invalid.calls.Count == 0 && invalid.notes.Count == 1, "skip syntax-invalid files");
        Check(!E.Writable("Assets/AdsCaller/Scripts/Core/AdsMediator.cs") && !E.Writable("Assets/MaxSdk/X.cs") && !E.Writable("Packages/Foo.cs") && !E.Writable("Assets/Game/Editor/X.cs"), "exclude provider, SDK, package and editor implementation paths");
        Check(E.BuildPlan(scan, new HashSet<string>(), rules).count == 0, "unselected managers never change");
        var fifty = Scan(Source("class Fifty { void Run() {" + string.Concat(Enumerable.Range(0, 50).Select(i => "Moneylitica.Instance.ShowIntertitial(" + i + ", null, null);\n")) + "} }"));
        var fiftyPlan = E.BuildPlan(fifty, new HashSet<string> { "Moneylitica" }, new[] { Interstitial(fifty.calls[0]) });
        Check(fifty.calls.Select(c => c.manager).Distinct().Count() == 1 && fiftyPlan.count == 50 && fiftyPlan.errors.Count == 0, "one mapping replaces all 50 occurrences with different arguments");
        Check(!Scan(Source(fiftyPlan.files[0].after)).calls.Any(c => c.manager == "Moneylitica"), "rescan confirms migrated manager calls are gone");
        foreach (E.AdAction action in Enum.GetValues(typeof(E.AdAction)))
        {
            if (action == E.AdAction.Unmapped || action == E.AdAction.Rewarded) continue;
            var actionPlan = E.BuildPlan(fifty, new HashSet<string> { "Moneylitica" }, new[] { new E.Rule { key = fifty.calls[0].key, action = action } });
            Check(actionPlan.count == 50 && actionPlan.errors.Count == 0, "generated mediator API compiles: " + action);
        }
        var lambda = Scan(Source(definitions + "Moneylitica.Instance.Prize(1, () => { Award(); Failure(); }, () => Failure());" + ending));
        var lambdaRule = new E.Rule { key = lambda.calls.Single().key, action = E.AdAction.Rewarded, successArgument = 1, failureArgument = 2 };
        var lambdaPlan = E.BuildPlan(lambda, new HashSet<string> { "Moneylitica" }, new[] { lambdaRule });
        Check(lambdaPlan.count == 1 && lambdaPlan.errors.Count == 0 && lambdaPlan.files[0].after.Contains("() => { Award(); Failure(); }"), "preserve entire reward lambdas with nested statements");
        RewardAndDiscoveryRegressions();
        TransactionTests();
#if UNITY_EDITOR
        ProductionScanTest();
#endif
        string output = Path.Combine(E.ProjectRoot, "Validation", "AdCallMigration", "assertions.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(output)); File.WriteAllLines(output, passed);
        Debug.Log("AD_CALL_MIGRATION_PASS " + passed.Count + " — " + output);
    }

    private static void RewardAndDiscoveryRegressions()
    {
        const string gameplay = @"
class SaveController { public static void MarkAsSaveIsRequired() {} }
class Upgrade { public void UpgradeStage() {} }
class Zone { public void UnlockNurse() {} }
class Panel { public void RedrawPanel() {} }
class SecretaryOffice { public Panel GetActivePanel() { return new Panel(); } }
class UIController { public static T GetPage<T>() where T : new() { return new T(); } }
class Debug { public static void Log(string message) {} }
class RewardGame {
 Upgrade upgrade = new Upgrade(); Zone zone = new Zone(); object selectedVehicle;
 void DoItForHireWorker() {} object WaitAndExecute(object vehicle) { return null; }
 void StartCoroutine(object routine) {}
 void Run() {
 AdsWrapper.Instance.ShowRewardedAd(() => {
     SaveController.MarkAsSaveIsRequired();
     upgrade.UpgradeStage();
     global::AdsMediator.Instance.ShowBanner();
 }, () => { });
 AdsWrapper.Instance.ShowRewardedAd(() => {
     SaveController.MarkAsSaveIsRequired();
     zone.UnlockNurse();
     UIController.GetPage<SecretaryOffice>().GetActivePanel().RedrawPanel();
     DoItForHireWorker();
 }, () => { });
 AdsWrapper.Instance.ShowRewardedAd(() => {
     Debug.Log(""Skate Shoes Button Pressed"");
     StartCoroutine(WaitAndExecute(selectedVehicle));
     //PlayerBehavior.SkateShoesBought(btnSkateShoes.transform.GetChild(0));
 }, () => { });
 } }
";
        var scan = Scan(Source(gameplay));
        var calls = scan.calls.Where(c => c.manager == "AdsWrapper").ToArray();
        Check(calls.Length == 3 && calls.All(c => c.callbacks.All(b => b)), "recognize all three user reward lambdas even when old wrapper cannot resolve");
        var rule = new E.Rule { key = calls[0].key, action = E.AdAction.Rewarded, successArgument = 0, failureArgument = 1 };
        var plan = E.BuildPlan(scan, new HashSet<string> { "AdsWrapper" }, new[] { rule });
        Check(plan.count == 3 && plan.skipped.Count == 0 && plan.errors.Count == 0, "migrate user callback examples without skipped calls or compiler errors");
        Check(plan.files[0].after == gameplay.Replace("AdsWrapper.Instance.ShowRewardedAd", "global::AdsMediator.Instance.ShowRewardedAd"), "preserve complete callback bodies, empty failure lambdas, comments and nested mediator calls");
        var suggested = new E.Rule();
        E.SuggestRewardCallbacks(suggested, calls);
        Check(suggested.successArgument == 0 && suggested.failureArgument == 1, "suggest both user callbacks when mapping a method to Rewarded");
        var named = Scan(Source("class Named { void Run() { AdsWrapper.Instance.ShowRewardedAd(success: () => {}, failure: () => {}); } }"));
        var namedCall = named.calls.Single();
        var namedPlan = E.BuildPlan(named, new HashSet<string> { "AdsWrapper" }, new[] { new E.Rule { key = namedCall.key, action = E.AdAction.Rewarded, successArgument = 0, failureArgument = 1 } });
        Check(namedPlan.count == 1 && namedPlan.errors.Count == 0, "unresolved named callback arguments follow displayed source argument order");
        var invalidBody = Scan(Source("class Invalid { void Run() { AdsWrapper.Instance.ShowRewardedAd(() => { return 123; }, () => {}); } }"));
        var invalidCall = invalidBody.calls.Single();
        var invalidPlan = E.BuildPlan(invalidBody, new HashSet<string> { "AdsWrapper" }, new[] { new E.Rule { key = invalidCall.key, action = E.AdAction.Rewarded, successArgument = 0, failureArgument = 1 } });
        Check(invalidPlan.errors.Count > 0, "compiler preflight rejects a lambda body that cannot convert to Action");
        var parameter = Scan(Source("class Invalid { void Run() { AdsWrapper.Instance.ShowRewardedAd(value => {}, () => {}); } }"));
        Check(!parameter.calls.Single().callbacks[0], "callback requiring an argument is still ineligible");
        var customDelegate = Scan(Source(gameplay + "delegate void UnityAction(); class AdsWrapper { public static AdsWrapper Instance; public void ShowRewardedAd(UnityAction success, UnityAction failure) {} }"));
        var customCall = customDelegate.calls.First(c => c.manager == "AdsWrapper");
        var customPlan = E.BuildPlan(customDelegate, new HashSet<string> { "AdsWrapper" }, new[] { new E.Rule { key = customCall.key, action = E.AdAction.Rewarded, successArgument = 0, failureArgument = 1 } });
        Check(customPlan.count == 3 && customPlan.errors.Count == 0, "lambdas formerly targeting custom delegates convert to mediator Actions");
        var duplicate = new E.Rule { action = E.AdAction.Rewarded, successArgument = 0, failureArgument = 0 };
        Check(E.Replacement(calls[0], duplicate, out string reason) == null && reason.Contains("same argument"), "explain duplicate callback selection precisely");
        Check(!E.Likely("EmojiController", "GetEmoji", "EmojiController.Instance"), "unrelated singleton is not an ad candidate");
        Check(!E.Likely("ShopSequenceRewards", "Initialize", "ShopSequenceRewards"), "gameplay rewards name is not an ad candidate");
        Check(!E.Likely("DownloadsController", "Initialize", "DownloadsController.Instance"), "ads substring inside Downloads is not an ad candidate");
        Check(E.Likely("Moneylitica", "ShowIntertitial", "Moneylitica.Instance"), "arbitrarily named manager with misspelled interstitial stays discoverable");
        Check(E.Likely("AdsWrapper", "Initialize", "AdsWrapper.Instance"), "explicit ad wrappers remain discoverable");
    }

#if UNITY_EDITOR
    private static void ProductionScanTest()
    {
        const string settingsPath = "ProjectSettings/AdsCallerAdCallMigration.json";
        byte[] settingsBefore = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        var window = ScriptableObject.CreateInstance<AdsCallMigrationWindow>();
        try
        {
            typeof(AdsCallMigrationWindow).GetMethod("ScanProject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(window, null);
            var result = (E.Scan)typeof(AdsCallMigrationWindow).GetField("scan", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(window);
            Check(result != null, "production window scans active Unity player assemblies");
            Check(result.calls.All(c => E.Writable(c.source.path)), "production scanner never offers protected implementation files");
        }
        finally
        {
            UnityEditor.EditorUtility.ClearProgressBar();
            UnityEngine.Object.DestroyImmediate(window);
            if (settingsBefore != null) File.WriteAllBytes(settingsPath, settingsBefore);
            else if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }
#endif

    private static void TransactionTests()
    {
        string fixture = "Assets/__AdsCallMigrationFixture_" + Guid.NewGuid().ToString("N");
        string folder = Path.Combine(E.ProjectRoot, fixture);
        Directory.CreateDirectory(folder);
        string path = fixture + "/Game.cs", secondPath = fixture + "/Other.cs";
        string original = "class Game { void Run() { Moneylitica.Instance.ShowIntertitial(1, 2, 3); } }\r\n";
        byte[] bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(original)).ToArray();
        try
        {
            File.WriteAllBytes(Path.Combine(E.ProjectRoot, path), bytes);
            File.WriteAllText(Path.Combine(E.ProjectRoot, secondPath), original.Replace("class Game", "class Other"));
            var scan = Scan(E.ReadSource(Path.Combine(E.ProjectRoot, path), new string[0], true), E.ReadSource(Path.Combine(E.ProjectRoot, secondPath), new string[0], true));
            var plan = E.BuildPlan(scan, new HashSet<string> { "Moneylitica" }, scan.calls.Select(Interstitial).ToArray());
            // The fake mediator is an in-memory fixture; real scans obtain dependencies from disk.
            plan.dependencies.RemoveAll(s => s.path == "Mediator.cs");
            File.AppendAllText(Path.Combine(E.ProjectRoot, secondPath), "// later edit");
            bool stale = false;
            try { E.Apply(plan); } catch (IOException) { stale = true; }
            Check(stale && File.ReadAllBytes(Path.Combine(E.ProjectRoot, path)).SequenceEqual(bytes), "stale second file prevents all writes");
            File.WriteAllBytes(Path.Combine(E.ProjectRoot, secondPath), plan.files.Single(f => f.path == secondPath).beforeBytes);
            string backup = E.Apply(plan);
            var changed = File.ReadAllBytes(Path.Combine(E.ProjectRoot, path));
            Check(changed.Take(3).SequenceEqual(bytes.Take(3)) && File.ReadAllText(Path.Combine(E.ProjectRoot, path)).EndsWith("\r\n"), "preserve UTF-8 BOM and CRLF");
            Check(File.Exists(Path.Combine(backup, "preview.txt")) && File.Exists(Path.Combine(backup, path)), "persist original bytes, receipt and preview outside Assets");
            File.AppendAllText(Path.Combine(E.ProjectRoot, secondPath), "// later edit");
            bool blocked = false;
            try { E.Restore(backup); } catch (IOException) { blocked = true; }
            Check(blocked && File.ReadAllBytes(Path.Combine(E.ProjectRoot, path)).SequenceEqual(changed), "restore preflight protects later edits without partial restoration");
            File.WriteAllBytes(Path.Combine(E.ProjectRoot, secondPath), plan.files.Single(f => f.path == secondPath).afterBytes);
            E.Restore(backup);
            Check(File.ReadAllBytes(Path.Combine(E.ProjectRoot, path)).SequenceEqual(bytes), "restore exact original bytes");
            bool traversal = false;
            try { E.ValidatePath("../Outside.cs"); } catch (IOException) { traversal = true; }
            Check(traversal, "reject paths outside gameplay Assets");
        }
        finally
        {
            // Only this uniquely named, test-created directory is removed.
            if (Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(folder, true);
        }
    }
}
