using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using R = AdsMigrationRoslyn;

internal static class AdsCallMigrationEngine
{
    internal enum AdAction { Unmapped, Interstitial, ShowBanner, HideBanner, ShowLargeBanner, HideLargeBanner, HideAllBanners, Rewarded, BackButtonAd, RemoveAds }
    [Serializable] internal sealed class Rule
    {
        public string key;
        public AdAction action;
        public int successArgument = 0, failureArgument = -1;
    }
    internal sealed class Source
    {
        internal string path, text;
        internal byte[] bytes;
        internal object tree, compilation;
        internal bool writable;
    }
    internal sealed class CallSite
    {
        internal Source source;
        internal string manager, key, method, signature, original, blocked;
        internal string[] arguments, argumentLabels;
        internal bool[] callbacks;
        internal int start, length, line;
        internal bool resolved, likely;
    }
    internal sealed class Reference
    {
        internal string manager, path, text;
        internal int line;
    }
    internal sealed class Scan
    {
        internal readonly List<Source> sources = new List<Source>();
        internal readonly List<CallSite> calls = new List<CallSite>();
        internal readonly List<Reference> references = new List<Reference>();
        internal readonly List<string> notes = new List<string>();
    }
    internal sealed class Change
    {
        internal string path, before, after;
        internal byte[] beforeBytes, afterBytes;
        internal Source source;
    }
    internal sealed class Plan
    {
        internal readonly List<Source> dependencies = new List<Source>();
        internal readonly List<Change> files = new List<Change>();
        internal readonly List<string> skipped = new List<string>();
        internal readonly List<string> preview = new List<string>();
        internal readonly List<string> errors = new List<string>();
        internal int count;
    }
    [Serializable] internal sealed class Receipt
    {
        public string project;
        public string state;
        public List<Backup> files = new List<Backup>();
    }
    [Serializable] internal sealed class Backup
    {
        public string path, originalHash, changedHash;
    }

