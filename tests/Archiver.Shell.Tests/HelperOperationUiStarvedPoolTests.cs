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
/// Here the pool is capped and more sessions wait for their helpers than it has threads: a reader
/// that holds a pool thread starves it for good, while the runner's own work only delays the probe.
/// </summary>
[Collection("StarvedThreadPool")]
public sealed class HelperOperationUiStarvedPoolTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProbeLimit = TimeSpan.FromSeconds(5);
    private const int Headroom = 2;

    [Fact]
    public async Task SessionsWaitingForTheirHelpers_LeaveThePoolFree()
    {
        // On a thread of its own and with synchronous waits: an await's continuation, or Task.Delay's
        // timer, would need the capped pool itself and could hang the test instead of failing it.
        string? starved = await Task.Factory.StartNew(StarvedBySessions, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

        starved.Should().BeNull("a session waiting for its helper must not hold a pool thread");
    }

    private static string? StarvedBySessions()
    {
        // The cap leaves the threads the runner already keeps busy (testhost blocks some for good: on
        // two cores a cap at the minimum left none) plus a little headroom; the sessions outnumber it.
        ThreadPool.GetMaxThreads(out int maxWorkers, out int maxIo);
        ThreadPool.GetMinThreads(out int minWorkers, out _);
        ThreadPool.GetAvailableThreads(out int available, out _);
        int cap = Math.Max(minWorkers, maxWorkers - available + Headroom);
        int sessionCount = cap - (maxWorkers - available) + 2;
        if (!ThreadPool.SetMaxThreads(cap, maxIo))
            throw new InvalidOperationException($"The pool's maximum could not be set to {cap}.");
        var helpers = new List<FakeHelper>();
        var sessions = new List<IOperationSession>();
        try
        {
            for (int i = 0; i < sessionCount; i++)
            {
                var helper = new FakeHelper();
                helpers.Add(helper);
                var ui = new HelperOperationUi(helper, new FakeOperationUi())
                {
                    ReadyTimeout = WaitLimit,
                    ProgressInterval = TimeSpan.Zero,
                    StartDelay = TimeSpan.Zero,
                };
                sessions.Add(ui.Begin($"Extracting: {i}.zip", ProgressStyle.Bytes));
                if (!helper.ReadUntilAsync<Begin>().Wait(WaitLimit))
                    return $"session {i} of {sessionCount} sent no Begin within {WaitLimit} (pool capped at {cap})";
            }
            // Each reader is started after its pump; let the last ones reach their read. Not
            // Task.Delay: its timer completes on the capped pool.
            Thread.Sleep(TimeSpan.FromMilliseconds(200));

            // Never disposed: the probe may still run after the wait gave up.
            var probe = new ManualResetEventSlim();
            ThreadPool.UnsafeQueueUserWorkItem(_ => probe.Set(), null);
            return probe.Wait(ProbeLimit)
                ? null
                : $"no pool thread ran a work item within {ProbeLimit} beside {sessions.Count} sessions (pool capped at {cap})";
        }
        finally
        {
            ThreadPool.SetMaxThreads(maxWorkers, maxIo);
            foreach (IOperationSession session in sessions)
                session.Dispose();
            foreach (FakeHelper helper in helpers)
                helper.Dispose();
        }
    }
}
