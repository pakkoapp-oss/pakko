using System.Diagnostics;
using Archiver.Core.Services.Sandbox;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// T-F426: the launcher marks the caller's stdin handle (the user's archive, T-F233) inheritable so
// tar.exe can get it through the handle list. Left inheritable while tar.exe runs, it went to any
// plain Process.Start in this process too (.NET always passes bInheritHandles TRUE), and that child
// held the archive open, without delete sharing, for as long as it lived. This test starts such a
// child itself, so it stays in the collection where nothing else launches.
[Collection("InheritableHandle")]
public sealed class SandboxedProcessLauncherStdInInheritanceTests
{
    [Fact]
    public async Task RunAsync_StdInHandle_DoesNotReachAProcessStartedWhileTheChildRuns()
    {
        using var temp = new TempDirectory();
        string archive = temp.CreateFile("archive.bin");
        Process? bystander = null;
        try
        {
            using (Microsoft.Win32.SafeHandles.SafeFileHandle stdIn = File.OpenHandle(archive, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                (int exitCode, _, _) = await SandboxedProcessLauncher.RunAsync(
                    @"C:\Windows\System32\cmd.exe",
                    ["/c", "exit 0"],
                    new ProcessLaunchOptions(
                        StdIn: stdIn,
                        OnProcessStarted: _ => bystander = Process.Start(new ProcessStartInfo(
                            @"C:\Windows\System32\ping.exe", "-n 60 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })),
                    CancellationToken.None);
                exitCode.Should().Be(0);
            }

            bystander.Should().NotBeNull();
            bystander!.HasExited.Should().BeFalse("the bystander must still run for this to prove anything");
            Action openExclusively = () => File.Open(archive, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose();
            openExclusively.Should().NotThrow("no process but tar.exe may hold the archive");
        }
        finally
        {
            bystander?.Kill();
            bystander?.WaitForExit(5000);
            bystander?.Dispose();
        }
    }
}
