using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// Step 5 of T-F52's build order: re-confirms Phase 0's spike 0a finding (see DECISIONS.md) —
/// that Windows' built-in bsdtar keeps its .tar.xz/.tar.zst compression filters statically linked
/// and in-process, so a Job Object with ActiveProcessLimit = 1 does not break their extraction —
/// through the real production SandboxJobObject + SandboxedProcessLauncher classes instead of the
/// throwaway spike script. No AppContainer here yet (that combination, with the quarantine ACLs
/// that make it work against a real destination folder, arrives in step 6's TarSandboxScope) —
/// this test isolates the Job Object dimension only.
/// </summary>
[Collection("TarSandbox")]
public sealed class SandboxJobObjectTarExtractionTests : IDisposable
{
    private const string TarExecutablePath = @"C:\Windows\System32\tar.exe";

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [SkipIfFormatUnsupported("xz")]
    public async Task ExtractAsync_TarXz_UnderJobObjectWithActiveProcessLimitOne_Succeeds()
        => await ExtractAndVerify("valid.tar.xz", "-cJf");

    [SkipIfFormatUnsupported("zst")]
    public async Task ExtractAsync_TarZst_UnderJobObjectWithActiveProcessLimitOne_Succeeds()
        => await ExtractAndVerify("valid.tar.zst", "--zstd -cf");

    // T-F239: a process over the Job's memory limit is not killed — its allocation fails and
    // tar.exe prints "Cannot allocate memory", which reads as the machine being out of memory.
    // The job's own notification says which limit it was. An xz preset-9 archive declares a
    // 64 MiB dictionary, which the decoder allocates up front.
    [SkipIfFormatUnsupported("xz")]
    public async Task ReadLimitHit_DecoderOverMemoryLimit_ReportsMemory()
    {
        string archivePath = CreateXzPreset9();
        using var job = SandboxJobObject.Create(ramLimitBytes: 32 * 1024 * 1024, cpuTimeLimit: TimeSpan.FromMinutes(2));

        var (exitCode, _, _) = await RunTarInJob(job, archivePath);

        exitCode.Should().NotBe(0);
        job.ReadLimitHit().Should().Be(SandboxJobObject.LimitHit.Memory);
    }

    // A busy loop: tar.exe on a small archive finishes inside the scheduler's CPU accounting tick.
    [Fact]
    public async Task ReadLimitHit_OverCpuTimeLimit_ReportsCpuTime()
    {
        using var job = SandboxJobObject.Create(ramLimitBytes: 512 * 1024 * 1024, cpuTimeLimit: TimeSpan.FromMilliseconds(50));

        var (exitCode, _, _) = await SandboxedProcessLauncher.RunAsync(
            @"C:\Windows\System32\cmd.exe", ["/c", "for /L %i in (1,1,50000000) do @rem"],
            new ProcessLaunchOptions(Job: job.Handle), CancellationToken.None);

        exitCode.Should().NotBe(0);
        job.ReadLimitHit().Should().Be(SandboxJobObject.LimitHit.CpuTime);
    }

    [SkipIfFormatUnsupported("xz")]
    public async Task ReadLimitHit_NormalRun_ReportsNone()
    {
        string archivePath = CreateXzPreset9();
        using var job = SandboxJobObject.Create(ramLimitBytes: 512 * 1024 * 1024, cpuTimeLimit: TimeSpan.FromMinutes(2));

        var (exitCode, _, stdErr) = await RunTarInJob(job, archivePath);

        exitCode.Should().Be(0, because: stdErr);
        job.ReadLimitHit().Should().Be(SandboxJobObject.LimitHit.None);
    }

    private string CreateXzPreset9()
    {
        string source = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "a.txt"), "dictionary");
        string archivePath = Path.Combine(_temp.Path, "preset9.tar.xz");
        var start = new System.Diagnostics.ProcessStartInfo(TarExecutablePath) { WorkingDirectory = source, UseShellExecute = false };
        foreach (string arg in new[] { "-cJf", archivePath, "--options", "xz:compression-level=9", "a.txt" })
            start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit();
        process.ExitCode.Should().Be(0);
        return archivePath;
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunTarInJob(SandboxJobObject job, string archivePath)
    {
        string destDir = Path.Combine(_temp.Path, "out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destDir);
        return await SandboxedProcessLauncher.RunAsync(
            TarExecutablePath, ["-xf", archivePath, "-C", destDir], new ProcessLaunchOptions(Job: job.Handle), CancellationToken.None);
    }

    private async Task ExtractAndVerify(string fileName, string compressionFlag)
    {
        string archivePath = Path.Combine(_temp.Path, fileName);
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, compressionFlag, [("a.txt", "hello sandboxed job object")]);

        string destDir = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(destDir);

        using var job = SandboxJobObject.Create(ramLimitBytes: 512 * 1024 * 1024, cpuTimeLimit: TimeSpan.FromMinutes(2));

        var (exitCode, _, stdErr) = await SandboxedProcessLauncher.RunAsync(
            TarExecutablePath,
            ["-xf", archivePath, "-C", destDir],
            new ProcessLaunchOptions(Job: job.Handle),
            CancellationToken.None);

        exitCode.Should().Be(0, because: stdErr);
        File.ReadAllText(Path.Combine(destDir, "a.txt")).Should().Be("hello sandboxed job object");
    }
}
