using System;
using System.Threading;
using System.Threading.Tasks;

namespace OutBuild
{
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
