using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;

// Late binding keeps compiler assemblies out of the exported package and player builds.
// Unity 2022.3 and Unity 6 ship these Roslyn assemblies with their Mono runtime.
internal static class AdsMigrationRoslyn
{
    private static Assembly core, csharp;
    private static readonly Dictionary<string, MethodInfo[]> methods = new Dictionary<string, MethodInfo[]>();

    internal static void Initialize()
    {
        if (csharp != null) return;
        string folder = Path.Combine(EditorApplication.applicationContentsPath, "MonoBleedingEdge/lib/mono/4.5");
        foreach (string name in new[] { "System.Collections.Immutable", "System.Reflection.Metadata", "Microsoft.CodeAnalysis", "Microsoft.CodeAnalysis.CSharp" })
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
            if (assembly == null)
            {
                string path = Path.Combine(folder, name + ".dll");
                if (!File.Exists(path)) throw new InvalidOperationException("Unity's C# parser is unavailable: " + path);
                assembly = Assembly.LoadFrom(path);
            }
            if (name == "Microsoft.CodeAnalysis") core = assembly;
            if (name == "Microsoft.CodeAnalysis.CSharp") csharp = assembly;
        }
    }

    internal static object Get(object value, string property)
    {
        if (value == null) return null;
        var type = value.GetType();
        PropertyInfo info;
        for (var current = type; current != null; current = current.BaseType)
        {
            info = current.GetProperty(property, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (info != null) return info.GetValue(value, null);
        }
        foreach (var contract in type.GetInterfaces())
        {
            info = contract.GetProperty(property);
            if (info != null) return info.GetValue(value, null);
        }
        return null;
    }

    internal static object Call(object target, string method, params object[] supplied)
    {
        var type = target as Type ?? target.GetType();
        string key = type.AssemblyQualifiedName + ":" + method + ":" + (target is Type);
        if (!methods.TryGetValue(key, out var candidates))
        {
            candidates = type.GetMethods(BindingFlags.Public | (target is Type ? BindingFlags.Static : BindingFlags.Instance))
                .Concat(target is Type ? new MethodInfo[0] : type.GetInterfaces().SelectMany(i => i.GetMethods()))
                .Where(m => m.Name == method && !m.ContainsGenericParameters).OrderBy(m => m.GetParameters().Length).ToArray();
            methods[key] = candidates;
        }
        foreach (var candidate in candidates)
        {
            var parameters = candidate.GetParameters();
            if (parameters.Length < supplied.Length) continue;
            bool match = true;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i >= supplied.Length) { if (!parameters[i].IsOptional) match = false; }
                else if (supplied[i] != null && !parameters[i].ParameterType.IsInstanceOfType(supplied[i])) match = false;
                else if (supplied[i] == null && parameters[i].ParameterType.IsValueType && Nullable.GetUnderlyingType(parameters[i].ParameterType) == null) match = false;
            }
            if (!match) continue;
            var arguments = new object[parameters.Length];
            for (int i = 0; i < arguments.Length; i++)
                arguments[i] = i < supplied.Length ? supplied[i] : parameters[i].DefaultValue;
            return candidate.Invoke(target is Type ? null : target, arguments);
        }
        throw new MissingMethodException(type.FullName, method);
    }

    internal static object[] Items(object collection) => collection == null ? new object[0] : ((IEnumerable)collection).Cast<object>().ToArray();
    internal static string Kind(object node) => node == null ? "" : node.GetType().Name;
    internal static int Start(object node) => (int)Get(Get(node, "Span"), "Start");
    internal static int Length(object node) => (int)Get(Get(node, "Span"), "Length");
    internal static string Text(object node) => node == null ? "" : node.ToString();
    internal static string Name(object node) => Text(Get(Get(node, "Identifier"), "ValueText"));
    internal static object[] Nodes(object root) => Items(Call(root, "DescendantNodes"));
    internal static string Display(object symbol) => symbol == null ? "" : Text(Call(symbol, "ToDisplayString"));

    private static Array Typed(string name, IEnumerable<object> values)
    {
        var items = values.ToArray();
        var array = Array.CreateInstance(core.GetType("Microsoft.CodeAnalysis." + name, true), items.Length);
        for (int i = 0; i < items.Length; i++) array.SetValue(items[i], i);
        return array;
    }

    internal static object Parse(string source, string path, string[] defines)
    {
        Initialize();
        var optionsType = csharp.GetType("Microsoft.CodeAnalysis.CSharp.CSharpParseOptions", true);
        var options = optionsType.GetProperty("Default").GetValue(null, null);
        options = Call(options, "WithPreprocessorSymbols", (object)defines);
        return Call(csharp.GetType("Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree", true), "ParseText", source, options, path);
    }

    internal static object ParseLike(string source, object tree) => Call(csharp.GetType("Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree", true),
        "ParseText", source, Get(tree, "Options"), Text(Get(tree, "FilePath")));

    internal static object Compilation(IEnumerable<object> trees, IEnumerable<string> referencePaths)
    {
        Initialize();
        var references = new List<object>();
        foreach (string path in referencePaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            references.Add(Call(core.GetType("Microsoft.CodeAnalysis.MetadataReference", true), "CreateFromFile", Path.GetFullPath(path)));
        return Call(csharp.GetType("Microsoft.CodeAnalysis.CSharp.CSharpCompilation", true), "Create", "AdsCallerMigrationScan",
            Typed("SyntaxTree", trees), Typed("MetadataReference", references));
    }

    internal static object Symbol(object model, object expression) => Get(Call(model, "GetSymbolInfo", expression), "Symbol");
    internal static object TypeOf(object model, object expression) => Get(Call(model, "GetTypeInfo", expression), "Type");
    internal static bool HasErrors(object tree) => Items(Call(tree, "GetDiagnostics")).Any(d => Text(Get(d, "Severity")) == "Error");
}
