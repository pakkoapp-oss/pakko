using System.Diagnostics;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Sandbox;
using FluentAssertions;
using Xunit.Abstractions;

namespace Archiver.Core.PerformanceTests;

/// <summary>
/// T-F132 empirical spike: measures the real overhead of running <see cref="ZipArchiveService"/>
/// inside a sandboxed AppContainer worker process (tools/ZipSandboxSpike/) versus the in-process
/// baseline <see cref="CompressionPerformanceTests"/> already times. Data-gathering, not a
/// regression gate — numbers are reported via <see cref="ITestOutputHelper"/>, not asserted
/// against a tolerance ratio, since there is no calibrated-baseline history for this scenario yet.
/// See docs/DECISIONS.md's T-F132 entry for the real results and docs/TASKS.md's T-F132 entry for
/// what (if anything) they change about the "not sandboxed by design" status quo.
///
/// Deliberately simpler than TarSandboxScope's production ACL split (read-only on source, modify
/// on destination, per-file re-grants): this isn't defending against a hostile archive, only
/// measuring confinement overhead for trusted code, so every test grants a single Modify ACE on
/// its whole TempDirectory root before creating anything inside it (fixtures and outputs alike) —
/// one grant-before-populate step instead of several. The retroactive-inheritance trap (an
/// inheritable ACE does not apply to files that already exist) still applies and is still
/// respected: grant always happens on an empty directory, before any file is written into it.
/// </summary>
public sealed class ZipSandboxSpikePerformanceTests : IDisposable
{
    private const string ProfileName = "Pakko.ZipSandboxSpike";

    // Generous headroom, not a tight security boundary — mirrors SevenZipRunner's own reasoning
    // for the same 300 MB OneLargeFile scenario.
    private static readonly long RamLimitBytes = 2L * 1024 * 1024 * 1024;
    private static readonly TimeSpan CpuTimeLimit = TimeSpan.FromMinutes(10);

    private static readonly AppContainerProfile Profile = new(ProfileName);
    private static readonly Lazy<string> ToolExePath = new(EnsureToolDirectory);

    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();
    private readonly ITestOutputHelper _output;

    public ZipSandboxSpikePerformanceTests(ITestOutputHelper output) => _output = output;

