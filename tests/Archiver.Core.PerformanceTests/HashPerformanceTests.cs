using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;
using Xunit.Abstractions;

namespace Archiver.Core.PerformanceTests;

/// <summary>
/// T-F128 follow-up: mirrors T-F114's <see cref="CompressionPerformanceTests"/> pattern exactly
/// (same-machine, same-invocation ratio against the vendored 7za.exe reference, one discarded
/// warmup pass, <c>Category</c> trait gating) — added after a real on-device folder hash felt
/// slower than NanaZip. 7-Zip's own <c>h</c> command runs the identical NanaZip/HashCalc.cpp
/// algorithm this project's own hashing reproduces, so it's the same valid reference T-F114
/// already established for compression/extraction.
/// </summary>
public sealed class HashPerformanceTests : IDisposable
{
    private const double ToleranceMultiplier = 3.0;

    private readonly TempDirectory _temp = new();
    private readonly ITestOutputHelper _output;

    public HashPerformanceTests(ITestOutputHelper output) => _output = output;

    public void Dispose() => _temp.Dispose();

    [Fact]
    [Trait("Category", "VeryLarge")]
    public async Task HashAsync_OneLargeFile_WithinToleranceOfSevenZipReference()
    {
        // Ratio history (300 MB file, CRC-32), 2026-07-20:
        //   9.00  observed first — Crc32.Accumulator was a byte-at-a-time single-table lookup.
        //   7.46  after switching to slice-by-8 (uint[8][256] jagged tables).
        //   6.45  after flattening to one uint[2048] array (fewer pointer dereferences, better
        //         cache locality). A throwaway in-memory-only benchmark isolated this as
        //         genuinely CRC-32-compute-bound, not I/O (plain file reads hit 4+ GB/s, even
        //         async ReadAsync on a useAsync:false FileStream hit ~1.8 GB/s; pure
        //         Accumulator.Update on an in-memory 300 MB buffer took ~1s on its own) — the
        //         existing Parallel.ForEachAsync pipeline gave zero benefit here since it only
        //         parallelizes ACROSS files, and this scenario is a single file.
        //   ~1.3-2.9  after adding genuine intra-file parallelism: large files split into 4 MiB
        //         chunks, each hashed independently (fresh Accumulator per chunk), folded back
        //         together in original byte order via the new Crc32.Combine (a faithful
        //         reimplementation of zlib's public-domain crc32_combine — see Crc32Tests.cs).
        //         12 runs on this dev machine (12 logical cores) clustered bimodally: ~7 runs at
        //         1.31-1.37, ~4 at 2.42-2.93, one outlier at 0.95 (Pakko briefly faster than
        //         7za). The bimodal split tracks OS scheduling contention when fully saturating
        //         every core for CPU-bound work, not anything wrong with the algorithm — every
        //         run stays comfortably inside this baseline's 3x tolerance either way. Not
        //         chasing further: closing the remaining typical-case gap to 7-Zip's own CRC-32
        //         (likely hardware SSE4.2/PCLMULQDQ-accelerated) would need SIMD intrinsics — a
        //         materially bigger, platform-specific undertaking explicitly out of scope; see
        //         DECISIONS.md's T-F128 entry.
        const double calibratedBaselineRatio = 1.35;
        string sourceDir = PerformanceFixtures.CreateOneLargeFileFolder(_temp.Path);
        string filePath = Directory.GetFiles(sourceDir)[0];

        await HashWithPakkoTimed(filePath); // warmup
        SevenZipRunner.Hash(filePath, "CRC32"); // warmup

        var pakkoElapsed = await HashWithPakkoTimed(filePath);
        var referenceElapsed = SevenZipRunner.Hash(filePath, "CRC32");

        AssertRatio("Hash/OneLargeFile", pakkoElapsed, referenceElapsed, calibratedBaselineRatio);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task HashAsync_ManyFilesAndFolders_WithinToleranceOfSevenZipReference()
    {
        // Observed 1.2-1.5 across several runs (3,000 files / 300 subfolders, CRC-32), 2026-07-20
        // — small absolute times (~200-280ms) mean run-to-run noise dominates more here than in
        // OneLargeFile; 1.3 sits in the middle of the observed range, same convention T-F114's own
        // ManySmallFiles/Hybrid scenarios already use for this reason.
        const double calibratedBaselineRatio = 1.3;
        string sourceDir = PerformanceFixtures.CreateManyFilesAndFoldersFolder(_temp.Path);

        await HashWithPakkoTimed(sourceDir); // warmup
        SevenZipRunner.Hash(sourceDir, "CRC32", recursive: true); // warmup

        var pakkoElapsed = await HashWithPakkoTimed(sourceDir);
        var referenceElapsed = SevenZipRunner.Hash(sourceDir, "CRC32", recursive: true);

        AssertRatio("Hash/ManyFilesAndFolders", pakkoElapsed, referenceElapsed, calibratedBaselineRatio);
    }

    private static async Task<TimeSpan> HashWithPakkoTimed(string path)
    {
        return await TimeAsync(async () =>
        {
            var result = await FileHashService.ComputeAsync([path], HashAlgorithmKind.Crc32, null, CancellationToken.None);
            result.Entries.Should().OnlyContain(e => e.Error == null);
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
