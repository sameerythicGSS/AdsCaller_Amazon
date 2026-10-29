using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OutBuild;

// This executable doubles as a deterministic fake adb child process. No hardware or Unity needed.
internal static class Program
{
    private static int passed;
    private static string Self { get { return Assembly.GetExecutingAssembly().Location; } }
    private static int Main(string[] args)
    {
        if (args.Length > 0) return FakeAdb(args);
        try { Tests().GetAwaiter().GetResult(); Console.WriteLine("PASS: " + passed + " checks"); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new Exception("FAIL: " + label);
        passed++;
        Console.WriteLine("PASS: " + label);
    }

    private static async Task Fails(Func<Task> action, string contains, string label)
    {
        try { await action(); }
        catch (Exception e) { Check(e.Message.Contains(contains), label); return; }
        throw new Exception("FAIL: expected exception: " + label);
    }

    private static async Task SchedulerTests()
    {
        var scheduler = new OperationScheduler();
        var started = new TaskCompletionSource<bool>();
        bool discoveryCancelled = false, userRan = false;
        var background = scheduler.Run(async token =>
        {
            started.SetResult(true);
            try { await Task.Delay(10000, token); }
            catch (OperationCanceledException) { discoveryCancelled = true; throw; }
        }, true);
        await started.Task;
        Check(scheduler.Quiet && !scheduler.ForegroundBusy, "discovery leaves foreground actions available");
        var user = scheduler.Run(token => { userRan = true; return Task.CompletedTask; });
        try { await background; } catch (OperationCanceledException) { }
        await user;
        Check(discoveryCancelled && userRan && !scheduler.Busy, "explicit action preempts discovery and executes exactly once");
        var release = new TaskCompletionSource<bool>();
        int concurrent = 0, maximum = 0, completed = 0;
        Func<CancellationToken, Task> operation = async token =>
        {
            maximum = Math.Max(maximum, ++concurrent);
            await release.Task;
            concurrent--; completed++;
        };
        var first = scheduler.Run(operation);
        var second = scheduler.Run(operation);
        bool quietRan = false;
        await scheduler.Run(token => { quietRan = true; return Task.CompletedTask; }, true);
        Check(scheduler.ForegroundBusy && !quietRan, "discovery cannot jump ahead of queued foreground work");
        release.SetResult(true);
        await Task.WhenAll(first, second);
        Check(maximum == 1 && completed == 2 && !scheduler.Busy, "foreground operations serialize without dropping either action");
        try { await scheduler.Run(token => { throw new IOException("fixture"); }); } catch (IOException) { }
        await scheduler.Run(token => Task.CompletedTask);
        Check(!scheduler.Busy, "failed action releases scheduler for the next request");
        scheduler.Shutdown();
        bool rejected = false;
        try { await scheduler.Run(token => Task.CompletedTask); } catch (OperationCanceledException) { rejected = true; }
        Check(rejected && !scheduler.Busy, "shutdown prevents work from starting after assembly reload");
    }

    private static async Task Tests()
    {
        await SchedulerTests();
        var devices = Adb.ParseDevices("List of devices attached\r\nUSB123 device product:p model:Pixel_8 transport_id:1\r\nadb-abc._adb-tls-connect._tcp device model:TV\r\n192.168.1.3:5555 unauthorized\r\nemulator-5554 offline\r\nUSB456 no permissions (missing udev rules)\r\n* daemon started successfully *\r\n");
        Check(devices.Count == 5, "all ADB transports and non-ready states retained");
        Check(devices[0].Name == "Pixel 8" && devices[0].Transport == "USB", "USB model parsed");
        Check(devices[1].Ready && devices[1].Transport == "Wi-Fi", "mDNS connection retained");
        Check(!devices[2].Ready && devices[2].Hint.Contains("approve"), "authorization guidance");
        Check(devices[3].Transport == "Emulator" && !devices[3].Ready, "offline emulator");
        Check(devices[4].State == "no permissions", "Linux permissions guidance");
        Check(Adb.ParseDevices("\nList of devices attached\ninvalid\n").Count == 0, "empty/malformed discovery");
        Check(Adb.ParseDevices("USB device\nUSB device").Count == 1, "duplicate transport removed");
        string endpoint;
        Check(Adb.TryEndpoint("192.168.1.4", out endpoint) && endpoint == "192.168.1.4:5555", "Fire TV default port");
        Check(Adb.TryEndpoint("host.local:37123", out endpoint), "hostname endpoint");
        Check(Adb.TryEndpoint("[fe80::1]:37123", out endpoint), "IPv6 endpoint");
        Check(!Adb.TryEndpoint("host:0", out endpoint) && !Adb.TryEndpoint("host:65536", out endpoint), "port bounds");
        Check(!Adb.TryEndpoint("host", out endpoint, 0), "pairing requires explicit port");
        Check(!Adb.TryEndpoint("host;echo secret", out endpoint) && !Adb.TryEndpoint("-s:5555", out endpoint), "reject shell-like endpoint and option injection");
        var services = Adb.ParseServices("List of discovered mdns services\nmyphone _adb-tls-pairing._tcp. 192.168.1.2:12345\nmyphone _adb-tls-connect._tcp. 192.168.1.2:54321");
        Check(services.Count == 2, "pairing and connection service discovery");
        Check(services[1].MatchesTransport("myphone._adb-tls-connect._tcp."), "manual IP connection recognizes its ready mDNS transport");
        Check(services[1].MatchesTransport("192.168.1.2:54321"), "wireless service matches the exact ready IP transport");
        Check(!services[0].MatchesTransport("192.168.1.2:12345") && !services[1].MatchesTransport("unrelated._adb-tls-connect._tcp"), "pairing and unrelated transports cannot confirm a connection");
        Check(services[1].ConnectTarget == "myphone._adb-tls-connect._tcp", "mDNS connection target preserves the service identity");
        Check(Adb.HasError("Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE]") && !Adb.HasError("Success\n"), "semantic ADB failure detection");

        var advertisedTargets = WirelessReconnect.Targets(services, new string[0]);
        Check(advertisedTargets.SequenceEqual(new[] { services[1].ConnectTarget, services[1].Endpoint }),
            "advertised device connects by stable service identity before its temporary endpoint");

        var known = new[] { "192.168.1.2:54321", "USB123", "myphone._adb-tls-connect._tcp", "192.168.1.2:11111" };
        var targets = WirelessReconnect.Targets(new WirelessService[0], known);
        Check(targets.SequenceEqual(new[] { known[0], known[2] }), "empty mDNS falls back to the most recent saved address and service");
        targets = WirelessReconnect.Targets(services.Where(service => service.Pairing), known);
        Check(targets.SequenceEqual(new[] { known[0], known[2] }), "pairing-only discovery still uses saved connection targets");
        var changedPort = new WirelessService { Name = "myphone", Endpoint = "192.168.1.2:60000", ServiceType = "_adb-tls-connect._tcp" };
        targets = WirelessReconnect.Targets(new[] { changedPort }, known);
        Check(targets.SequenceEqual(new[] { changedPort.ConnectTarget, changedPort.Endpoint }), "fresh advertisement replaces stale saved addresses without depending on its port");
        Check(WirelessReconnect.Targets(new WirelessService[0], new[] { "USB123", "emulator-5554" }).Count == 0, "USB and emulator serials are never network retry targets");
        Check(WirelessReconnect.Targets(new WirelessService[0], new[] { "[fe80::1]:5555" }).Single() == "[fe80::1]:5555", "saved IPv6 fallback retained");

        var retry = new WirelessReconnect();
        double now = 0;
        var attempted = new List<string>();
        Func<string, Task<AdbResult>> falseSuccess = target =>
        {
            attempted.Add(target);
            return Task.FromResult(new AdbResult { ExitCode = 0, Output = "already connected to " + target });
        };
        bool recovered = await retry.TryConnect(new[] { "stale", "fresh" }, falseSuccess,
            () => Task.FromResult(attempted.Last() == "fresh"), () => now, CancellationToken.None);
        Check(recovered && attempted.SequenceEqual(new[] { "stale", "fresh" }), "already-connected but offline continues to a verified fallback");
        attempted.Clear();
        recovered = await retry.TryConnect(new[] { "stale" }, falseSuccess, () => Task.FromResult(false), () => now, CancellationToken.None);
        Check(!recovered && !retry.CanAttempt("stale", now), "unverified success is a failure and starts cooldown");
        attempted.Clear();
        recovered = await retry.TryConnect(new[] { "stale", "new-port" }, falseSuccess,
            () => Task.FromResult(true), () => now, CancellationToken.None);
        Check(recovered && attempted.SequenceEqual(new[] { "new-port" }), "new port retries immediately while old port is in cooldown");
        Check(retry.CanAttempt("new-port", now), "successful recovery clears its failure history");
        attempted.Clear();
        await retry.TryConnect(new[] { "down" }, falseSuccess, () => Task.FromResult(false), () => now, CancellationToken.None);
        await retry.TryConnect(new[] { "down" }, falseSuccess, () => Task.FromResult(false), () => now, CancellationToken.None);
        Check(attempted.Count == 1, "unchanged unavailable endpoint is not hammered during cooldown");
        now = 4;
        Check(retry.CanAttempt("down", now), "transient failure becomes eligible without toggling wireless debugging");
        for (int i = 0; i < 8; i++)
        {
            await retry.TryConnect(new[] { "down" }, falseSuccess, () => Task.FromResult(false), () => now, CancellationToken.None);
            now += 30;
        }
        Check(retry.CanAttempt("down", now), "repeated failures remain retryable with a bounded cooldown");
        retry.Reset(new[] { "down" });
        Check(retry.CanAttempt("down", 0), "external recovery clears stale backoff");
        attempted.Clear();
        recovered = await retry.TryConnect(new[] { "rejected", "working" }, target =>
        {
            attempted.Add(target);
            return Task.FromResult(new AdbResult { ExitCode = target == "rejected" ? 1 : 0 });
        }, () => Task.FromResult(attempted.Last() == "working"), () => now, CancellationToken.None);
        Check(recovered && attempted.Count == 2, "command failure still tries a working alternate target");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            attempted.Clear();
            await Fails(() => retry.TryConnect(new[] { "unused" }, falseSuccess, () => Task.FromResult(true), () => now, cancelled.Token),
                "canceled", "cancelled recovery stops before launching ADB");
            Check(attempted.Count == 0, "cancelled recovery never connects");
        }

        var result = await Adb.Run(Self, CancellationToken.None, 3000, "nonzero");
        Check(!result.Success && result.ExitCode == 7, "nonzero exit cannot succeed");
        result = await Adb.Run(Self, CancellationToken.None, 3000, "failure");
        Check(!result.Success && result.ExitCode == 0, "failure text cannot succeed even with zero exit");
        result = await Adb.Run(Self, CancellationToken.None, 5000, "flood");
        Check(result.Success && result.Output.Length <= 131100 && result.Error.Length <= 131100, "both pipes drained and output bounded");
        result = await Adb.Run(Self, CancellationToken.None, 150, "sleep");
        Check(result.TimedOut && !result.Success, "hung command timeout");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.CancelAfter(150);
            result = await Adb.Run(Self, cancel.Token, 5000, "sleep");
            Check(result.Cancelled && !result.Success, "in-flight cancellation");
        }
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            result = await Adb.Run(Self, cancel.Token, 5000, "sleep");
            Check(result.Cancelled, "pre-cancelled command never starts");
        }
        foreach (string value in new[] { "two words", "C:\\folder with spaces\\", "literal\"quote", "slash\\\"quote", "", "dollar$(not-a-shell)&x" })
        {
            result = await Adb.Run(Self, CancellationToken.None, 3000, "echo", value);
            Check(result.Success && result.Output.TrimEnd('\r', '\n') == value, "argument round trip: " + value);
        }
        result = await Adb.Run("Z:\\not-found\\adb.exe", CancellationToken.None, 500, "version");
        Check(!result.Success && !result.Detail.Contains("version"), "missing executable error contains no command arguments");

        string apk = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".apk");
        File.WriteAllText(apk, "test fixture");
        try
        {
            var stages = new List<string>();
            await Adb.Deploy(Self, "ready", apk, "com.example.test", 23, new[] { "arm64-v8a" }, CancellationToken.None, stages.Add);
            Check(stages.Count == 3, "ready device install and verified launch");
            await Adb.Deploy(Self, "tv", apk, "com.example.test", 23, new[] { "arm64-v8a" }, CancellationToken.None, s => { });
            Check(true, "TV leanback launcher fallback");
            await Fails(() => Adb.Deploy(Self, "offline", apk, "com.example.test", 23, null, CancellationToken.None, s => { }), "not ready", "offline device cannot install");
            await Fails(() => Adb.Deploy(Self, "ready", apk, "com.example.test", 99, null, CancellationToken.None, s => { }), "minimum API", "minimum SDK preflight");
            await Fails(() => Adb.Deploy(Self, "ready", apk, "com.example.test", 23, new[] { "x86" }, CancellationToken.None, s => { }), "architecture", "ABI preflight");
            await Fails(() => Adb.Deploy(Self, "badinstall", apk, "com.example.test", 23, null, CancellationToken.None, s => { }), "Signing key mismatch", "installation failure stops deployment");
            await Fails(() => Adb.Deploy(Self, "badlaunch", apk, "com.example.test", 23, null, CancellationToken.None, s => { }), "launch was not confirmed", "launch failure distinguished from build/install");
            await Fails(() => Adb.Deploy(Self, "ready", apk + ".aab", "com.example.test", 23, null, CancellationToken.None, s => { }), "APK", "AAB cannot use APK install flow");
        }
        finally { File.Delete(apk); }
    }

    private static int FakeAdb(string[] args)
    {
        switch (args[0])
        {
            case "nonzero": Console.Error.WriteLine("test child failed"); return 7;
            case "failure": Console.WriteLine("Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE]"); return 0;
            case "sleep": Thread.Sleep(10000); return 0;
            case "echo": Console.WriteLine(args[1]); return 0;
            case "flood":
                for (int i = 0; i < 3000; i++) { Console.WriteLine(new string('o', 100)); Console.Error.WriteLine(new string('e', 100)); }
                return 0;
        }
        string serial = args.Length > 1 ? args[1] : "";
        if (args.Contains("get-state")) { Console.WriteLine(serial == "offline" ? "offline" : "device"); return 0; }
        if (args.Contains("ro.build.version.sdk")) { Console.WriteLine("30"); return 0; }
        if (args.Contains("ro.product.cpu.abilist")) { Console.WriteLine("arm64-v8a,armeabi-v7a"); return 0; }
        if (args.Contains("install")) { Console.WriteLine(serial == "badinstall" ? "Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE]" : "Success"); return 0; }
        if (args.Contains("resolve-activity"))
        {
            Console.WriteLine(serial == "tv" && args.Contains("android.intent.category.LAUNCHER") ? "No activity found" : "com.example.test/.MainActivity");
            return 0;
        }
        if (args.Contains("start")) { Console.WriteLine(serial == "badlaunch" ? "Error: Activity not found" : "Status: ok\nComplete"); return 0; }
        return 1;
    }
}
