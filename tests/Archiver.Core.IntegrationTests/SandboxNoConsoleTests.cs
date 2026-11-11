using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F507: a launched tar.exe or helper gets no console. With CREATE_NO_WINDOW each one still had a
/// hidden conhost.exe in Pakko's job (about 2,000 page faults and 30-50 ms per launch, three
/// launches per tar extraction); DETACHED_PROCESS gives none. "mode con" prints the console device's
/// status (in the system's language) when there is one, and nothing in a detached child.
/// </summary>
[Collection("TarSandbox")]
public sealed class SandboxNoConsoleTests
{
    private const string CmdPath = @"C:\Windows\System32\cmd.exe";

    [Integration]
    public async Task RunAsync_Child_HasNoConsole()
    {
        (int exitCode, string stdOut, string stdErr) = await SandboxedProcessLauncher.RunAsync(
            CmdPath, ["/d", "/c", "mode con"], new ProcessLaunchOptions(), CancellationToken.None);

        exitCode.Should().Be(0, stdErr);
        stdOut.Should().BeEmpty("a detached child has no console device to report on");
    }

    // The child's standard handles are the launcher's pipes either way.
    [Integration]
    public async Task RunAsync_DetachedChild_StillWritesToItsPipes()
    {
        (int exitCode, string stdOut, string stdErr) = await SandboxedProcessLauncher.RunAsync(
            CmdPath, ["/d", "/c", "echo out& echo err 1>&2"], new ProcessLaunchOptions(), CancellationToken.None);

        exitCode.Should().Be(0);
        stdOut.Trim().Should().Be("out");
        stdErr.Trim().Should().Be("err");
    }
}
