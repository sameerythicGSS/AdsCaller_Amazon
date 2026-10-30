using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using CompressionLevel = System.IO.Compression.CompressionLevel;

// The delivery envelope contains no provider scripts: UPM resolves dependencies before importing them.
public static class AdsCallerUpmExport
{
    public const string Prefix = "com.adscaller.family.";
    // UPM requires three numeric components; retain the exporter's release number.
    public static string NormalizeVersion(string version)
    {
        version = (version ?? string.Empty).Trim();
        if (Regex.IsMatch(version, @"^(0|[1-9]\d*)\.(0|[1-9]\d*)$")) version += ".0";
        if (!Regex.IsMatch(version, @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$"))
            throw new ArgumentException("Enter a package version such as 2.5, 2.5.0 or 2.5.0-beta.1.", nameof(version));
        return version;
    }
    [Serializable] public sealed class Delivery { public int schema = 1; public string unity; public string online; public string[] packages; public Entry[] assets; }
    [Serializable] public sealed class Entry { public string path, guid, package, originalGuidBase64; public bool configuration; }
    [Serializable] private sealed class Index { public Entry[] assets; }
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
    public static string Identity(string value)
    {
        using (var md5 = MD5.Create()) return BitConverter.ToString(md5.ComputeHash(Utf8.GetBytes("adscaller.family/" + value))).Replace("-", "").ToLowerInvariant();
    }
    private static string Json(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    private static string Physical(string path)
    {
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
        return package == null ? path : package.resolvedPath + path.Substring(package.assetPath.Length);
    }
    private static IEnumerable<string> Expand(IEnumerable<string> roots)
    {
        return roots.SelectMany(root => {
            string resolved = AdsCallerAssets.Resolve(root), physical = Physical(resolved);
            if (Directory.Exists(physical)) return Directory.GetFiles(physical, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith(".meta")).Select(p => resolved + p.Substring(physical.Length).Replace('\\', '/'));
            return File.Exists(physical) ? new[] { resolved } : Array.Empty<string>();
        }).Distinct().OrderBy(p => p, StringComparer.Ordinal);
    }

    private static bool IsCatalogMedia(string source, string databaseRoot)
    {
        if (!source.StartsWith(databaseRoot, StringComparison.Ordinal)) return false;
        var importer = AssetImporter.GetAtPath(source);
        return importer is TextureImporter || importer is AudioImporter || importer is UnityEditor.VideoClipImporter;
    }

    // Transform only the bytes being packaged. Never save or temporarily edit source assets.
    private static byte[] ReadExportBytes(string source, bool stripCatalogData, HashSet<string> excludedMediaGuids)
    {
        byte[] bytes = File.ReadAllBytes(Physical(source));
        string extension = Path.GetExtension(source);
        if (!stripCatalogData || (extension != ".asset" && extension != ".prefab" && extension != ".unity")) return bytes;
        string text = Utf8.GetString(bytes);
        if (!text.TrimStart('\uFEFF').StartsWith("%YAML", StringComparison.Ordinal))
            throw new InvalidDataException("Catalog-free export requires text serialization: " + source
                + ". Reserialize this asset using Unity Force Text before exporting without catalog data.");

        // Unity serializes root fields at two spaces, and list items at the same indentation.
        text = Regex.Replace(text, @"(?m)^  (gameEntries|runtimeCatalog):[^\r\n]*(?:\r?\n(?:  -[^\r\n]*|[ \t]{3,}[^\r\n]*))*", "  $1: []");
        text = Regex.Replace(text, @"(?m)^(  (?:sheet1CrossPromoURL|sheet2GameDataURL):)[^\r\n]*", "$1 ");
        // Demo scenes and prefab variants can override the URLs or repopulate the arrays.
        text = Regex.Replace(text, @"(?m)^( +propertyPath: (?:sheet1CrossPromoURL|sheet2GameDataURL)\r?\n +value:)[^\r\n]*", "$1 ");
        text = Regex.Replace(text, @"(?m)^(?<indent> +)- target:[^\r\n]*\r?\n\k<indent>  propertyPath: (?:gameEntries|runtimeCatalog)(?:\.[^\r\n]*)?\r?\n\k<indent>  value:[^\r\n]*\r?\n\k<indent>  objectReference:[^\r\n]*(?:\r?\n|$)", string.Empty);
        // Ad-body prefabs also contain preview artwork from the database. Clear only
        // references to omitted media; runtime catalog loading supplies the actual ad.
        text = Regex.Replace(text, @"\{fileID: [^,}]+, guid: ([a-fA-F0-9]{32}), type: [^}]+\}",
            match => excludedMediaGuids.Contains(match.Groups[1].Value) ? "{fileID: 0}" : match.Value);
        return Utf8.GetBytes(text);
    }

    public static void Export(string output, string online, bool cross, bool sdks, IEnumerable<string> coreRoots,
        IEnumerable<string> onlineRoots, IEnumerable<string> crossRoots, IEnumerable<string> sdkRoots, IEnumerable<string> projectRoots,
        bool tarballs = false, bool includeCatalogData = true, string packageVersion = null)
    {
        string version = NormalizeVersion(packageVersion);
        var data = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var entries = new List<Entry>();
        bool stripCatalogData = cross && !includeCatalogData;
        string databaseRoot = AdsCallerAssets.Resolve("Assets/AdsCaller/SalonAds/Database").TrimEnd('/') + "/";
        var excludedMedia = new HashSet<string>(stripCatalogData
            ? Expand(crossRoots).Where(p => IsCatalogMedia(p, databaseRoot)) : Enumerable.Empty<string>(), StringComparer.Ordinal);
        var excludedMediaGuids = new HashSet<string>(excludedMedia.Select(AssetDatabase.AssetPathToGUID)
            .Where(g => !string.IsNullOrEmpty(g)), StringComparer.Ordinal);
        var sourceIndexes = AssetDatabase.FindAssets("AdsCallerAssetIndex t:TextAsset").Select(AssetDatabase.GUIDToAssetPath)
            .Select(p => JsonUtility.FromJson<Index>(File.ReadAllText(Physical(p))))
            .Where(i => i?.assets != null).SelectMany(i => i.assets).ToArray();
        var modules = new Dictionary<string, IEnumerable<string>> { { "core", coreRoots } };
        if (!string.IsNullOrEmpty(online)) modules.Add("main." + online, onlineRoots);
        if (cross) modules.Add("cross-promo", crossRoots.Where(p => p != "Assets/AddressableAssetsData"));
        if (sdks && !string.IsNullOrEmpty(online))
            foreach (var group in sdkRoots.GroupBy(p => p.Contains("/MaxSdk") ? "sdk.max" : p.Contains("/ExternalDependencyManager") ? "sdk.edm" : "sdk.admob")) modules.Add(group.Key, group);
        var claimed = new HashSet<string>();
        foreach (var module in modules)
        {
            var index = new List<Entry>();
            foreach (string source in Expand(module.Value))
            {
                string physical = Physical(source), guid = AssetDatabase.AssetPathToGUID(source);
                if (!claimed.Add(source) || source.EndsWith("/AdsCallerAssetIndex.json") || source.Contains("/Tests/")
                    || Regex.IsMatch(source, @"/(AppLovinSettings|GoogleMobileAdsSettings)\.asset$")) continue;
                // Catalog entries are stripped below; their artwork must not remain in the payload/index.
                // Limit this to the database so CrossPromo UI sprites and render textures remain available.
                if (excludedMedia.Contains(source)) continue;
                string relative = source.StartsWith("Assets/AdsCaller/", StringComparison.Ordinal) ? source.Substring(17) : source.StartsWith("Assets/") ? source.Substring(7) : source.Substring(source.IndexOf('/', 9) + 1);
                if (relative == "Editor/OutBuildAutomator.cs") relative = "Editor/Compatibility/OutBuildAutomator.cs";
                string target = "packages/" + Prefix + module.Key + "/" + relative;
                byte[] bytes = ReadExportBytes(source, stripCatalogData, excludedMediaGuids);
                bool config = source.EndsWith(".asset") && (source.Contains("/Data/") || source.Contains("/Database/") || source.EndsWith("/AdsConfiguration.asset"));
                data.Add(target, bytes);
                if (File.Exists(physical + ".meta")) data.Add(target + ".meta", File.ReadAllBytes(physical + ".meta"));
                string originalGuid = guid;
                var originalEntry = sourceIndexes.FirstOrDefault(a => a.guid == guid && !string.IsNullOrEmpty(a.originalGuidBase64));
                if (config && originalEntry != null) originalGuid = Utf8.GetString(Convert.FromBase64String(originalEntry.originalGuidBase64));
                string encodedGuid = config ? Convert.ToBase64String(Utf8.GetBytes(originalGuid)) : null;
                entries.Add(new Entry { path = source, guid = originalGuid, package = Prefix + module.Key, configuration = config });
                index.Add(new Entry { path = source, guid = guid, originalGuidBase64 = encodedGuid });
                foreach (var alias in sourceIndexes.Where(a => a.guid == guid && a.path != source))
                    index.Add(new Entry { path = alias.path, guid = guid, originalGuidBase64 = encodedGuid });
            }
            // Folder GUIDs are package-scoped, avoiding collisions with remaining project folders.
            foreach (string root in module.Value)
            {
                string resolved = AdsCallerAssets.Resolve(root), physical = Physical(resolved);
                if (!Directory.Exists(physical)) continue;
                foreach (string sourceFolder in Directory.GetDirectories(physical, "*", SearchOption.AllDirectories).Concat(new[] { physical }))
                {
                    string source = resolved + sourceFolder.Substring(physical.Length).Replace('\\', '/');
                    string relative = source.StartsWith("Assets/AdsCaller/", StringComparison.Ordinal) ? source.Substring(17) : source.StartsWith("Assets/") ? source.Substring(7) : source.Substring(source.IndexOf('/', 9) + 1);
                    string target = "packages/" + Prefix + module.Key + "/" + relative;
                    if (!data.Keys.Any(k => k.StartsWith(target + "/", StringComparison.Ordinal)) || !File.Exists(sourceFolder + ".meta")) continue;
                    string folderGuid = Identity(target);
                    data[target + ".meta"] = Utf8.GetBytes("fileFormatVersion: 2\nguid: " + folderGuid + "\nfolderAsset: yes\n");
                    index.Add(new Entry { path = source, guid = folderGuid });
                }
            }
            string name = Prefix + module.Key;
            var deps = new Dictionary<string, string>();
            bool old = !Application.unityVersion.StartsWith("6000.", StringComparison.Ordinal);
            if (module.Key == "core") deps.Add("com.unity.ugui", "1.0.0");
            if (module.Key.StartsWith("main.") || module.Key == "cross-promo") deps.Add(Prefix + "core", version);
            // SDK snapshots are optional distribution files, not provider dependencies.
            // Official Assets/UPM imports expose the same assemblies without our package IDs.
            // SDK snapshots also accept an existing official EDM installation. UPM
            // cannot express alternative providers, so do not pin our EDM identity.
            if (module.Key == "cross-promo")
            {
                deps.Add("com.unity.addressables", old ? "1.29.0" : "3.1.0");
                deps.Add("com.unity.nuget.newtonsoft-json", "3.2.2");
            }
            string display = module.Key == "core" ? "Core" : module.Key == "cross-promo" ? "Cross Promo" : module.Key.Replace("main.", "").Replace("sdk.", "SDK ");
            string manifest = "{\n  \"name\": " + Json(name) + ",\n  \"version\": " + Json(version) + ",\n  \"displayName\": " + Json("Ads Caller — " + System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(display))
                + ",\n  \"unity\": " + Json(module.Key == "cross-promo" && !old ? "6000.0" : "2022.3") + ",\n  \"description\": \"Ads Caller package family. Use Ads Caller > Setup Wizard after installation.\",\n  \"author\": { \"name\": \"Sameer Khan\" },\n  \"dependencies\": {"
                + string.Join(",", deps.Select(d => "\n    " + Json(d.Key) + ": " + Json(d.Value))) + "\n  }\n}\n";
            string folder = "packages/" + name + "/";
            data.Add(folder + "package.json", Utf8.GetBytes(manifest));
            if (module.Key == "core") index.Add(new Entry { path = "Assets/AdsCaller/AdsCallerAssetIndex.json", guid = Identity(folder + "AdsCallerAssetIndex.json") });
            data.Add(folder + "AdsCallerAssetIndex.json", Utf8.GetBytes(JsonUtility.ToJson(new Index { assets = index.ToArray() }, true)));
            if (module.Key == "core") data.Add(folder + "Editor/Compatibility/AdsCaller.BuildCompatibility.asmref", Utf8.GetBytes("{\"reference\":\"OutBuildAutomator.Editor\"}"));
        }
        // Roots can be individual files, and relocation can create new parents. Source
        // directory enumeration misses those folders; immutable UPM installs cannot repair them.
        foreach (string key in data.Keys.Where(k => k.StartsWith("packages/", StringComparison.Ordinal)).ToArray())
        {
            int packageEnd = key.IndexOf('/', "packages/".Length);
            string folder = key.Substring(0, key.LastIndexOf('/'));
            while (folder.Length > packageEnd)
            {
                string metadata = folder + ".meta";
                if (!data.ContainsKey(metadata))
                    data[metadata] = Utf8.GetBytes("fileFormatVersion: 2\nguid: " + Identity(folder) + "\nfolderAsset: yes\n");
                folder = folder.Substring(0, folder.LastIndexOf('/'));
            }
        }
        // Package templates have stable private GUIDs; existing project configuration keeps its identity.
        foreach (var config in entries.Where(e => e.configuration))
        {
            string templateGuid = Identity("template/" + config.guid);
            foreach (string key in data.Keys.Where(k => k.EndsWith(".meta") || k.EndsWith(".asset") || k.EndsWith(".prefab") || k.EndsWith(".unity") || k.EndsWith(".json")).ToArray())
                data[key] = Utf8.GetBytes(Utf8.GetString(data[key]).Replace(config.guid, templateGuid));
        }
        foreach (string source in Expand(projectRoots))
        {
            if (!source.StartsWith("Assets/", StringComparison.Ordinal)) continue;
            if (excludedMedia.Contains(source)) continue;
            data["project/" + source] = ReadExportBytes(source, stripCatalogData, excludedMediaGuids);
            if (File.Exists(Physical(source) + ".meta")) data["project/" + source + ".meta"] = File.ReadAllBytes(Physical(source) + ".meta");
        }
        foreach (string key in data.Keys.Where(k => !k.EndsWith(".meta")).ToArray())
            if (!data.ContainsKey(key + ".meta")) data[key + ".meta"] = Utf8.GetBytes("fileFormatVersion: 2\nguid: " + Identity(key) + "\n");
        var delivery = new Delivery { unity = cross ? Application.unityVersion : "2022.3.0f1", online = online, packages = modules.Keys.Select(k => Prefix + k).ToArray(), assets = entries.ToArray() };
        data.Add("delivery.json", Utf8.GetBytes(JsonUtility.ToJson(delivery, true)));
        if (tarballs)
        {
            ExportTarballs(output, data, delivery, sdks);
            return;
        }
        byte[] payload;
        using (var memory = new MemoryStream())
        {
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                foreach (var file in data) using (var stream = zip.CreateEntry(file.Key, CompressionLevel.Optimal).Open()) stream.Write(file.Value, 0, file.Value.Length);
            payload = memory.ToArray();
        }
        string template = Physical(AdsCallerAssets.Resolve("Assets/AdsCaller/Scripts/Editor/Distribution/AdsCallerPackageInstaller.cs.txt"));
        var envelope = new Dictionary<string, byte[]> {
            { "Assets/AdsCallerInstaller/Editor/AdsCallerPackageInstaller.cs", File.ReadAllBytes(template) },
            { "Assets/AdsCallerInstaller/Editor/AdsCaller.Installer.Editor.asmdef", Utf8.GetBytes("{\"name\":\"AdsCaller.Installer.Editor\",\"includePlatforms\":[\"Editor\"],\"references\":[],\"overrideReferences\":true,\"precompiledReferences\":[]}") },
            { "Assets/AdsCallerInstaller/" + (online ?? "cross") + (cross ? "-cross" : "") + ".adscaller.bytes", payload }
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        using (var file = File.Create(output)) using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
        {
            foreach (var asset in envelope)
            {
                string id = Identity(asset.Key);
                Tar(gzip, id + "/pathname", Utf8.GetBytes(asset.Key));
                Tar(gzip, id + "/asset", asset.Value);
                Tar(gzip, id + "/asset.meta", Utf8.GetBytes("fileFormatVersion: 2\nguid: " + id + "\n"));
            }
            gzip.Write(new byte[1024], 0, 1024);
        }
        Debug.Log("[Ads Caller] UPM delivery exported: " + output);
    }

    [Serializable] private sealed class PackageManifest { public string name, version; }

    private static void ExportTarballs(string output, SortedDictionary<string, byte[]> data, Delivery delivery, bool sdks)
    {
        output = Path.GetFullPath(output);
        string parent = Path.GetDirectoryName(output);
        Directory.CreateDirectory(parent);
        if (File.Exists(output)) throw new IOException("The tarball output must be a folder: " + output);
        string staging = output + ".staging-" + Guid.NewGuid().ToString("N");
        string previous = null;
        Directory.CreateDirectory(staging);
        try
        {
            var instructions = new StringBuilder("# Ads Caller UPM tarballs\n\n");
            instructions.Append("Use this export with Unity " + delivery.unity + " or a compatible version.\n\n");
            instructions.Append("Import compatible official SDKs first: MAX for Amazon; MAX and Google Mobile Ads (including UMP) for Android. Use the vendors' Unity packages or supported UPM installation. Firebase Analytics is optional for Android. Keep the SDKs' External Dependency Manager installed. Ads Caller does not pin vendor SDK package names or versions.\n\n");
            instructions.Append("With official SDKs installed, no Ads Caller sdk.* tarball is required. SDK detection uses the installed SDK types and assemblies, without checking vendor package IDs, directories or version strings. Any EDM version compatible with your SDKs and Unity can be retained; Ads Caller imposes no EDM version requirement.\n\n");
            instructions.Append("Back up the destination project. Open Package Manager and choose + > Install package from tarball (Add package from tarball in some Unity versions). Install these Ads Caller modules in order, waiting for each operation to finish:\n\n");
            // Private dependencies must be registered before their dependents: there is no public registry for this family.
            string[] order = { "core", "sdk.edm", "sdk.max", "sdk.admob", "main.android", "main.amazon", "cross-promo" };
            int number = 0;
            var snapshots = new StringBuilder();
            foreach (string module in order.Select(s => Prefix + s).Where(delivery.packages.Contains))
            {
                string root = "packages/" + module + "/";
                var manifest = JsonUtility.FromJson<PackageManifest>(Utf8.GetString(data[root + "package.json"]));
                string filename = manifest.name + "-" + manifest.version + ".tgz";
                using (var file = File.Create(Path.Combine(staging, filename)))
                using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
                {
                    foreach (var item in data.Where(p => p.Key.StartsWith(root, StringComparison.Ordinal)))
                        Tar(gzip, "package/" + item.Key.Substring(root.Length), item.Value);
                    gzip.Write(new byte[1024], 0, 1024);
                }
                if (module.StartsWith(Prefix + "sdk.", StringComparison.Ordinal))
                    snapshots.Append("- `" + filename + "`\n");
                else instructions.Append(++number + ". `" + filename + "`\n");
            }
            if (snapshots.Length != 0)
                instructions.Append("\nOptional SDK snapshots (only for SDKs not already installed):\n\n" + snapshots
                    + "\nInstall these before Main only if needed. Keep one EDM installation: use your existing official EDM, or install the EDM snapshot first when none is present. MAX and AdMob snapshots do not require the Ads Caller EDM package. Never combine a snapshot with another copy of the same SDK.\n");
            var projectFiles = data.Where(p => p.Key.StartsWith("project/", StringComparison.Ordinal)).ToArray();
            if (projectFiles.Length != 0)
            {
                // These writable assets belong in Assets, not in an immutable registry/tarball package.
                using (var file = File.Create(Path.Combine(staging, "ProjectAssets.unitypackage")))
                using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
                {
                    foreach (var item in projectFiles.Where(p => !p.Key.EndsWith(".meta", StringComparison.Ordinal)))
                    {
                        string path = item.Key.Substring("project/".Length);
                        byte[] metadata = data[item.Key + ".meta"];
                        string guid = Regex.Match(Utf8.GetString(metadata), @"(?m)^guid: ([a-fA-F0-9]{32})").Groups[1].Value;
                        if (guid.Length != 32) throw new InvalidDataException("Missing asset GUID: " + path);
                        Tar(gzip, guid + "/pathname", Utf8.GetBytes(path));
                        Tar(gzip, guid + "/asset", item.Value);
                        Tar(gzip, guid + "/asset.meta", metadata);
                    }
                    gzip.Write(new byte[1024], 0, 1024);
                }
            }
            instructions.Append("\nUPM resolves public dependencies such as Addressables and Newtonsoft JSON from each package manifest. Internet access or cached packages is required. Existing incompatible dependency pins must be resolved in Package Manager. Keep these tarballs in a stable location and in source control when referenced by the project.\n\n");
            if (!sdks && !string.IsNullOrEmpty(delivery.online))
                instructions.Append("Network SDK bundling is disabled. Use your official SDK installation; no Ads Caller sdk.* tarball is required.\n\n");
            instructions.Append("Package Manager can install these modules while game scripts contain compilation errors, including in Safe Mode. Setup Wizard, scene editing and Play Mode require compilation to succeed.\n\n");
            if (projectFiles.Length != 0)
                instructions.Append("After compilation succeeds, optionally import ProjectAssets.unitypackage for the exported demos and SDK/Addressables settings. Review the import list and preserve existing project settings. It contains assets directly, with no C# installation bootstrap.\n\n");
            instructions.Append("Then open Ads Caller > Setup Wizard. Review application identifiers and rebuild Addressables content for the destination application.\n\nDo not install beside legacy Ads Caller scripts or another copy of the same SDK. Use the migration installer for legacy projects; these tarballs do not migrate or archive Assets. Install only one Main provider (Android or Amazon).\n\nAll module versions and private dependencies use the version entered in Package Exporter (2.5 becomes UPM version 2.5.0).\n");
            File.WriteAllText(Path.Combine(staging, "README.md"), instructions.ToString(), Utf8);
            if (Directory.Exists(output))
            {
                previous = output + ".previous-" + Guid.NewGuid().ToString("N");
                Directory.Move(output, previous);
            }
            try { Directory.Move(staging, output); }
            catch
            {
                if (previous != null) Directory.Move(previous, output);
                throw;
            }
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        Debug.Log("[Ads Caller] UPM tarballs exported: " + output);
    }

    private static void Tar(Stream stream, string name, byte[] bytes)
    {
        // POSIX extended headers preserve long and Unicode package paths without truncation.
        if (Utf8.GetByteCount(name) > 100 || name.Any(c => c > 127))
        {
            string record = " path=" + name + "\n";
            int length = Utf8.GetByteCount(record) + 1;
            while (length != Utf8.GetByteCount(record) + length.ToString(System.Globalization.CultureInfo.InvariantCulture).Length)
                length = Utf8.GetByteCount(record) + length.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
            TarEntry(stream, "PaxHeader", Utf8.GetBytes(length.ToString(System.Globalization.CultureInfo.InvariantCulture) + record), 'x');
            name = "entry";
        }
        TarEntry(stream, name, bytes, '0');
    }

    private static void TarEntry(Stream stream, string name, byte[] bytes, char type)
    {
        byte[] header = new byte[512];
        Action<int, int, string> field = (offset, count, value) => Encoding.ASCII.GetBytes(value).Take(count).ToArray().CopyTo(header, offset);
        field(0, 100, name); field(100, 8, "0000644\0"); field(108, 8, "0000000\0"); field(116, 8, "0000000\0");
        field(124, 12, Convert.ToString(bytes.LongLength, 8).PadLeft(11, '0') + "\0"); field(136, 12, "00000000000\0");
        field(148, 8, "        "); header[156] = (byte)type; field(257, 6, "ustar\0"); field(263, 2, "00");
        field(148, 8, Convert.ToString(header.Sum(b => (int)b), 8).PadLeft(6, '0') + "\0 ");
        stream.Write(header, 0, 512); stream.Write(bytes, 0, bytes.Length);
        int padding = (512 - bytes.Length % 512) % 512; stream.Write(new byte[padding], 0, padding);
    }
}