    internal static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    internal static string BackupRoot => Path.Combine(ProjectRoot, "AdsCallerMigrationBackups", "AdCalls");
    internal static string Normalize(string path) => path.Replace('\\', '/');
    internal static string Relative(string path)
    {
        string full = Normalize(Path.GetFullPath(path));
        string root = Normalize(ProjectRoot).TrimEnd('/') + "/";
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : full;
    }
    // These are implementation dependencies, not gameplay entry points. Never rewrite them.
    internal static bool Writable(string path)
    {
        string rel = Relative(path);
        if (!rel.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || !rel.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return false;
        string[] protectedRoots = { "Assets/AdsCaller/", "Assets/MMPHandler/", "Assets/MaxSdk/", "Assets/GoogleMobileAds/", "Assets/ExternalDependencyManager/", "Assets/Plugins/", "Assets/Editor/", "Assets/AdsCallerPackageValidation/" };
        return !protectedRoots.Any(p => rel.StartsWith(p, StringComparison.OrdinalIgnoreCase)) &&
            !rel.Split('/').Any(p => p.Equals("Editor", StringComparison.OrdinalIgnoreCase) || p.Equals("Tests", StringComparison.OrdinalIgnoreCase));
    }

    internal static Source ReadSource(string path, string[] defines, bool writable)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string text;
        using (var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true)) text = reader.ReadToEnd();
        return new Source { path = Relative(path), text = text, bytes = bytes, tree = R.Parse(text, path, defines), writable = writable };
    }

    internal static void Analyze(Scan result, Source[] sources, string[] references)
    {
        var compilation = R.Compilation(sources.Select(s => s.tree), references);
        result.sources.AddRange(sources);
        foreach (var source in sources) source.compilation = compilation;
        foreach (var source in sources.Where(s => s.writable))
        {
            if (R.HasErrors(source.tree)) { result.notes.Add(source.path + ": parser errors; this file was skipped."); continue; }
            object model = R.Call(compilation, "GetSemanticModel", source.tree, true);
            object root = R.Call(source.tree, "GetRoot");
            var nodes = R.Nodes(root);
            var aliases = new Dictionary<string, string>();
            foreach (var directive in nodes.Where(n => R.Kind(n) == "UsingDirectiveSyntax" && R.Get(n, "Alias") != null))
                aliases[R.Text(R.Get(R.Get(directive, "Alias"), "Name"))] = R.Text(R.Get(directive, "Name"));

            foreach (var invocation in nodes.Where(n => R.Kind(n) == "InvocationExpressionSyntax"))
            {
                object expression = R.Get(invocation, "Expression"), receiver = R.Get(expression, "Expression");
                string method = R.Name(R.Get(expression, "Name"));
                bool conditional = R.Kind(expression) == "MemberBindingExpressionSyntax";
                if (conditional)
                {
                    var parent = R.Get(invocation, "Parent");
                    if (R.Kind(parent) == "ConditionalAccessExpressionSyntax") receiver = R.Get(parent, "Expression");
                }
                object symbol = R.Symbol(model, invocation);
                object type = R.Get(symbol, "ContainingType");
                bool resolved = type != null && R.Text(R.Get(type, "TypeKind")) != "Error";
                if (string.IsNullOrEmpty(method)) method = R.Name(expression);
                if (string.IsNullOrEmpty(method)) continue;
                string receiverText = R.Text(receiver);
                string manager = resolved ? R.Display(type) : FallbackManager(receiverText, aliases);
                if (string.IsNullOrEmpty(manager) || manager == "AdsMediator" || manager == "global::AdsMediator") continue;
                // Ignore calls inside the manager itself, including its nested implementation types.
                bool ownImplementation = false;
                for (object p = R.Get(invocation, "Parent"); p != null; p = R.Get(p, "Parent"))
                    if (R.Kind(p) == "ClassDeclarationSyntax" || R.Kind(p) == "StructDeclarationSyntax")
                    {
                        var declaration = R.Call(model, "GetDeclaredSymbol", p);
                        if (R.Display(declaration) == manager || (!resolved && R.Name(p) == manager.Split('.').Last())) ownImplementation = true;
                    }
                if (ownImplementation) continue;
                var args = R.Items(R.Get(R.Get(invocation, "ArgumentList"), "Arguments"));
                var parameters = resolved ? R.Items(R.Get(symbol, "Parameters")) : new object[0];
                int slots = resolved ? parameters.Length : args.Length;
                var values = new string[slots];
                var callbacks = new bool[slots];
                string[] labels = resolved ? parameters.Select(p => R.Text(R.Get(p, "Name"))).ToArray() : Enumerable.Range(1, slots).Select(i => "Argument " + i).ToArray();
                for (int i = 0; i < args.Length; i++)
                {
                    string named = R.Text(R.Get(R.Get(args[i], "NameColon"), "Name"));
                    int slot = !resolved || string.IsNullOrEmpty(named) ? i : Array.IndexOf(labels, named);
                    if (slot < 0 || slot >= slots) continue;
                    object argument = R.Get(args[i], "Expression");
                    values[slot] = R.Text(argument);
                    callbacks[slot] = R.Text(R.Get(args[i], "RefKindKeyword")) == "" && IsCallback(model, argument);
                }
                string signature = resolved ? R.Display(symbol) : method + "(" + args.Length + " arguments; unresolved type)";
                string blocked = "";
                if (conditional || R.Kind(R.Get(invocation, "Parent")) != "ExpressionStatementSyntax")
                    blocked = "Call is conditional, nested, or used as a value; edit this occurrence manually.";
                if (nodes.Any(n => R.Kind(n) == "DirectiveTriviaSyntax" && R.Start(n) >= R.Start(invocation) && R.Start(n) < R.Start(invocation) + R.Length(invocation)))
                    blocked = "Preprocessor directive inside invocation; edit manually.";
                // Trivia includes disabled branches/directives that ordinary node traversal omits.
                if (R.Items(R.Call(invocation, "DescendantTrivia")).Any(t => Convert.ToBoolean(R.Get(t, "HasStructure"))))
                    blocked = "Structured trivia inside invocation; edit manually.";
                result.calls.Add(new CallSite {
                    source = source, manager = manager, key = manager + "|" + signature, method = method, signature = signature,
                    arguments = values, argumentLabels = labels, callbacks = callbacks, start = R.Start(invocation), length = R.Length(invocation),
                    original = source.text.Substring(R.Start(invocation), R.Length(invocation)), line = Line(source.text, R.Start(invocation)),
                    resolved = resolved, blocked = blocked, likely = Likely(manager, method, receiverText)
                });
            }
            // Inventory type references (fields, casts, using aliases, etc.), without rewriting them.
            foreach (var identifier in nodes.Where(n => R.Kind(n) == "IdentifierNameSyntax"))
            {
                object symbol = R.Symbol(model, identifier);
                if (R.Text(R.Get(symbol, "Kind")) != "NamedType") continue;
                int start = R.Start(identifier);
                if (result.calls.Any(c => c.source == source && start >= c.start && start < c.start + c.length)) continue;
                result.references.Add(new Reference { manager = R.Display(symbol), path = source.path, line = Line(source.text, start), text = R.Text(R.Get(identifier, "Parent")) });
            }
        }
    }

    private static bool IsCallback(object model, object expression)
    {
        while (R.Kind(expression) == "ParenthesizedExpressionSyntax") expression = R.Get(expression, "Expression");
        string kind = R.Kind(expression);
        // An unresolved old method supplies no ConvertedType for its lambdas. Their
        // parameter list is still known. Validate the resulting Action conversion
        // (and the complete body) in BuildPlan's compiler preflight instead.
        if (kind == "SimpleLambdaExpressionSyntax") return false;
        if (kind == "ParenthesizedLambdaExpressionSyntax" || kind == "AnonymousMethodExpressionSyntax")
            return R.Items(R.Get(R.Get(expression, "ParameterList"), "Parameters")).Length == 0;
        var typeInfo = R.Call(model, "GetTypeInfo", expression);
        string type = R.Display(R.Get(typeInfo, "Type"));
        if (type == "System.Action") return true;
        var method = R.Symbol(model, expression);
        return R.Text(R.Get(method, "Kind")) == "Method" && R.Items(R.Get(method, "Parameters")).Length == 0 && Equals(R.Get(method, "ReturnsVoid"), true);
    }

    private static string FallbackManager(string receiver, Dictionary<string, string> aliases)
    {
        string candidate = receiver.Replace("global::", "");
        foreach (string suffix in new[] { ".Instance", ".instance", ".Singleton", ".singleton", ".GetInstance()" })
            if (candidate.EndsWith(suffix, StringComparison.Ordinal)) { candidate = candidate.Substring(0, candidate.Length - suffix.Length); break; }
        if (candidate.Length == 0 || candidate.Any(c => !(char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '@'))) return "";
        string first = candidate.Split('.')[0];
        if (aliases.TryGetValue(first, out var full)) candidate = full + candidate.Substring(first.Length);
        // An unresolved local variable cannot be reliably assigned to a manager type.
        string last = candidate.Split('.').Last().TrimStart('@');
        if (last.Length == 0 || !char.IsUpper(last[0])) return "";
        return candidate;
    }
    internal static bool Likely(string manager, string method, string receiver)
    {
        // Singleton access and gameplay rewards are not evidence of advertising.
        // Match name components so Downloads/Threads/etc. do not match "ads".
        string[] managerWords = NameWords(manager), methodWords = NameWords(method);
        if (managerWords.Any(w => w == "ad" || w == "ads" || w == "advertising" || w == "advertisement") ||
            methodWords.Any(w => w == "ad" || w == "ads" || w == "advertisement")) return true;
        string managerName = manager.Split('.').Last().ToLowerInvariant();
        if (new[] { "adswrapper", "adwrapper", "adsmanager", "admanager", "adscontroller", "adcontroller", "adservice", "adsservice", "admob", "maxsdk", "ironsource", "applovin" }.Contains(managerName)) return true;
        string operation = method.Replace("_", "").ToLowerInvariant();
        bool adOperation = new[] { "show", "hide", "load", "request", "display", "play", "is", "has", "can", "destroy" }.Any(operation.StartsWith);
        return adOperation && (operation.Contains("interstitial") || operation.Contains("intertitial") || operation.Contains("interstital") ||
            operation.Contains("banner") || operation.Contains("rewarded") || operation.Contains("fullscreenad"));
    }
    private static string[] NameWords(string name)
    {
        string separated = Regex.Replace(name, "([A-Z])([A-Z][a-z])", "$1 $2");
        separated = Regex.Replace(separated, "([a-z0-9])([A-Z])", "$1 $2");
        return Regex.Split(separated.ToLowerInvariant(), "[^a-z0-9]+");
    }

    internal static void SuggestRewardCallbacks(Rule rule, IEnumerable<CallSite> calls)
    {
        var sites = calls.ToArray();
        int[] compatible = sites.Length == 0 ? new int[0] : Enumerable.Range(0, sites[0].arguments.Length)
            .Where(i => sites.All(c => CallbackAt(c, i))).ToArray();
        // More than two callbacks may include unrelated lifecycle events; leave the choice explicit.
        rule.successArgument = compatible.Length == 1 || compatible.Length == 2 ? compatible[0] : -1;
        rule.failureArgument = compatible.Length == 2 ? compatible[1] : -1;
    }
    private static int Line(string source, int position) => source.Take(position).Count(c => c == '\n') + 1;

    internal static string Replacement(CallSite call, Rule rule, out string reason)
    {
        reason = call.blocked;
        if (!string.IsNullOrEmpty(reason)) return null;
        const string mediator = "global::AdsMediator.Instance.";
        switch (rule.action)
        {
            case AdAction.Interstitial: return mediator + "ShowInterstitial()";
            case AdAction.ShowBanner: return mediator + "ShowBanner()";
            case AdAction.HideBanner: return mediator + "HideBanner()";
            case AdAction.ShowLargeBanner: return mediator + "ShowLargeBanner()";
            case AdAction.HideLargeBanner: return mediator + "HideLargeBanner()";
            case AdAction.HideAllBanners: return mediator + "HideAllBanners()";
            case AdAction.BackButtonAd: return mediator + "BackBtnAd()";
            case AdAction.RemoveAds: return "global::AdsMediator.RemoveAds()";
            case AdAction.Rewarded:
                int success = rule.successArgument, failure = rule.failureArgument;
                if (!CallbackAt(call, success))
                { reason = "Reward callback " + (success < 0 ? "is not selected" : "argument " + (success + 1) + " is missing or is not a parameterless callback") + ". Choose the success lambda/Action in this method's mapping."; return null; }
                if (failure == success)
                { reason = "Success and failure point to the same argument. Select the failure callback separately, or choose No failure callback."; return null; }
                if (failure >= 0 && !CallbackAt(call, failure))
                { reason = "Failure callback argument " + (failure + 1) + " is missing or is not a parameterless callback. Choose its lambda/Action, or No failure callback."; return null; }
                return mediator + "ShowRewardedAd(" + call.arguments[success] + (failure < 0 ? "" : ", " + call.arguments[failure]) + ")";
            default: reason = "No action mapped."; return null;
        }
    }
    private static bool CallbackAt(CallSite call, int slot) => slot >= 0 && slot < call.arguments.Length && !string.IsNullOrEmpty(call.arguments[slot]) && call.callbacks[slot];

    internal static Plan BuildPlan(Scan scan, HashSet<string> selected, IList<Rule> rules)
    {
        var plan = new Plan();
        var map = rules.GroupBy(r => r.key).ToDictionary(g => g.Key, g => g.Last());
        foreach (var file in scan.calls.Where(c => selected.Contains(c.manager)).GroupBy(c => c.source))
        {
            var edits = new List<Tuple<CallSite, string>>();
            foreach (var call in file)
            {
                if (!map.TryGetValue(call.key, out var rule) || rule.action == AdAction.Unmapped) { plan.skipped.Add(Location(call) + "Unmapped: " + call.method); continue; }
                string replacement = Replacement(call, rule, out var reason);
                if (replacement == null) { plan.skipped.Add(Location(call) + reason); continue; }
                edits.Add(Tuple.Create(call, replacement));
            }
            // Never let an outer edit silently consume another independently selected invocation.
            var overlapping = new HashSet<CallSite>();
            foreach (var edit in edits)
                foreach (var other in edits)
                    if (edit != other && edit.Item1.start < other.Item1.start + other.Item1.length && other.Item1.start < edit.Item1.start + edit.Item1.length)
                    { overlapping.Add(edit.Item1); overlapping.Add(other.Item1); }
            foreach (var call in overlapping) plan.skipped.Add(Location(call) + "Overlapping selected calls; edit manually.");
            edits.RemoveAll(e => overlapping.Contains(e.Item1));
            if (edits.Count == 0) continue;
            string after = file.Key.text;
            foreach (var edit in edits.OrderByDescending(e => e.Item1.start))
            {
                var call = edit.Item1;
                after = after.Remove(call.start, call.length).Insert(call.start, edit.Item2);
                plan.preview.Add(Location(call) + "\n- " + call.original + "\n+ " + edit.Item2);
            }
            plan.files.Add(new Change { source = file.Key, path = file.Key.path, before = file.Key.text, after = after, beforeBytes = file.Key.bytes, afterBytes = EncodeLike(file.Key.bytes, after) });
            plan.count += edits.Count;
        }
        var changedAssemblies = new HashSet<object>(plan.files.Select(f => f.source.compilation));
        plan.dependencies.AddRange(scan.sources.Where(s => changedAssemblies.Contains(s.compilation)));
        ValidateCompilation(plan);
        return plan;
    }

    private static Dictionary<string, int> Errors(object compilation)
    {
        return R.Items(R.Call(compilation, "GetDiagnostics")).Where(d => R.Text(R.Get(d, "Severity")) == "Error")
            .Select(d => R.Text(R.Get(R.Get(R.Get(d, "Location"), "SourceTree"), "FilePath")) + ": " + R.Text(R.Get(d, "Id")) + " " + R.Text(R.Call(d, "GetMessage")))
            .GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());
    }
    private static void ValidateCompilation(Plan plan)
    {
        foreach (var group in plan.files.GroupBy(f => f.source.compilation))
        {
            var baseline = Errors(group.Key);
            object changed = group.Key;
            foreach (var file in group)
                changed = R.Call(changed, "ReplaceSyntaxTree", file.source.tree, R.ParseLike(file.after, file.source.tree));
            foreach (var error in Errors(changed))
                if (!baseline.TryGetValue(error.Key, out int previous) || error.Value > previous)
                    plan.errors.Add(error.Key);
        }
    }
    private static string Location(CallSite call) => call.source.path + ":" + call.line + " — ";
    private static byte[] EncodeLike(byte[] original, string text)
    {
        Encoding encoding;
        using (var reader = new StreamReader(new MemoryStream(original), new UTF8Encoding(false, true), true)) { reader.ReadToEnd(); encoding = reader.CurrentEncoding; }
        var preamble = encoding.GetPreamble();
        bool hadPreamble = preamble.Length > 0 && original.Take(preamble.Length).SequenceEqual(preamble);
        return (hadPreamble ? preamble : new byte[0]).Concat(encoding.GetBytes(text)).ToArray();
    }
    internal static string Hash(byte[] data)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
    }

    internal static void ValidatePath(string relative)
    {
        string full = Path.GetFullPath(Path.Combine(ProjectRoot, relative));
        if (Path.IsPathRooted(relative) || !Writable(full)) throw new IOException("Migration target is outside editable gameplay scripts: " + relative);
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(full)); directory != null; directory = directory.Parent)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Migration does not follow linked folders: " + directory.FullName);
            if (string.Equals(directory.FullName, ProjectRoot, StringComparison.OrdinalIgnoreCase)) break;
        }
        if ((File.GetAttributes(full) & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) != 0) throw new IOException("File is read-only or linked: " + relative);
    }

    internal static string Apply(Plan plan)
    {
        if (plan.count == 0) throw new InvalidOperationException("There are no mapped calls to replace.");
        if (plan.errors.Count > 0) throw new InvalidOperationException("Replacement introduces compiler errors. Review the preview before applying: " + string.Join("\n", plan.errors));
        foreach (var source in plan.dependencies)
        {
            string path = Path.IsPathRooted(source.path) ? source.path : Path.Combine(ProjectRoot, source.path);
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(source.bytes))
                throw new IOException("A source dependency changed since scanning. Scan and preview again: " + source.path);
        }
        foreach (var file in plan.files)
        {
            ValidatePath(file.path);
            if (!File.ReadAllBytes(Path.Combine(ProjectRoot, file.path)).SequenceEqual(file.beforeBytes))
                throw new IOException("File changed since scanning. Scan and preview again: " + file.path);
        }
        string folder = Path.Combine(BackupRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        var receipt = new Receipt { project = ProjectRoot, state = "prepared" };
        foreach (var file in plan.files)
        {
            string backup = Path.Combine(folder, file.path);
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            File.WriteAllBytes(backup, file.beforeBytes);
            receipt.files.Add(new Backup { path = file.path, originalHash = Hash(file.beforeBytes), changedHash = Hash(file.afterBytes) });
        }
        string receiptPath = Path.Combine(folder, "receipt.json");
        File.WriteAllText(receiptPath, JsonUtility.ToJson(receipt, true));
        File.WriteAllText(Path.Combine(folder, "preview.txt"), string.Join("\n\n", plan.preview));
        var written = new List<Change>();
        try
        {
            foreach (var file in plan.files)
            {
                // Also protects files changed while the backup was being written.
                if (!File.ReadAllBytes(Path.Combine(ProjectRoot, file.path)).SequenceEqual(file.beforeBytes)) throw new IOException("File changed during migration: " + file.path);
                written.Add(file);
                File.WriteAllBytes(Path.Combine(ProjectRoot, file.path), file.afterBytes);
            }
            receipt.state = "applied";
            File.WriteAllText(receiptPath, JsonUtility.ToJson(receipt, true));
        }
        catch
        {
            foreach (var file in written) File.WriteAllBytes(Path.Combine(ProjectRoot, file.path), file.beforeBytes);
            receipt.state = "rolled back";
            File.WriteAllText(receiptPath, JsonUtility.ToJson(receipt, true));
            throw;
        }
        return folder;
    }

    internal static string LatestBackup()
    {
        if (!Directory.Exists(BackupRoot)) return null;
        foreach (var folder in Directory.GetDirectories(BackupRoot).OrderByDescending(p => p, StringComparer.Ordinal))
        {
            string path = Path.Combine(folder, "receipt.json");
            if (File.Exists(path) && JsonUtility.FromJson<Receipt>(File.ReadAllText(path)).state == "applied") return folder;
        }
        return null;
    }
    internal static void Restore(string folder)
    {
        string root = Path.GetFullPath(BackupRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(folder).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid backup folder.");
        string manifest = Path.Combine(folder, "receipt.json");
        var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(manifest));
        if (receipt.project != ProjectRoot || receipt.state != "applied") throw new IOException("This backup cannot be restored in this project.");
        var current = new Dictionary<string, byte[]>();
        foreach (var file in receipt.files)
        {
            ValidatePath(file.path);
            byte[] bytes = File.ReadAllBytes(Path.Combine(ProjectRoot, file.path));
            if (Hash(bytes) != file.changedHash) throw new IOException("Restore stopped because this file has later edits: " + file.path);
            if (Hash(File.ReadAllBytes(Path.Combine(folder, file.path))) != file.originalHash) throw new IOException("Backup is damaged: " + file.path);
            current[file.path] = bytes;
        }
        var written = new List<string>();
        try
        {
            foreach (var file in receipt.files)
            {
                if (!File.ReadAllBytes(Path.Combine(ProjectRoot, file.path)).SequenceEqual(current[file.path])) throw new IOException("File changed during restore: " + file.path);
                written.Add(file.path);
                File.WriteAllBytes(Path.Combine(ProjectRoot, file.path), File.ReadAllBytes(Path.Combine(folder, file.path)));
            }
            receipt.state = "restored";
            File.WriteAllText(manifest, JsonUtility.ToJson(receipt, true));
        }
        catch { foreach (var path in written) File.WriteAllBytes(Path.Combine(ProjectRoot, path), current[path]); throw; }
    }
}
