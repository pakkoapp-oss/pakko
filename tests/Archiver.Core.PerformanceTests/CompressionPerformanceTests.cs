using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;
using Xunit.Abstractions;

namespace Archiver.Core.PerformanceTests;

/// <summary>
/// T-F114: compares Pakko's own ZIP path (System.IO.Compression) against a vendored 7za.exe
/// reference, run back-to-back on the same machine in the same test method, asserting on the
/// *ratio* between their elapsed times rather than an absolute threshold — the only pattern that
/// generalizes across arbitrary, never-before-seen machines (see DECISIONS.md's T-F114 entry for
/// the BenchmarkDotNet/criterion.rs/benchstat research behind this choice). A single discarded
/// warmup pass precedes the timed pass for both engines, absorbing JIT/cold-start skew.
///
/// Realistic sensitivity is "catches a ~2x+ slowdown," not a fine-grained regression detector —
/// intentional, matching this repo's actual regression history (gross, not subtle) and the lack
/// of any CI here to make repeated-run statistics meaningful. Extraction scenarios extract from a
/// single shared reference ZIP (built once via 7za, untimed) so both engines process byte-identical
/// input rather than each extracting their own separately-created archive.
/// </summary>
public sealed class CompressionPerformanceTests : IDisposable
{
    private const double ToleranceMultiplier = 3.0;

    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();
    private readonly ITestOutputHelper _output;

    public CompressionPerformanceTests(ITestOutputHelper output) => _output = output;

    public void Dispose() => _temp.Dispose();

    // T-F114: the one-large-file scenarios are gated behind their own explicit category, not the
    // default Category=Slow run — per user request, the "short" perf scenarios (ManySmallFiles,
    // Hybrid, below) always run under Category=Slow; this one (and its Extract counterpart) only
    // run on demand via `dotnet test --filter "Category=VeryLarge"`.
    [Fact]
    [Trait("Category", "VeryLarge")]
    public async Task ArchiveAsync_OneLargeFile_WithinToleranceOfSevenZipReference()
    {
        // observed 2026-07-17, see DECISIONS.md; re-verified unaffected 2026-07-18 after T-F35
        // (1.23 measured — a single large file always stays on the untouched sequential path,
        // never routed through T-F35's parallel pipeline, by design)
        const double calibratedBaselineRatio = 1.22;
        string sourceDir = PerformanceFixtures.CreateOneLargeFileFolder(_temp.Path);

        await ArchiveWithPakkoTimed(sourceDir, Path.Combine(_temp.Path, "warmup_pakko.zip"));
        SevenZipRunner.Archive(sourceDir, Path.Combine(_temp.Path, "warmup_7za.zip"));

        string pakkoZip = Path.Combine(_temp.Path, "pakko.zip");
        var pakkoElapsed = await ArchiveWithPakkoTimed(sourceDir, pakkoZip);
        var referenceElapsed = SevenZipRunner.Archive(sourceDir, Path.Combine(_temp.Path, "reference.zip"));

        File.Exists(pakkoZip).Should().BeTrue();
        new FileInfo(pakkoZip).Length.Should().BeGreaterThan(0);
        AssertRatio("Archive/OneLargeFile", pakkoElapsed, referenceElapsed, calibratedBaselineRatio);
    }

