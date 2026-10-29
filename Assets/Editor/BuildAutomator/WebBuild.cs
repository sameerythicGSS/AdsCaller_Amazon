using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEditor;

namespace OutBuild
{
    [Serializable]
    public sealed class WebBuildSettings
    {
        public bool overrideSettings;
        public WebGLCompressionFormat compression = WebGLCompressionFormat.Gzip;
        public bool fallback, cache = true, hashedNames = true;
        public WebGLExceptionSupport exceptions = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
        public float budgetMB = 30;

        public static WebBuildSettings Capture()
        {
            return new WebBuildSettings {
                compression = PlayerSettings.WebGL.compressionFormat,
                fallback = PlayerSettings.WebGL.decompressionFallback,
                cache = PlayerSettings.WebGL.dataCaching,
                hashedNames = PlayerSettings.WebGL.nameFilesAsHashes,
                exceptions = PlayerSettings.WebGL.exceptionSupport
            };
        }
        public void Apply()
        {
            PlayerSettings.WebGL.compressionFormat = compression;
            PlayerSettings.WebGL.decompressionFallback = fallback;
            PlayerSettings.WebGL.dataCaching = cache;
            PlayerSettings.WebGL.nameFilesAsHashes = hashedNames;
            PlayerSettings.WebGL.exceptionSupport = exceptions;
        }
        public void Recipe(int recipe)
        {
            overrideSettings = true;
            compression = recipe == 0 ? WebGLCompressionFormat.Disabled : recipe == 3 ? WebGLCompressionFormat.Brotli : WebGLCompressionFormat.Gzip;
            fallback = recipe == 1;
            cache = recipe != 0;
            hashedNames = recipe != 0;
        }
    }

    public sealed class WebFile
    {
        public string name;
        public long bytes;
    }
    public sealed class WebAnalysis
    {
        public string root;
        public long bytes, payloadBytes;
        public List<WebFile> files = new List<WebFile>();
        public List<string> warnings = new List<string>();
    }

