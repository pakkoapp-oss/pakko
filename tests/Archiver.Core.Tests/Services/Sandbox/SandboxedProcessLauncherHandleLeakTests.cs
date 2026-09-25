using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// CA1711: xUnit's CollectionDefinition marker-class convention (see docs/CONVENTIONS.md).
#pragma warning disable CA1711
[CollectionDefinition("ProcessHandleCount", DisableParallelization = true)]
public sealed class ProcessHandleCountCollection;
#pragma warning restore CA1711

// Error path (T-F244 item 1): a launch that fails (no such file) must not leak its pipe handles.
// Counts the whole process's handles, so it runs alone — xUnit runs a DisableParallelization
// collection after every parallel one, with nothing else in flight in this process.
[Collection("ProcessHandleCount")]
public sealed class SandboxedProcessLauncherHandleLeakTests
{
    [Fact]
    public async Task RunAsync_CreateProcessFails_DoesNotLeakHandles()
    {
        string missing = Path.Combine(Path.GetTempPath(), "pakko-missing-" + Guid.NewGuid() + ".exe");
        await RunMissingAsync(missing);
        int before = System.Diagnostics.Process.GetCurrentProcess().HandleCount;

        for (int i = 0; i < 50; i++)
            await RunMissingAsync(missing);

        int after = System.Diagnostics.Process.GetCurrentProcess().HandleCount;
        (after - before).Should().BeLessThan(20, "two pipe handles leaked per failed launch would add about 100");
    }

    private static async Task RunMissingAsync(string missing) =>
        await FluentActions.Awaiting(() => SandboxedProcessLauncher.RunAsync(
                missing, [], new ProcessLaunchOptions(), CancellationToken.None))
            .Should().ThrowAsync<IOException>();
}
