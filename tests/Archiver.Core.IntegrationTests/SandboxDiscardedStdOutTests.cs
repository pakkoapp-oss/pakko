using System.Diagnostics;
using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F397: "tar -x -O" writes every file's content to stdout. A test of an archive must read
/// that pipe, or tar.exe stops at a full one, and must not keep what it reads.
/// </summary>
[Collection("TarSandbox")]
public sealed class SandboxDiscardedStdOutTests : IDisposable
{
    private const string TarExecutablePath = @"C:\Windows\System32\tar.exe";
    private const int ContentBytes = 24 * 1024 * 1024;

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string TarOfOneLargeFile()
    {
        string source = Path.Combine(_temp.Path, "big.bin");
        var bytes = new byte[ContentBytes];
        new Random(397).NextBytes(bytes);
        File.WriteAllBytes(source, bytes);
        string archive = Path.Combine(_temp.Path, "big.tar");
        using Process tar = Process.Start(new ProcessStartInfo(TarExecutablePath)
        {
            ArgumentList = { "-cf", archive, "-C", _temp.Path, "big.bin" },
            UseShellExecute = false,
        })!;
        tar.WaitForExit();
        tar.ExitCode.Should().Be(0);
        return archive;
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> ExtractToStdOutAsync(string archive, Action<int>? onDiscarded)
    {
        using var stdin = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await SandboxedProcessLauncher.RunAsync(
            TarExecutablePath, ["-x", "-O", "-f", "-"],
            new ProcessLaunchOptions(StdIn: stdin.SafeFileHandle, OnStdOutDiscarded: onDiscarded),
            CancellationToken.None);
    }

    [Integration]
    public async Task OnStdOutDiscarded_CountsEveryByteAndReturnsNoStdOut()
    {
        string archive = TarOfOneLargeFile();
        long discarded = 0;

        (int exitCode, string stdOut, string stdErr) = await ExtractToStdOutAsync(archive, count => discarded += count);

        exitCode.Should().Be(0, stdErr);
        stdOut.Should().BeEmpty();
        discarded.Should().Be(ContentBytes);
    }

    // The option is what turns the keeping off: without it the content comes back, as a listing's must.
    [Integration]
    public async Task WithoutTheOption_StdOutIsReturned()
    {
        string archive = TarOfOneLargeFile();

        (int exitCode, string stdOut, _) = await ExtractToStdOutAsync(archive, onDiscarded: null);

        exitCode.Should().Be(0);
        stdOut.Length.Should().BeGreaterThan(ContentBytes / 2);
    }

    [Integration]
    public async Task OnStdOutDiscarded_NonZeroExit_KeepsTheExitCodeAndStdErr()
    {
        string archive = TarOfOneLargeFile();
        using (var stream = new FileStream(archive, FileMode.Open, FileAccess.Write))
            stream.SetLength(ContentBytes / 2);

        (int exitCode, string stdOut, string stdErr) = await ExtractToStdOutAsync(archive, _ => { });

        exitCode.Should().NotBe(0);
        stdOut.Should().BeEmpty();
        stdErr.Should().Contain("Truncated");
    }

    [Integration]
    public async Task OnStdOutDiscarded_Cancelled_Throws()
    {
        string archive = TarOfOneLargeFile();
        using var cancel = new CancellationTokenSource();
        using var stdin = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);

        Func<Task> run = () => SandboxedProcessLauncher.RunAsync(
            TarExecutablePath, ["-x", "-O", "-f", "-"],
            new ProcessLaunchOptions(StdIn: stdin.SafeFileHandle, OnStdOutDiscarded: _ => cancel.Cancel()),
            cancel.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
    }
}