    // File and HTTP policy are shared by analysis, packaging and the preview server.
    public static class WebArtifacts
    {
        public static string Root(string path)
        {
            string root = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!File.Exists(Path.Combine(root, "index.html"))) throw new IOException("Choose a WebGL build folder containing index.html.");
            RejectLinks(root);
            return root;
        }
        private static void RejectLinks(string path)
        {
            for (var info = new DirectoryInfo(path); info != null; info = info.Parent)
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked build folders are not supported.");
        }
        public static string Resolve(string root, string request)
        {
            string relative = Uri.UnescapeDataString(request.Split('?')[0]).TrimStart('/');
            if (relative.Length == 0) relative = "index.html";
            if (relative.Contains('\\') || relative.Contains(':') || relative.Contains('\0')) throw new IOException("Invalid path.");
            var parts = relative.Split('/');
            if (parts.Any(p => p.StartsWith(".", StringComparison.Ordinal))) throw new IOException("Invalid path.");
            string current = root;
            foreach (string part in parts)
            {
                current = Path.Combine(current, part);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked files are not served.");
            }
            if (!File.Exists(current)) throw new FileNotFoundException();
            return current;
        }
        private static IEnumerable<string> Files(string root, CancellationToken token)
        {
            foreach (string file in Directory.GetFiles(root))
            {
                token.ThrowIfCancellationRequested();
                if (Path.GetFileName(file).StartsWith(".", StringComparison.Ordinal)) continue;
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked files are not supported.");
                yield return file;
            }
            foreach (string directory in Directory.GetDirectories(root))
            {
                token.ThrowIfCancellationRequested();
                if (Path.GetFileName(directory).StartsWith(".", StringComparison.Ordinal)) continue;
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked folders are not supported.");
                foreach (string file in Files(directory, token)) yield return file;
            }
        }
        public static WebAnalysis Analyze(string path, CancellationToken token)
        {
            string root = Root(path);
            var analysis = new WebAnalysis { root = root };
            foreach (string file in Files(root, token))
            {
                var item = new WebFile { name = file.Substring(root.Length + 1).Replace('\\', '/'), bytes = new FileInfo(file).Length };
                analysis.files.Add(item); analysis.bytes += item.bytes;
                if (item.name.StartsWith("Build/", StringComparison.OrdinalIgnoreCase)) analysis.payloadBytes += item.bytes;
            }
            analysis.files = analysis.files.OrderByDescending(f => f.bytes).ToList();
            if (!analysis.files.Any(f => f.name.Contains(".wasm"))) analysis.warnings.Add("No WebAssembly file found. Check whether the template uses remote files.");
            if (!analysis.files.Any(f => f.name.Contains(".data"))) analysis.warnings.Add("No local data payload found. Remote content is excluded from the size estimate.");
            if (analysis.files.Any(f => f.name.EndsWith(".br", StringComparison.OrdinalIgnoreCase))) analysis.warnings.Add("Brotli requires Content-Encoding: br and HTTPS for browser compatibility. Loopback preview uses HTTP; use Gzip for local testing.");
            if (analysis.files.Any(f => f.name.EndsWith(".unityweb", StringComparison.OrdinalIgnoreCase))) analysis.warnings.Add("Fallback files use Unity's JavaScript decompressor. Do not add Content-Encoding to .unityweb files.");
            if (analysis.files.Any(f => f.name.Contains(".symbols") || f.name.EndsWith(".map"))) analysis.warnings.Add("Debug symbols are included. Review before sharing this release.");
            return analysis;
        }
        public static string Encoding(string file)
        {
            if (file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) return "gzip";
            if (file.EndsWith(".br", StringComparison.OrdinalIgnoreCase)) return "br";
            return "";
        }
        public static string Mime(string file)
        {
            string name = file.ToLowerInvariant();
            if (Encoding(name).Length > 0) name = Path.GetFileNameWithoutExtension(name);
            if (name.EndsWith(".unityweb")) return "application/octet-stream";
            switch (Path.GetExtension(name))
            {
                case ".wasm": return "application/wasm";
                case ".js": return "application/javascript";
                case ".html": return "text/html; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".json": return "application/json";
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".svg": return "image/svg+xml";
                case ".ico": return "image/x-icon";
                case ".txt": return "text/plain; charset=utf-8";
                case ".mp4": return "video/mp4";
                case ".ogg": return "audio/ogg";
                default: return "application/octet-stream";
            }
        }
        private static string HeaderRules(WebAnalysis analysis)
        {
            var text = new StringBuilder("# Generated from actual artifacts. Deploy beside index.html on a host supporting _headers.\n/*\n  Cache-Control: no-cache\n");
            foreach (var file in analysis.files)
            {
                string encoding = Encoding(file.name);
                if (encoding.Length == 0 && Mime(file.name) != "application/wasm") continue;
                text.Append('/').Append(file.name).Append("\n  Content-Type: ").Append(Mime(file.name)).Append('\n');
                if (encoding.Length > 0) text.Append("  Content-Encoding: ").Append(encoding).Append('\n');
            }
            return text.ToString();
        }
        private static string NginxRules()
        {
            return "# Include inside your HTTPS server block; set root to the extracted build folder.\n" +
                "# Review with nginx -t. Explicit types apply after removing the compression suffix.\n" +
                "gzip off;\nlocation / { try_files $uri $uri/ =404; add_header Cache-Control \"no-cache\"; }\n" +
                "location ~ \\.wasm$ { default_type application/wasm; add_header Cache-Control \"no-cache\"; }\n" +
                "location ~ \\.unityweb$ { default_type application/octet-stream; add_header Cache-Control \"no-cache\"; }\n" +
                CompressedLocation("gz", "gzip") + CompressedLocation("br", "br");
        }
        private static string CompressedLocation(string suffix, string encoding)
        {
            var text = new StringBuilder();
            foreach (var type in new[] { new[] { "js", "application/javascript" }, new[] { "wasm", "application/wasm" }, new[] { "data", "application/octet-stream" }, new[] { "symbols\\.json", "application/json" } })
                text.Append(@"location ~ \.").Append(type[0]).Append(@"\.").Append(suffix).Append("$ {\n  types {}\n  default_type ").Append(type[1])
                    .Append(";\n  add_header Content-Encoding ").Append(encoding).Append(";\n  add_header Cache-Control \"no-cache\";\n}\n");
            return text.ToString();
        }
        public static Dictionary<string, string> HostingKit(WebAnalysis analysis)
        {
            return new Dictionary<string, string> {
                { "_headers", HeaderRules(analysis) },
                { "nginx-webgl.conf", NginxRules() },
                { "DEPLOY.txt", "WEBGL RELEASE CHECKLIST\n\nUpload the entire build with index.html at the site root (or preserve all relative URLs in a subfolder).\nServe .wasm with application/wasm; .js with application/javascript. Compressed .gz/.br need gzip/br Content-Encoding. Do not recompress files.\nFallback .unityweb files intentionally have no Content-Encoding. Use HTTPS for production and Brotli.\n_headers works only on hosts that support this convention; verify your provider's documentation. nginx-webgl.conf is a reviewed-by-you server snippet, not an automatic deployment.\nFor hosts without custom response headers (including GitHub Pages), choose Portable host or disable compression and rebuild.\nFor itch.io upload the ZIP, choose HTML and confirm that index.html is at its root. Host limits and iframe behavior still apply.\nUse no-cache for index.html; immutable caching is safe only for proven content-hashed files.\nIf using threaded WebAssembly, configure COOP: same-origin and COEP: require-corp on your HTTPS host and verify all external resources support isolation.\nTest a cold load, refresh after updating, fullscreen, audio after interaction, save data, remote asset CORS, and intended browsers/devices.\nPreview cannot certify production headers, runtime memory usage or mobile compatibility. Size estimates exclude remote content and are not measured loading times.\n\nSources:\nhttps://docs.unity3d.com/2022.3/Documentation/Manual/webgl-deploying.html\nhttps://docs.unity3d.com/2022.3/Documentation/Manual/webgl-server-configuration-code-samples.html\n" }
            };
        }
        public static void ExportZip(string root, string destination, CancellationToken token)
        {
            var analysis = Analyze(root, token);
            destination = Path.GetFullPath(destination);
            if (destination.StartsWith(analysis.root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Save the ZIP outside the build folder.");
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
                {
                    foreach (var file in analysis.files)
                    {
                        token.ThrowIfCancellationRequested();
                        // Compressed Unity payloads should not be compressed a second time.
                        var entry = zip.CreateEntry(file.name, Encoding(file.name).Length > 0 || file.name.EndsWith(".unityweb") ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                        using (var input = File.OpenRead(Path.Combine(analysis.root, file.name)))
                        using (var output = entry.Open())
                        {
                            byte[] buffer = new byte[65536]; int count;
                            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                token.ThrowIfCancellationRequested(); output.Write(buffer, 0, count);
                            }
                        }
                    }
                }
                token.ThrowIfCancellationRequested();
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