    public void Dispose() => _temp.Dispose();

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ArchiveAsync_ManySmallFiles_Comparison()
    {
        GrantWholeRoot(_temp.Path);
        string sourceDir = PerformanceFixtures.CreateManySmallFilesFolder(_temp.Path);

        await RunInProcessArchiveTimed(sourceDir, Path.Combine(_temp.Path, "warmup_inprocess.zip")); // warmup
        await RunWorkerAsync("archive", sourceDir, Path.Combine(_temp.Path, "warmup_sandboxed.zip")); // warmup

        var inProcess = await RunInProcessArchiveTimed(sourceDir, Path.Combine(_temp.Path, "inprocess.zip"));
        string workerDest = Path.Combine(_temp.Path, "sandboxed.zip");
        var worker = await RunWorkerAsync("archive", sourceDir, workerDest);

        worker.ExitCode.Should().Be(0, because: worker.StdErr);
        File.Exists(workerDest).Should().BeTrue();
        Report("Archive/ManySmallFiles", inProcess, worker);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ExtractAsync_ManySmallFiles_Comparison()
    {
        GrantWholeRoot(_temp.Path);
        string sourceDir = PerformanceFixtures.CreateManySmallFilesFolder(_temp.Path);
        string referenceZip = Path.Combine(_temp.Path, "shared.zip");
        await RunInProcessArchiveTimed(sourceDir, referenceZip); // untimed shared input, both sides extract this

        await RunInProcessExtractTimed(referenceZip, Path.Combine(_temp.Path, "warmup_extract_inprocess")); // warmup
        await RunWorkerAsync("extract", referenceZip, Path.Combine(_temp.Path, "warmup_extract_sandboxed")); // warmup

        var inProcess = await RunInProcessExtractTimed(referenceZip, Path.Combine(_temp.Path, "extract_inprocess"));
        string workerDest = Path.Combine(_temp.Path, "extract_sandboxed");
        var worker = await RunWorkerAsync("extract", referenceZip, workerDest);

        worker.ExitCode.Should().Be(0, because: worker.StdErr);
        Directory.GetFiles(workerDest, "*", SearchOption.AllDirectories)
            .Should().HaveCount(PerformanceFixtures.ManySmallFilesCount);
        Report("Extract/ManySmallFiles", inProcess, worker);
    }

    [Fact]
    [Trait("Category", "VeryLarge")]
    public async Task ArchiveAsync_OneLargeFile_Comparison()
    {
        GrantWholeRoot(_temp.Path);
        string sourceDir = PerformanceFixtures.CreateOneLargeFileFolder(_temp.Path);

        await RunInProcessArchiveTimed(sourceDir, Path.Combine(_temp.Path, "warmup_inprocess.zip"));
        await RunWorkerAsync("archive", sourceDir, Path.Combine(_temp.Path, "warmup_sandboxed.zip"));

        var inProcess = await RunInProcessArchiveTimed(sourceDir, Path.Combine(_temp.Path, "inprocess.zip"));
        string workerDest = Path.Combine(_temp.Path, "sandboxed.zip");
        var worker = await RunWorkerAsync("archive", sourceDir, workerDest);

        worker.ExitCode.Should().Be(0, because: worker.StdErr);
        File.Exists(workerDest).Should().BeTrue();
        Report("Archive/OneLargeFile", inProcess, worker);
    }

    [Fact]
    [Trait("Category", "VeryLarge")]
    public async Task ExtractAsync_OneLargeFile_Comparison()
    {
        GrantWholeRoot(_temp.Path);
        string sourceDir = PerformanceFixtures.CreateOneLargeFileFolder(_temp.Path);
        string referenceZip = Path.Combine(_temp.Path, "shared.zip");
        await RunInProcessArchiveTimed(sourceDir, referenceZip);

        await RunInProcessExtractTimed(referenceZip, Path.Combine(_temp.Path, "warmup_extract_inprocess"));
        await RunWorkerAsync("extract", referenceZip, Path.Combine(_temp.Path, "warmup_extract_sandboxed"));

        var inProcess = await RunInProcessExtractTimed(referenceZip, Path.Combine(_temp.Path, "extract_inprocess"));
        string workerDest = Path.Combine(_temp.Path, "extract_sandboxed");
        var worker = await RunWorkerAsync("extract", referenceZip, workerDest);

        worker.ExitCode.Should().Be(0, because: worker.StdErr);
        Directory.GetFiles(workerDest, "*", SearchOption.AllDirectories).Should().HaveCount(1);
        Report("Extract/OneLargeFile", inProcess, worker);
    }

    /// <summary>
    /// Negative control — the actual security proof, not just "does the happy path work",
    /// mirroring QuarantineAclTests' own no-grant test. A destination that never received any ACE
    /// for the AppContainer SID must be unreachable, even though Pakko's own (unsandboxed) test
    /// process created it and can read/write it freely itself. This must pass before any timing
    /// number above is trusted — otherwise a misconfigured/no-op AppContainer could produce
    /// plausible-looking numbers that don't reflect a real security boundary being crossed.
    /// </summary>
    [Fact]
    [Trait("Category", "Slow")]
    public async Task NoGrant_SandboxedWorker_FailsWithAccessDenied()
    {
        string toolExePath = ToolExePath.Value;

        // Deliberately never call GrantWholeRootAsync on _temp.Path for this test.
        string sourceDir = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "a.txt"), "should not be archived");
        string destZip = Path.Combine(_temp.Path, "should_not_exist.zip");

        Profile.EnsureExists();
        using SafeSidHandle sid = Profile.GetSid();
        using SecurityCapabilitiesAttributeList caps = SecurityCapabilitiesAttributeList.Create(sid);

        var (exitCode, _, _) = await SandboxedProcessLauncher.RunAsync(
            toolExePath, ["archive", sourceDir, destZip], caps.AttributeList, jobObject: null, CancellationToken.None);

