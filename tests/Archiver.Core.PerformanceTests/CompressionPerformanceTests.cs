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
        // observed 2026-07-17, see DECISIONS.md; re-verified unaffected 2026-07-18 after T-F35's
        // pipeline (1.23), the ComputeSingleArchiveTotals enumeration-merge fix (1.20), and the
        // temp-file-compression redesign that removed the size ceiling entirely (1.18) — a single
        // large file's total FILE COUNT (1) never crosses ArchiveAsync's own 64-file gate, so it
        // always stays on the completely untouched original sequential path regardless of anything
        // done inside the parallel pipeline itself, by design.
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
        // Ratio history for this scenario (5,000-file fixture), all same-day 2026-07-18 unless noted:
        //   6.02  observed 2026-07-17, before T-F35 (fully sequential SingleArchive)
        //   2.39  after T-F35's parallel pipeline shipped (~2.5x real improvement)
        //   2.2   after fixing WorkItemEnumerator's redundant per-file stat calls (DirectoryInfo.
        //         EnumerateFiles() instead of separate FileInfo.Length/GetLastWriteTime/
        //         GetAttributes calls per file) — modest, confirmed NOT the dominant cost
        //   1.45  after a profiling pass found ArchiveAsync's SingleArchive branch walked the same
        //         directory tree TWICE more before the pipeline even started (ComputeTotalBytes for
        //         progress + the gate's own file count) — merged into one combined
        //         ComputeSingleArchiveTotals walk (also applying the same DirectoryInfo fix). This
        //         was the dominant remaining cost, not per-file allocations or Task/Channel
        //         overhead as originally hypothesized — see DECISIONS.md's T-F35 profiling entry
        //         for the full stage-by-stage breakdown that found it.
        //   ~1.0  after replacing the file-size ceiling entirely: files above the in-memory
        //         threshold (now 1 MiB, was 4 MiB) are now ALSO compressed in parallel, into a
        //         private temp file per worker instead of a byte[] — this scenario's files are all
        //         well under 1 MiB either way, so this specific number is unaffected by that change
        //         (0.92-1.03 observed across 5 runs, essentially parity with 7za — the small
        //         variance/occasional sub-1.0 reading is ordinary run-to-run noise on a sub-second
        //         operation, not a real further improvement to this exact scenario).
        const double calibratedBaselineRatio = 1.0;
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
        // Ratio history (500 small + 4 medium 5-20MB files), all 2026-07-18 unless noted:
        //   3.47  observed 2026-07-17, before T-F35
        //   3.03  after T-F35's parallel pipeline — smaller improvement than ManySmallFiles, as
        //         expected: the 4 MiB Zip64/parallel-eligibility threshold (DECISIONS.md's T-F35
        //         entry) routes this fixture's medium files through the untouched sequential path
        //   2.85  after the ComputeSingleArchiveTotals enumeration-merge fix (same fix as
        //         ManySmallFiles — this scenario's directory walk was redundant too)
        //   ~1.3  after replacing the 4 MiB size ceiling with per-worker temp-file compression —
        //         this scenario's 4 "medium" 5-20 MB files were exactly the ones excluded from
        //         parallelism by the old design (the whole reason this scenario's ratio lagged
        //         ManySmallFiles' this whole session); now parallel too. 0.93-1.54 observed across
        //         5 runs — more variance than ManySmallFiles since 4 medium-file compressions
        //         landing in different relative positions across runs shifts the total more.
        const double calibratedBaselineRatio = 1.3;
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
