using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// CA1711: xUnit's CollectionDefinition marker-class convention (see docs/CONVENTIONS.md).
#pragma warning disable CA1711
[CollectionDefinition("InheritableHandle", DisableParallelization = true)]
public sealed class InheritableHandleCollection;
#pragma warning restore CA1711

// T-F373: the pipe below is inheritable until the child exists, and any Process.Start another test class runs
// meanwhile (AgentBashHookTests' pwsh, a mklink junction) inherits it with every other inheritable
// handle, so EOF waited for that unrelated child. Alone in its collection, nothing else launches.
[Collection("InheritableHandle")]
public sealed class SandboxedProcessLauncherInheritanceTests
{
    // T-F244 item 5: bInheritHandles = TRUE with no PROC_THREAD_ATTRIBUTE_HANDLE_LIST handed the
    // child EVERY inheritable handle in this process — another launch's pipe write end, or a
    // stdin handle to the user's archive. A reader waiting for EOF on such a pipe then waited
    // until this unrelated child exited. Here: our own inheritable pipe must reach EOF as soon as
    // we close our copy, while the child is still running.
    [Fact]
    public async Task RunAsync_UnrelatedInheritableHandle_IsNotInheritedByChild()
    {
        using var server = new System.IO.Pipes.AnonymousPipeServerStream(
            System.IO.Pipes.PipeDirection.In, System.IO.HandleInheritability.Inheritable);

        using var cts = new CancellationTokenSource();
        int childId = 0;
        Task<(int, string, string)> child = SandboxedProcessLauncher.RunAsync(
            @"C:\Windows\System32\cmd.exe",
            ["/c", @"C:\Windows\System32\ping.exe -n 6 127.0.0.1 >nul"],
            new ProcessLaunchOptions(OnProcessStarted: id => childId = id),
            cts.Token);

        // CreateProcessW ran before RunAsync's first await: the child holds a copy or never will.
        using var childProcess = System.Diagnostics.Process.GetProcessById(childId);
        server.DisposeLocalCopyOfClientHandle();

        // T-F425: ReadAsync on this synchronous pipe blocks a thread-pool thread, and a starved pool
        // at the end of a CI run held EOF past a 2 s clock. A dedicated thread and the kernel's own
        // wait on the child judge EOF against the child's lifetime, not the pool's.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Task<(int Read, TimeSpan At, bool ChildAliveAfter)> eof = Task.Factory.StartNew(
            () =>
            {
                int read = server.Read(new byte[1], 0, 1);
                TimeSpan at = clock.Elapsed;
                return (read, at, !childProcess.WaitForExit(1000));
            },
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        Task finished = await Task.WhenAny(eof, Task.Delay(TimeSpan.FromSeconds(30)));
        finished.Should().BeSameAs(eof,
            $"something holds the pipe open (pool threads {ThreadPool.ThreadCount}, pending {ThreadPool.PendingWorkItemCount})");
        (int bytes, TimeSpan eofAt, bool childAliveAfter) = await eof;
        bytes.Should().Be(0);
        childAliveAfter.Should().BeTrue(
            $"EOF must come while the child runs, not when it exits: the child must not hold an inherited copy of the pipe (EOF after {eofAt.TotalMilliseconds:F0} ms)");

        cts.Cancel();
        await FluentActions.Awaiting(() => child).Should().ThrowAsync<OperationCanceledException>();
    }
}