    [Fact]
    [Trait("Category", "VeryLarge")]
    public async Task ExtractAsync_OneLargeFile_WithinToleranceOfSevenZipReference()
    {
        const double calibratedBaselineRatio = 1.06; // observed 2026-07-17, see DECISIONS.md
        string sourceDir = PerformanceFixtures.CreateOneLargeFileFolder(_temp.Path);
        string referenceZip = Path.Combine(_temp.Path, "shared.zip");
        SevenZipRunner.Archive(sourceDir, referenceZip); // untimed setup — shared input for both engines

        await ExtractWithPakkoTimed(referenceZip, Path.Combine(_temp.Path, "warmup_extract_pakko"));
        SevenZipRunner.Extract(referenceZip, Path.Combine(_temp.Path, "warmup_extract_7za"));

        string pakkoDest = Path.Combine(_temp.Path, "extract_pakko");
        var pakkoElapsed = await ExtractWithPakkoTimed(referenceZip, pakkoDest);
        var referenceElapsed = SevenZipRunner.Extract(referenceZip, Path.Combine(_temp.Path, "extract_7za"));

        Directory.GetFiles(pakkoDest, "*", SearchOption.AllDirectories).Should().HaveCount(1);
        AssertRatio("Extract/OneLargeFile", pakkoElapsed, referenceElapsed, calibratedBaselineRatio);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ArchiveAsync_ManySmallFiles_WithinToleranceOfSevenZipReference()
    {
        // Re-measured 2026-07-18 after T-F35 (parallel SingleArchive pipeline): 2.39, down from
        // the original 6.02 baseline (observed 2026-07-17, before T-F35) — a ~2.5x real
        // improvement, consistent with T-F35's goal of closing this specific gap. Further
        // re-measured same day after eliminating WorkItemEnumerator's redundant per-file stat
        // calls (DirectoryInfo.EnumerateFiles() instead of Directory.EnumerateFiles() + separate
        // FileInfo.Length/File.GetLastWriteTime/File.GetAttributes calls — 3 fewer native calls
        // per file): 2.2 average across 3 runs (2.11-2.34) — a modest, real, but small further
        // improvement, confirming per-file stat overhead was NOT the dominant remaining cost.
        // The bulk of the remaining gap vs. 7za is more likely per-entry managed allocations
        // (MemoryStream/byte[]/record per file) and Task/async orchestration overhead at
        // thousands of files — not yet profiled/fixed, see DECISIONS.md's T-F35 entry.
        const double calibratedBaselineRatio = 2.2;
        string sourceDir = PerformanceFixtures.CreateManySmallFilesFolder(_temp.Path);

        await ArchiveWithPakkoTimed(sourceDir, Path.Combine(_temp.Path, "warmup_pakko.zip"));
        SevenZipRunner.Archive(sourceDir, Path.Combine(_temp.Path, "warmup_7za.zip"));

        string pakkoZip = Path.Combine(_temp.Path, "pakko.zip");
        var pakkoElapsed = await ArchiveWithPakkoTimed(sourceDir, pakkoZip);
        var referenceElapsed = SevenZipRunner.Archive(sourceDir, Path.Combine(_temp.Path, "reference.zip"));

        File.Exists(pakkoZip).Should().BeTrue();
        new FileInfo(pakkoZip).Length.Should().BeGreaterThan(0);
        AssertRatio("Archive/ManySmallFiles", pakkoElapsed, referenceElapsed, calibratedBaselineRatio);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ExtractAsync_ManySmallFiles_WithinToleranceOfSevenZipReference()
    {
        const double calibratedBaselineRatio = 1.58; // observed 2026-07-17, see DECISIONS.md
        string sourceDir = PerformanceFixtures.CreateManySmallFilesFolder(_temp.Path);
        string referenceZip = Path.Combine(_temp.Path, "shared.zip");
        SevenZipRunner.Archive(sourceDir, referenceZip);

        await ExtractWithPakkoTimed(referenceZip, Path.Combine(_temp.Path, "warmup_extract_pakko"));
        SevenZipRunner.Extract(referenceZip, Path.Combine(_temp.Path, "warmup_extract_7za"));

        string pakkoDest = Path.Combine(_temp.Path, "extract_pakko");
        var pakkoElapsed = await ExtractWithPakkoTimed(referenceZip, pakkoDest);
        var referenceElapsed = SevenZipRunner.Extract(referenceZip, Path.Combine(_temp.Path, "extract_7za"));

        Directory.GetFiles(pakkoDest, "*", SearchOption.AllDirectories)
            .Should().HaveCount(PerformanceFixtures.ManySmallFilesCount);
        AssertRatio("Extract/ManySmallFiles", pakkoElapsed, referenceElapsed, calibratedBaselineRatio);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ArchiveAsync_Hybrid_WithinToleranceOfSevenZipReference()
    {
        // Re-measured 2026-07-18 after T-F35: 3.03, down from the original 3.47 baseline (observed
        // 2026-07-17, before T-F35) — a smaller improvement than ManySmallFiles, as expected: the
        // 4 MiB Zip64/parallel-eligibility threshold (see DECISIONS.md's T-F35 entry) routes this
        // fixture's 5-20 MB "medium" files through the same untouched sequential streaming path as
        // before, so only the 500 small files' portion of this scenario sped up.
        const double calibratedBaselineRatio = 3.03;
        string sourceDir = PerformanceFixtures.CreateHybridFolder(_temp.Path);

        await ArchiveWithPakkoTimed(sourceDir, Path.Combine(_temp.Path, "warmup_pakko.zip"));
        SevenZipRunner.Archive(sourceDir, Path.Combine(_temp.Path, "warmup_7za.zip"));

        string pakkoZip = Path.Combine(_temp.Path, "pakko.zip");
        var pakkoElapsed = await ArchiveWithPakkoTimed(sourceDir, pakkoZip);
        var referenceElapsed = SevenZipRunner.Archive(sourceDir, Path.Combine(_temp.Path, "reference.zip"));

        File.Exists(pakkoZip).Should().BeTrue();
        new FileInfo(pakkoZip).Length.Should().BeGreaterThan(0);
        AssertRatio("Archive/Hybrid", pakkoElapsed, referenceElapsed, calibratedBaselineRatio);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ExtractAsync_Hybrid_WithinToleranceOfSevenZipReference()
    {
        const double calibratedBaselineRatio = 1.56; // observed 2026-07-17, see DECISIONS.md
        string sourceDir = PerformanceFixtures.CreateHybridFolder(_temp.Path);
        string referenceZip = Path.Combine(_temp.Path, "shared.zip");
        SevenZipRunner.Archive(sourceDir, referenceZip);

        await ExtractWithPakkoTimed(referenceZip, Path.Combine(_temp.Path, "warmup_extract_pakko"));
        SevenZipRunner.Extract(referenceZip, Path.Combine(_temp.Path, "warmup_extract_7za"));

        string pakkoDest = Path.Combine(_temp.Path, "extract_pakko");
        var pakkoElapsed = await ExtractWithPakkoTimed(referenceZip, pakkoDest);
        var referenceElapsed = SevenZipRunner.Extract(referenceZip, Path.Combine(_temp.Path, "extract_7za"));

        Directory.GetFiles(pakkoDest, "*", SearchOption.AllDirectories)
            .Should().HaveCount(PerformanceFixtures.HybridSmallFilesCount + PerformanceFixtures.HybridMediumFilesCount);
        AssertRatio("Extract/Hybrid", pakkoElapsed, referenceElapsed, calibratedBaselineRatio);
    }

    private async Task<TimeSpan> ArchiveWithPakkoTimed(string sourceDir, string destinationZipPath)
    {
        return await TimeAsync(async () =>
        {
            var result = await _sut.ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [sourceDir],
                DestinationFolder = Path.GetDirectoryName(destinationZipPath)!,
                ArchiveName = Path.GetFileNameWithoutExtension(destinationZipPath)
            });
            result.Success.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        });
    }

    private async Task<TimeSpan> ExtractWithPakkoTimed(string archivePath, string destinationDir)
    {
        return await TimeAsync(async () =>
        {
            var result = await _sut.ExtractAsync(new ExtractOptions
            {
                ArchivePaths = [archivePath],
                DestinationFolder = destinationDir,
                Mode = ExtractMode.SingleFolder
            });
            result.Success.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        });
    }

    private void AssertRatio(
        string operationLabel, TimeSpan pakkoElapsed, TimeSpan referenceElapsed,
        double calibratedBaselineRatio, double toleranceMultiplier = ToleranceMultiplier)
    {
        double ratio = pakkoElapsed.TotalMilliseconds / referenceElapsed.TotalMilliseconds;
        _output.WriteLine($"{operationLabel} — Pakko: {pakkoElapsed}, 7za: {referenceElapsed}, ratio: {ratio:F3}");
        ratio.Should().BeLessThanOrEqualTo(
            calibratedBaselineRatio * toleranceMultiplier,
            because: $"{operationLabel}: Pakko took {pakkoElapsed} vs. 7za's {referenceElapsed} " +
                      $"(ratio {ratio:F2}) — this suggests a real regression, not machine noise");
    }

    private static async Task<TimeSpan> TimeAsync(Func<Task> action)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await action();
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }
}
