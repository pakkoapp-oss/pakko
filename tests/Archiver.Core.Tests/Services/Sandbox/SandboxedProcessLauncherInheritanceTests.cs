using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// CA1711: xUnit's CollectionDefinition marker-class convention (see docs/CONVENTIONS.md).
#pragma warning disable CA1711
[CollectionDefinition("InheritableHandle", DisableParallelization = true)]
public sealed class InheritableHandleCollection;
#pragma warning restore CA1711

// T-F373: the pipe below is inheritable for 500 ms, and any Process.Start another test class runs
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
        Task<(int, string, string)> child = SandboxedProcessLauncher.RunAsync(
            @"C:\Windows\System32\cmd.exe",
            ["/c", @"C:\Windows\System32\ping.exe -n 6 127.0.0.1 >nul"],
            new ProcessLaunchOptions(),
            cts.Token);

        await Task.Delay(500);
        server.DisposeLocalCopyOfClientHandle();

        byte[] buffer = new byte[1];
        Task<int> read = server.ReadAsync(buffer, 0, 1);
        Task finished = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(2)));

        child.IsCompleted.Should().BeFalse("the child must still be running for this to prove anything");
        finished.Should().BeSameAs(read, "the child must not hold an inherited copy of the pipe");
        (await read).Should().Be(0);

        cts.Cancel();
        await FluentActions.Awaiting(() => child).Should().ThrowAsync<OperationCanceledException>();
    }
}
