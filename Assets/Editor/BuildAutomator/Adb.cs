using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OutBuild
{
    // No Unity dependencies: command execution and parsing can be tested without an Editor.
    public sealed class AdbResult
    {
        public int ExitCode = -1;
        public string Output = "", Error = "";
        public bool TimedOut, Cancelled;
        public bool Success { get { return ExitCode == 0 && !TimedOut && !Cancelled && !Adb.HasError(Output + "\n" + Error); } }
        public string Detail { get { return Cancelled ? "Cancelled." : TimedOut ? "ADB timed out. Check the device connection and retry." : (Output + "\n" + Error).Trim(); } }
    }

    [Serializable]
    public sealed class Device
    {
        public string Id = "", Name = "", State = "", Transport = "", HardwareId = "";
        public bool Ready { get { return State == "device"; } }
        public string Hint
        {
            get
            {
                switch (State)
                {
                    case "device": return "Ready";
                    case "unauthorized": return "Unlock device and approve this computer's debugging request.";
                    case "offline": return "Reconnect cable or Wi-Fi; check debugging is enabled.";
                    case "no permissions": return "Check USB permissions/driver on this computer.";
                    case "saved": return "Saved device, currently unavailable.";
                    default: return State;
                }
            }
        }
    }

    public sealed class WirelessService
    {
        public string Name, Endpoint, ServiceType;
        public bool Pairing;
        public string ConnectTarget { get { return Name + "." + ServiceType.TrimEnd('.'); } }
        public bool MatchesTransport(string transport)
        {
            return !Pairing && (string.Equals(transport, Endpoint, StringComparison.OrdinalIgnoreCase) ||
                string.Equals((transport ?? "").TrimEnd('.'), ConnectTarget, StringComparison.OrdinalIgnoreCase));
        }
    }

    // Retry each address independently: a new advertised port must not inherit an old port's cooldown.
    public sealed class WirelessReconnect
    {
        private readonly Dictionary<string, int> failures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> retryAfter = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public bool CanAttempt(string target, double now)
        {
            double next;
            return !retryAfter.TryGetValue(target, out next) || now >= next;
        }

        public void Clear() { failures.Clear(); retryAfter.Clear(); }
        public void Reset(IEnumerable<string> targets)
        {
            foreach (string target in targets) { failures.Remove(target); retryAfter.Remove(target); }
        }

        public static List<string> Targets(IEnumerable<WirelessService> advertised, IEnumerable<string> knownTransports)
        {
            var services = advertised.Where(service => !service.Pairing).ToArray();
            // Prefer the stable service name. The current resolved endpoint is only a fallback;
            // neither the device registry nor the user has to track a changing IP or port.
            if (services.Length > 0)
                return services.SelectMany(service => new[] { service.ConnectTarget, service.Endpoint })
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var known = knownTransports.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            string endpoint;
            string address = known.FirstOrDefault(value => Adb.TryEndpoint(value, out endpoint, 0));
            string mdns = known.FirstOrDefault(value => value.EndsWith("._adb-tls-connect._tcp", StringComparison.OrdinalIgnoreCase));
            return new[] { address, mdns }.Where(value => value != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<bool> TryConnect(IEnumerable<string> candidates, Func<string, Task<AdbResult>> connect,
            Func<Task<bool>> ready, Func<double> now, CancellationToken token)
        {
            var targets = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (string target in targets)
            {
                token.ThrowIfCancellationRequested();
                if (!CanAttempt(target, now())) continue;
                var result = await connect(target);
                token.ThrowIfCancellationRequested();
                if (result.Cancelled) throw new OperationCanceledException();
                // ADB may return exit code 0 / "already connected" for an unusable transport.
                // Only the verified device state counts, and failure must still try the next target.
                if (await ready()) { Reset(targets); return true; }
                token.ThrowIfCancellationRequested();
                int count;
                failures.TryGetValue(target, out count);
                failures[target] = count = Math.Min(count + 1, 4);
                retryAfter[target] = now() + Math.Min(30, 2 << count);
            }
            return false;
        }
    }

    public static class Adb
    {
        private const int OutputLimit = 128 * 1024;

        public static bool HasError(string text)
        {
            return Regex.IsMatch(text ?? "", @"(?im)^\s*(error:|adb:.*error|failed\b|failure\b|cannot\b|unable\b|.*(?:INSTALL_FAILED_|SecurityException|Error type 3))");
        }

        public static string Quote(string value)
        {
            if (value == null || value.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0)
                throw new ArgumentException("Invalid command argument.");
            // ProcessStartInfo on Windows/Mono uses backslash escaping, not shell escaping.
            return "\"" + Regex.Replace(value, @"(\\*)\""", "$1$1\\\"")
                .TrimEnd('\\') + new string('\\', value.Reverse().TakeWhile(c => c == '\\').Count() * 2) + "\"";
        }

        public static bool TryEndpoint(string input, out string endpoint, int defaultPort = 5555)
        {
            endpoint = "";
            string value = (input ?? "").Trim();
            if (value.Length == 0 || value.Length > 255) return false;
            string host;
            int port = defaultPort;
            if (value.StartsWith("[", StringComparison.Ordinal))
            {
                int close = value.IndexOf(']');
                IPAddress ip;
                if (close < 2 || !IPAddress.TryParse(value.Substring(1, close - 1), out ip)) return false;
                host = value.Substring(0, close + 1);
                if (close + 1 < value.Length && (value[close + 1] != ':' || !int.TryParse(value.Substring(close + 2), out port))) return false;
            }
            else
            {
                string[] parts = value.Split(':');
                if (parts.Length > 2 || !Regex.IsMatch(parts[0], @"^[a-zA-Z0-9][a-zA-Z0-9.\-]*$")) return false;
                host = parts[0];
                if (parts.Length == 2 && !int.TryParse(parts[1], out port)) return false;
            }
            if (port < 1 || port > 65535) return false;
            endpoint = host + ":" + port;
            return true;
        }

        public static List<Device> ParseDevices(string output)
        {
            var devices = new List<Device>();
            foreach (string line in (output ?? "").Split('\n'))
            {
                var parts = Regex.Split(line.Trim(), @"\s+");
                if (parts.Length < 2 || parts[0] == "List" || parts[0].StartsWith("*")) continue;
                string state = parts[1];
                if (state == "no" && parts.Length > 2 && parts[2] == "permissions") state = "no permissions";
                if (!new[] { "device", "offline", "unauthorized", "no permissions", "recovery", "sideload", "bootloader", "rescue", "connecting" }.Contains(state)) continue;
                string model = parts.FirstOrDefault(p => p.StartsWith("model:", StringComparison.Ordinal));
                string id = parts[0];
                devices.Add(new Device
                {
                    Id = id, Name = model == null ? id : model.Substring(6).Replace('_', ' '), State = state,
                    Transport = id.StartsWith("emulator-") ? "Emulator" : id.Contains(":") || id.Contains("._tcp") ? "Wi-Fi" : "USB"
                });
            }
            return devices.GroupBy(d => d.Id).Select(g => g.First()).ToList();
        }

        public static List<WirelessService> ParseServices(string output)
        {
            var services = new List<WirelessService>();
            foreach (string line in (output ?? "").Split('\n'))
            {
                var parts = Regex.Split(line.Trim(), @"\s+");
                if (parts.Length < 3) continue;
                bool pairing = parts[1].Contains("_adb-tls-pairing._tcp");
                if (!pairing && !parts[1].Contains("_adb-tls-connect._tcp")) continue;
                string endpoint;
                if (TryEndpoint(parts[2], out endpoint, 0))
                    services.Add(new WirelessService
                    {
                        Name = parts[0], Endpoint = endpoint, Pairing = pairing,
                        ServiceType = pairing ? "_adb-tls-pairing._tcp" : "_adb-tls-connect._tcp"
                    });
            }
            return services;
        }

        public static Task<AdbResult> Run(string executable, CancellationToken token, int timeoutMs, params string[] args)
        {
            // Never log arguments: adb pair contains a short-lived secret.
            return Task.Run(() =>
            {
                var result = new AdbResult();
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                using (var process = new Process())
                {
                    try
                    {
                        if (token.IsCancellationRequested) { result.Cancelled = true; return result; }
                        process.StartInfo = new ProcessStartInfo(executable, string.Join(" ", args.Select(Quote)))
                        {
                            UseShellExecute = false, CreateNoWindow = true,
                            RedirectStandardOutput = true, RedirectStandardError = true
                        };
                        process.OutputDataReceived += (s, e) => Append(stdout, e.Data);
                        process.ErrorDataReceived += (s, e) => Append(stderr, e.Data);
                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        var watch = Stopwatch.StartNew();
                        while (!process.WaitForExit(100))
                        {
                            if (!token.IsCancellationRequested && watch.ElapsedMilliseconds < timeoutMs) continue;
                            result.Cancelled = token.IsCancellationRequested;
                            result.TimedOut = !result.Cancelled;
                            try { process.Kill(); } catch (InvalidOperationException) { }
                            process.WaitForExit(2000);
                            return result;
                        }
                        process.WaitForExit(); // Flush the asynchronous output events after exit.
                        result.ExitCode = process.ExitCode;
                        lock (stdout) result.Output = stdout.ToString();
                        lock (stderr) result.Error = stderr.ToString();
                    }
                    catch (Exception e) { result.Error = "Unable to execute ADB: " + e.GetType().Name + ". Check the executable path and permissions."; }
                }
                return result;
            });
        }

        private static void Append(StringBuilder buffer, string line)
        {
            if (line == null) return;
            lock (buffer)
            {
                if (buffer.Length < OutputLimit) buffer.AppendLine(line.Substring(0, Math.Min(line.Length, OutputLimit - buffer.Length)));
            }
        }

        public static async Task Require(string path, CancellationToken token, int timeout, params string[] args)
        {
            var result = await Run(path, token, timeout, args);
            if (result.Cancelled) throw new OperationCanceledException();
            if (!result.Success) throw new InvalidOperationException(result.Detail);
        }

        public static async Task Deploy(string path, string serial, string apk, string package, int minimumSdk, string[] abis,
            CancellationToken token, Action<string> status)
        {
            if (!File.Exists(apk) || !string.Equals(Path.GetExtension(apk), ".apk", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Select an existing APK. AAB files cannot be installed directly.");
            if (!Regex.IsMatch(package ?? "", @"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$"))
                throw new InvalidOperationException("Invalid Android application identifier.");
            status("Checking " + serial);
            var state = await Run(path, token, 10000, "-s", serial, "get-state");
            if (!state.Success || state.Output.Trim() != "device")
                throw new InvalidOperationException("Device is not ready. Refresh Devices, approve authorization, or reconnect. " + state.Detail);
            var sdk = await Run(path, token, 10000, "-s", serial, "shell", "getprop", "ro.build.version.sdk");
            int api;
            if (sdk.Success && int.TryParse(sdk.Output.Trim(), out api) && api < minimumSdk)
                throw new InvalidOperationException("Device API " + api + " is below this build's minimum API " + minimumSdk + ".");
            var supported = await Run(path, token, 10000, "-s", serial, "shell", "getprop", "ro.product.cpu.abilist");
            if (supported.Success && !string.IsNullOrWhiteSpace(supported.Output) && abis != null && abis.Length > 0 &&
                !supported.Output.Trim().Split(',').Intersect(abis).Any())
                throw new InvalidOperationException("Device CPU architecture does not match this APK. Check Android Target Architectures.");
            token.ThrowIfCancellationRequested();
            status("Installing on " + serial);
            var install = await Run(path, token, 180000, "-s", serial, "install", "-r", apk);
            if (install.Cancelled) throw new OperationCanceledException();
            if (!install.Success || !Regex.IsMatch(install.Output, @"(?m)^Success\s*$"))
                throw new InvalidOperationException("Installation failed. " + FriendlyInstallError(install.Detail));
            token.ThrowIfCancellationRequested();
            status("Launching on " + serial);
            // Resolve the launcher so Android TV's LEANBACK_LAUNCHER works as well as phones.
            string component = "";
            foreach (string category in new[] { "android.intent.category.LAUNCHER", "android.intent.category.LEANBACK_LAUNCHER" })
            {
                var resolve = await Run(path, token, 10000, "-s", serial, "shell", "cmd", "package", "resolve-activity", "--brief", "-a", "android.intent.action.MAIN", "-c", category, package);
                if (resolve.Cancelled) throw new OperationCanceledException();
                if (!resolve.Success) continue;
                component = resolve.Output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => Regex.IsMatch(l, @"^[\w.]+/[\w.$]+$")) ?? "";
                if (component.Length > 0) break;
            }
            if (component.Length == 0) throw new InvalidOperationException("APK installed, but no phone/TV launcher activity was found.");
            var launch = await Run(path, token, 15000, "-s", serial, "shell", "am", "start", "-W", "-n", component);
            if (launch.Cancelled) throw new OperationCanceledException();
            if (!launch.Success || !Regex.IsMatch(launch.Output, @"(?im)^Status:\s*ok\s*$"))
                throw new InvalidOperationException("APK installed, but launch was not confirmed. " + launch.Detail);
        }

        public static string FriendlyInstallError(string detail)
        {
            if (detail.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE")) return "Signing key mismatch. Use the original key, or explicitly uninstall the app (deletes its data).\n" + detail;
            if (detail.Contains("INSTALL_FAILED_VERSION_DOWNGRADE")) return "Installed version is newer. Increase the version code; no automatic downgrade was attempted.\n" + detail;
            if (detail.Contains("INSTALL_FAILED_INSUFFICIENT_STORAGE")) return "Free storage on the device and retry Install Last Build.\n" + detail;
            return detail;
        }
    }
}
