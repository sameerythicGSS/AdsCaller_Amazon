using System;
using System.Threading;
using System.Threading.Tasks;

namespace OutBuild
{
    // A held OS file handle advertises builds to other Editors using this machine's
    // device registry. Crashes release the handle, so stale files never freeze discovery.
    [UnityEditor.InitializeOnLoad]
    internal static class BuildRefreshPause
    {
        private static System.IO.FileStream lease;
        private static int scopes;
        internal static event Action Pausing;
        private static string Folder { get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(MachineSettingsStore.DefaultPath), "build-leases"); } }
        static BuildRefreshPause()
        {
            UnityEditor.EditorApplication.update += Update;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Release;
            UnityEditor.EditorApplication.quitting += Release;
        }
        internal static IDisposable Begin()
        {
            scopes++;
            Hold();
            return new Scope();
        }
        internal static void Hold()
        {
            if (lease != null) return;
            try
            {
                System.IO.Directory.CreateDirectory(Folder);
                string path = System.IO.Path.Combine(Folder, System.Diagnostics.Process.GetCurrentProcess().Id + ".lock");
                lease = new System.IO.FileStream(path, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None);
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[Build Automator] Could not advertise build pause: " + e.Message); }
            Pausing?.Invoke();
        }
        internal static bool IsPaused
        {
            get
            {
                if (scopes > 0 || lease != null || BuildJobs.Busy || UnityEditor.BuildPipeline.isBuildingPlayer) return true;
                try
                {
                    if (!System.IO.Directory.Exists(Folder)) return false;
                    foreach (string path in System.IO.Directory.GetFiles(Folder, "*.lock"))
                    {
                        try { using (System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None)) { } }
                        catch (System.IO.FileNotFoundException) { }
                        catch (System.IO.IOException) { return true; }
                    }
                }
                catch (System.IO.IOException) { }
                catch (UnauthorizedAccessException) { }
                return false;
            }
        }
        private static void Update()
        {
            if (BuildJobs.Busy || UnityEditor.BuildPipeline.isBuildingPlayer) Hold();
            else if (scopes == 0) Release();
        }
        private static void Release() { lease?.Dispose(); lease = null; }
        private sealed class Scope : IDisposable
        {
            private bool disposed;
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                scopes--;
                Update();
            }
        }
    }

    // Also cover builds started from Unity's own Build window or another script.
    internal sealed class BuildRefreshPreprocessor : UnityEditor.Build.IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return int.MinValue; } }
        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report) { BuildRefreshPause.Hold(); }
    }

    // One scheduler per Editor, independent of the number/lifetime of dashboard windows.
    // Call on the Editor synchronization context; worker commands may run on other threads.
    public sealed class OperationScheduler
    {
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource current;
        private int foregroundRequests;
        public bool Quiet { get; private set; }
        public bool Busy { get { return current != null || foregroundRequests > 0; } }
        public bool ForegroundBusy { get { return foregroundRequests > 0; } }
        public bool Stopped { get; private set; }
        public void Cancel() { current?.Cancel(); }
        public void Shutdown() { Stopped = true; Cancel(); }

        public async Task Run(Func<CancellationToken, Task> action, bool quiet = false)
        {
            if (Stopped) throw new OperationCanceledException();
            if (quiet && Busy) return;
            if (!quiet)
            {
                foregroundRequests++;
                if (Quiet) Cancel();
            }
            await gate.WaitAsync();
            try
            {
                if (Stopped) throw new OperationCanceledException();
                current = new CancellationTokenSource();
                Quiet = quiet;
                await action(current.Token);
            }
            finally
            {
                current?.Dispose();
                current = null;
                Quiet = false;
                if (!quiet) foregroundRequests--;
                gate.Release();
            }
        }
    }
}