        exitCode.Should().NotBe(0);
        File.Exists(destZip).Should().BeFalse();
    }

    private void Report(string label, TimeSpan inProcess, WorkerRunResult worker)
    {
        double deltaMs = worker.Total.TotalMilliseconds - inProcess.TotalMilliseconds;
        double deltaPercent = (worker.Total.TotalMilliseconds / inProcess.TotalMilliseconds - 1) * 100;
        _output.WriteLine(
            $"{label} — in-process: {inProcess.TotalMilliseconds:F1}ms, " +
            $"sandboxed-total: {worker.Total.TotalMilliseconds:F1}ms, " +
            $"worker-internal: {worker.InternalMs?.ToString("F1") ?? "n/a"}ms, " +
            $"delta: {deltaMs:F1}ms ({deltaPercent:F1}%)");
    }

    private void GrantWholeRoot(string path)
    {
        Profile.EnsureExists();
        using SafeSidHandle sid = Profile.GetSid();
        QuarantineAcl.GrantModify(path, sid);
    }

    private async Task<TimeSpan> RunInProcessArchiveTimed(string sourceDir, string destinationZipPath)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir],
            DestinationFolder = Path.GetDirectoryName(destinationZipPath)!,
            ArchiveName = Path.GetFileNameWithoutExtension(destinationZipPath),
        });
        stopwatch.Stop();
        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        return stopwatch.Elapsed;
    }

    private async Task<TimeSpan> RunInProcessExtractTimed(string archivePath, string destinationDir)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destinationDir,
            Mode = ExtractMode.SingleFolder,
        });
        stopwatch.Stop();
        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        return stopwatch.Elapsed;
    }

    private async Task<WorkerRunResult> RunWorkerAsync(string operation, string sourcePath, string destPath)
    {
        string toolExePath = ToolExePath.Value;

        Profile.EnsureExists();
        using SafeSidHandle sid = Profile.GetSid();
        using SecurityCapabilitiesAttributeList caps = SecurityCapabilitiesAttributeList.Create(sid);
        using SandboxJobObject job = SandboxJobObject.Create(RamLimitBytes, CpuTimeLimit);

        var stopwatch = Stopwatch.StartNew();
        var (exitCode, _, stdErr) = await SandboxedProcessLauncher.RunAsync(
            toolExePath, [operation, sourcePath, destPath], caps.AttributeList, job.Handle, CancellationToken.None);
        stopwatch.Stop();

        return new WorkerRunResult(stopwatch.Elapsed, ParseInternalElapsedMs(stdErr), exitCode, stdErr);
    }

    private static double? ParseInternalElapsedMs(string stdErr)
    {
        const string prefix = "internal_elapsed_ms=";
        foreach (string line in stdErr.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal)
                && double.TryParse(trimmed[prefix.Length..], out double ms))
                return ms;
        }
        return null;
    }

    private static string? FindRepoRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static string EnsureToolDirectory()
    {
        string repoRoot = FindRepoRoot(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException(
                $"Could not locate windows-archiver-wrapper.sln walking up from '{AppContext.BaseDirectory}'.");

        string publishedDir = Path.Combine(repoRoot, "artifacts", "zip-sandbox-spike", "win-x64");
        string publishedExe = Path.Combine(publishedDir, "ZipSandboxSpike.exe");
        if (!File.Exists(publishedExe))
            throw new InvalidOperationException(
                $"Published ZipSandboxSpike.exe not found at '{publishedExe}'. Publish it first — see " +
                "tools/ZipSandboxSpike/README.md.");

        string parentDir = Path.Combine(Path.GetTempPath(), "PakkoZipSandboxSpike");
        string toolDir = Path.Combine(parentDir, "tool");
        string toolExe = Path.Combine(toolDir, "ZipSandboxSpike.exe");

        // Always re-stage fresh (this method only runs once per test process, via the Lazy field
        // above) rather than trusting a previous session's leftover %TEMP% copy — a stale worker
        // binary compared against freshly-rebuilt ZipArchiveService.cs code would silently produce
        // misleading numbers.
        if (Directory.Exists(toolDir))
            Directory.Delete(toolDir, recursive: true);

        Directory.CreateDirectory(parentDir);
        Profile.EnsureExists();
        using (SafeSidHandle sid = Profile.GetSid())
        {
            QuarantineAcl.GrantTraverseOnly(parentDir, sid);

            Directory.CreateDirectory(toolDir);
            // Grant BEFORE copying — an inheritable ACE does not retroactively apply to files
            // that already exist in a folder, only to things created after the grant (the same
            // trap TarSandboxScope's own comment documents for its staged archive file). A
            // self-contained publish output is ~100-160 files; granting after copying would leave
            // all of them unreadable to the AppContainer token.
            QuarantineAcl.GrantReadExecute(toolDir, sid);
        }

        foreach (string file in Directory.GetFiles(publishedDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(publishedDir, file);
            string dest = Path.Combine(toolDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }

        return toolExe;
    }

    private readonly record struct WorkerRunResult(TimeSpan Total, double? InternalMs, int ExitCode, string StdErr);
}
