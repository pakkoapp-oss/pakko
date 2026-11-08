using Archiver.OperationUi.Protocol;
using Archiver.Shell;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// CA1711: xUnit's CollectionDefinition marker-class convention (see docs/CONVENTIONS.md).
#pragma warning disable CA1711
[CollectionDefinition("StarvedThreadPool", DisableParallelization = true)]
public sealed class StarvedThreadPoolCollection;
#pragma warning restore CA1711

/// <summary>
/// T-F456: CancelInTheWindow_CancelsTheOperationWithoutFailover timed out once in a full run (16 s)
/// and passed alone in 0.3 s. The session read the helper's pipe through ReadAsync, which on an
/// anonymous (synchronous) pipe blocks a pool thread for as long as the helper says nothing, and the
/// fake helper read the same way; a loaded run had no pool thread left for the next frame (T-F425).
/// Here every pool thread but one is held, as a loaded run holds them: a session waiting for its
/// helper must leave that one free.
/// </summary>
[Collection("StarvedThreadPool")]
public sealed class HelperOperationUiStarvedPoolTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProbeLimit = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task SessionWaitingForItsHelper_LeavesThePoolItsLastThread()
    {
        using var helper = new FakeHelper();
        var ui = new HelperOperationUi(helper, new FakeOperationUi())
        {
            ReadyTimeout = WaitLimit,
            ProgressInterval = TimeSpan.Zero,
            StartDelay = TimeSpan.Zero,
        };

        bool poolThreadFree;
        using (PoolHold.AllButOne())
        {
            using IOperationSession session = ui.Begin("Extracting: a.zip", ProgressStyle.Bytes);
            await helper.ReadUntilAsync<Begin>();
            // The reader is started after the pump; let it reach its read.
            await Task.Delay(TimeSpan.FromMilliseconds(200));

            // Waited on here, not awaited: an await would hand this test's own pool thread to the probe.
            // Never disposed: the probe may still run after the wait gave up.
            var probe = new ManualResetEventSlim();
            ThreadPool.UnsafeQueueUserWorkItem(_ => probe.Set(), null);
            poolThreadFree = probe.Wait(ProbeLimit);
        }

        poolThreadFree.Should().BeTrue("a session waiting for its helper must not hold a pool thread");
    }

    // Caps the pool at its minimum and holds all of it but one thread, the way a loaded run does.
    private sealed class PoolHold : IDisposable
    {
        private static readonly TimeSpan Settle = TimeSpan.FromSeconds(1);

        // Never disposed: a holder may still be waking from it after Dispose.
        private readonly ManualResetEventSlim _release = new();
        private readonly int _maxWorkers;
        private readonly int _maxIo;

        private PoolHold()
        {
            ThreadPool.GetMaxThreads(out _maxWorkers, out _maxIo);
            ThreadPool.GetMinThreads(out int minWorkers, out _);
            if (!ThreadPool.SetMaxThreads(minWorkers, _maxIo))
                throw new InvalidOperationException($"The pool's maximum could not be set to {minWorkers}.");
            try
            {
                // Threads busy with the runner's own work come and go: hold until one stays free.
                DateTime until = DateTime.UtcNow + Settle;
                while (DateTime.UtcNow < until)
                {
                    ThreadPool.GetAvailableThreads(out int available, out _);
                    if (available > 1)
                        HoldMore(available - 1);
                    else
                        Thread.Sleep(20);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public static PoolHold AllButOne() => new();

        private void HoldMore(int count)
        {
            // Not disposed: after a timeout a late holder still signals it.
            var started = new CountdownEvent(count);
            for (int i = 0; i < count; i++)
            {
                ThreadPool.UnsafeQueueUserWorkItem(_ =>
                {
                    started.Signal();
                    _release.Wait();
                }, null);
            }
            if (!started.Wait(WaitLimit))
                throw new TimeoutException($"The pool did not start {count} threads to hold.");
        }

        public void Dispose()
        {
            _release.Set();
            ThreadPool.SetMaxThreads(_maxWorkers, _maxIo);
        }
    }
}
