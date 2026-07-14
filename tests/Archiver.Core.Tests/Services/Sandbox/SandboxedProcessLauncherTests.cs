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
        var (exitCode, stdOut, stdErr) = await SandboxedProcessLauncher.RunAsync(
            @"C:\Windows\System32\cmd.exe",
            ["/c", "echo hello sandbox"],
            attributeList: null,
            jobObject: null,
            CancellationToken.None);

        exitCode.Should().Be(0);
        stdOut.Should().Contain("hello sandbox");
        stdErr.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_NonZeroExitCommand_ReturnsExitCode()
    {
        var (exitCode, _, _) = await SandboxedProcessLauncher.RunAsync(
            @"C:\Windows\System32\cmd.exe",
            ["/c", "exit 7"],
            attributeList: null,
            jobObject: null,
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
            var (exitCode, stdOut, _) = await SandboxedProcessLauncher.RunAsync(
                @"C:\Windows\System32\cmd.exe",
                ["/c", "type", tempFile],
                attributeList: null,
                jobObject: null,
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
        var (exitCode, stdOut, _) = await SandboxedProcessLauncher.RunAsync(
            @"C:\Windows\System32\cmd.exe",
            ["/c", "for /L %i in (1,1,2000) do @echo line %i"],
            attributeList: null,
            jobObject: null,
            CancellationToken.None);

        exitCode.Should().Be(0);
        stdOut.Should().Contain("line 2000");
    }
}
