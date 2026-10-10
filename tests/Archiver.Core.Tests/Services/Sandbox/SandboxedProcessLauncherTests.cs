using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// Step 3 of T-F52's build order: prove the raw CreateProcessW + STARTUPINFOEX + pipe plumbing
// works at all before layering AppContainer security capabilities (step 4) or a Job Object
// (step 5) on top — no attributeList, no jobObject here.
public sealed class SandboxedProcessLauncherTests
{
    [Fact]
    public async Task RunAsync_EchoCommand_CapturesStdOutAndExitCode()
    {
        (int exitCode, string? stdOut, string? stdErr) = await SandboxedProcessLauncher.RunAsync(
            @"C:\Windows\System32\cmd.exe",
            ["/c", "echo hello sandbox"],
            new ProcessLaunchOptions(),
            CancellationToken.None);

        exitCode.Should().Be(0);
        stdOut.Should().Contain("hello sandbox");
        stdErr.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_NonZeroExitCommand_ReturnsExitCode()
    {
        (int exitCode, _, _) = await SandboxedProcessLauncher.RunAsync(
            @"C:\Windows\System32\cmd.exe",
            ["/c", "exit 7"],
            new ProcessLaunchOptions(),
            CancellationToken.None);

        exitCode.Should().Be(7);
    }

    [Fact]
    public async Task RunAsync_ArgumentContainingSpaces_StaysOneArgument()
    {
        // cmd.exe's own "echo" builtin doesn't parse argv (it just echoes the raw command-tail
        // text back, quotes and all), so it can't prove BuildCommandLine's quoting. "type"
        // instead genuinely requires its filename argument to arrive as one unsplit token — if
        // the embedded space were to split it into two argv entries, "type" would look for a
        // wrong/truncated path and fail non-zero.
        string tempFile = Path.Combine(Path.GetTempPath(), "Pakko sandbox launcher test " + Guid.NewGuid() + ".txt");
        File.WriteAllText(tempFile, "ABC123");
        try
        {
            (int exitCode, string? stdOut, _) = await SandboxedProcessLauncher.RunAsync(
                @"C:\Windows\System32\cmd.exe",
                ["/c", "type", tempFile],
                new ProcessLaunchOptions(),
                CancellationToken.None);

            exitCode.Should().Be(0);
            stdOut.Should().Contain("ABC123");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RunAsync_LargeStdOutput_DoesNotDeadlock()
    {
        // Regression guard for the classic pipe-deadlock bug: a child writing enough output to
        // fill the pipe buffer before the parent starts reading would hang forever without
        // async draining of both streams.
        (int exitCode, string? stdOut, _) = await SandboxedProcessLauncher.RunAsync(
            @"C:\Windows\System32\cmd.exe",
            ["/c", "for /L %i in (1,1,2000) do @echo line %i"],
            new ProcessLaunchOptions(),
            CancellationToken.None);

        exitCode.Should().Be(0);
        stdOut.Should().Contain("line 2000");
    }

    // T-F244 item 1: TerminateProcess returns before the process is gone; TarSandboxScope.Dispose
    // then deleted the quarantine while tar.exe still held files in it (a flaky leftover
    // %TEMP%\PakkoTarSandbox\<guid>). A cancelled call now returns only once the child is gone.
    [Fact]
    public async Task RunAsync_Cancelled_ChildHasExitedWhenCallReturns()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            int pid = 0;
            using var cts = new CancellationTokenSource();
            Task<(int, string, string)> child = SandboxedProcessLauncher.RunAsync(
                @"C:\Windows\System32\ping.exe",
                ["-n", "30", "127.0.0.1"],
                new ProcessLaunchOptions(OnProcessStarted: p => pid = p),
                cts.Token);

            await Task.Delay(300);
            cts.Cancel();
            await FluentActions.Awaiting(() => child).Should().ThrowAsync<OperationCanceledException>();

            pid.Should().NotBe(0);
            ProcessHasExited(pid).Should().BeTrue();
        }
    }

    private static bool ProcessHasExited(int pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return p.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
