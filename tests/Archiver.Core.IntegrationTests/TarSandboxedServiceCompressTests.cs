using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F105: exercises TarSandboxedService.CompressAsync against the real system tar.exe.
/// Compression is deliberately unsandboxed (trusted local input, not an untrusted archive being
/// parsed — see SECURITY.md's tar.exe Trust Model), so unlike TarSandboxedServiceExtractTests
/// these tests don't need TarSandboxScope/AppContainer setup. Round-trips through the same
/// service's own ExtractAsync to verify content, matching T-F50's established "round-trip
/// through the real tar.exe" pattern rather than asserting on raw tar.exe output directly.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceCompressTests : IDisposable
{
    private readonly TarSandboxedService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private async Task<string> ExtractAndReadAsync(string archivePath, string relativeEntryPath)
    {
        string destDir = Path.Combine(_temp.Path, "extract-" + Path.GetRandomFileName());
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });
        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        return File.ReadAllText(Path.Combine(destDir, relativeEntryPath));
    }

    [Integration]
    public async Task CompressAsync_PlainTar_RoundTripsFileContent()
    {
        string srcFile = Path.Combine(_temp.Path, "a.txt");
        File.WriteAllText(srcFile, "hello tar");

        var result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [srcFile],
            DestinationFolder = _temp.Path,
            ArchiveName = "out",
            Format = ArchiveContainerFormat.Tar,
        });

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        string destPath = Path.Combine(_temp.Path, "out.tar");
        File.Exists(destPath).Should().BeTrue();
        (await ExtractAndReadAsync(destPath, "a.txt")).Should().Be("hello tar");
    }

    [Integration]
    public async Task CompressAsync_TarGz_RoundTripsFileContent()
    {
        string srcFile = Path.Combine(_temp.Path, "a.txt");
        File.WriteAllText(srcFile, "hello tar.gz");

        var result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [srcFile],
            DestinationFolder = _temp.Path,
            ArchiveName = "out",
            Format = ArchiveContainerFormat.TarGz,
        });

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        string destPath = Path.Combine(_temp.Path, "out.tar.gz");
        File.Exists(destPath).Should().BeTrue();
        (await ExtractAndReadAsync(destPath, "a.txt")).Should().Be("hello tar.gz");
    }

    [SkipIfFormatUnsupported("bz2")]
    public async Task CompressAsync_TarBz2_RoundTripsFileContent()
    {
        string srcFile = Path.Combine(_temp.Path, "a.txt");
        File.WriteAllText(srcFile, "hello tar.bz2");

        var result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [srcFile],
            DestinationFolder = _temp.Path,
            ArchiveName = "out",
            Format = ArchiveContainerFormat.TarBz2,
        });

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        (await ExtractAndReadAsync(Path.Combine(_temp.Path, "out.tar.bz2"), "a.txt")).Should().Be("hello tar.bz2");
    }

    [SkipIfFormatUnsupported("xz")]
    public async Task CompressAsync_TarXz_RoundTripsFileContent()
    {
        string srcFile = Path.Combine(_temp.Path, "a.txt");
        File.WriteAllText(srcFile, "hello tar.xz");

        var result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [srcFile],
            DestinationFolder = _temp.Path,
            ArchiveName = "out",
            Format = ArchiveContainerFormat.TarXz,
        });

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        (await ExtractAndReadAsync(Path.Combine(_temp.Path, "out.tar.xz"), "a.txt")).Should().Be("hello tar.xz");
    }

    [SkipIfFormatUnsupported("zstd")]
    public async Task CompressAsync_TarZst_RoundTripsFileContent()
    {
        string srcFile = Path.Combine(_temp.Path, "a.txt");
        File.WriteAllText(srcFile, "hello tar.zst");

        var result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [srcFile],
            DestinationFolder = _temp.Path,
            ArchiveName = "out",
            Format = ArchiveContainerFormat.TarZst,
        });

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        (await ExtractAndReadAsync(Path.Combine(_temp.Path, "out.tar.zst"), "a.txt")).Should().Be("hello tar.zst");
    }

    [SkipIfFormatUnsupported("lzma")]
    public async Task CompressAsync_TarLzma_RoundTripsFileContent()
    {
        string srcFile = Path.Combine(_temp.Path, "a.txt");
        File.WriteAllText(srcFile, "hello tar.lzma");

        var result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [srcFile],
            DestinationFolder = _temp.Path,
            ArchiveName = "out",
            Format = ArchiveContainerFormat.TarLzma,
        });

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        (await ExtractAndReadAsync(Path.Combine(_temp.Path, "out.tar.lzma"), "a.txt")).Should().Be("hello tar.lzma");
    }

    [Integration]
    public async Task CompressAsync_MultipleSourcesFromDifferentParents_PreservesRelativeStructure()
    {
        string parent1 = Path.Combine(_temp.Path, "p1");
        string parent2 = Path.Combine(_temp.Path, "p2", "sub");
        Directory.CreateDirectory(parent1);
        Directory.CreateDirectory(parent2);
        File.WriteAllText(Path.Combine(parent1, "one.txt"), "one");
        Directory.CreateDirectory(Path.Combine(parent1, "folder"));
        File.WriteAllText(Path.Combine(parent1, "folder", "nested.txt"), "nested");
        File.WriteAllText(Path.Combine(parent2, "..", "two.txt"), "two");

        string srcTwo = Path.Combine(Path.GetDirectoryName(parent2)!, "two.txt");

        var result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [Path.Combine(parent1, "one.txt"), Path.Combine(parent1, "folder"), srcTwo],
            DestinationFolder = _temp.Path,
            ArchiveName = "multi",
            Mode = ArchiveMode.SingleArchive,
            Format = ArchiveContainerFormat.Tar,
        });

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        string destDir = Path.Combine(_temp.Path, "extracted-multi");
        var extractResult = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [Path.Combine(_temp.Path, "multi.tar")],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        // T-F118: "one.txt", "folder/", and "two.txt" are three root-level items with no common
        // containing folder — smart-foldering (now shared with ZipArchiveService) wraps them under
        // a "multi" subfolder named after the archive.
        extractResult.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(destDir, "multi", "one.txt")).Should().Be("one");
        File.ReadAllText(Path.Combine(destDir, "multi", "folder", "nested.txt")).Should().Be("nested");
        File.ReadAllText(Path.Combine(destDir, "multi", "two.txt")).Should().Be("two");
    }

    [Integration]
    public async Task CompressAsync_SeparateArchivesMode_CreatesOneArchivePerSource()
    {
        string src1 = Path.Combine(_temp.Path, "first.txt");
        string src2 = Path.Combine(_temp.Path, "second.txt");
        File.WriteAllText(src1, "first content");
        File.WriteAllText(src2, "second content");

        var result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [src1, src2],
            DestinationFolder = _temp.Path,
            Mode = ArchiveMode.SeparateArchives,
            Format = ArchiveContainerFormat.TarGz,
        });

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        result.CreatedFiles.Should().HaveCount(2);
        (await ExtractAndReadAsync(Path.Combine(_temp.Path, "first.tar.gz"), "first.txt")).Should().Be("first content");
        (await ExtractAndReadAsync(Path.Combine(_temp.Path, "second.tar.gz"), "second.txt")).Should().Be("second content");
    }

    [Integration]
    public async Task CompressAsync_RenameConflict_CreatesNumberedArchiveWithoutOverwriting()
    {
        string srcFile = Path.Combine(_temp.Path, "a.txt");
        File.WriteAllText(srcFile, "new content");
        string existingDest = Path.Combine(_temp.Path, "out.tar");
        File.WriteAllText(existingDest, "not a real tar, just occupying the name");

        var result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [srcFile],
            DestinationFolder = _temp.Path,
            ArchiveName = "out",
            Format = ArchiveContainerFormat.Tar,
            OnConflict = ConflictBehavior.Rename,
        });

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        File.Exists(Path.Combine(_temp.Path, "out (1).tar")).Should().BeTrue();
        File.ReadAllText(existingDest).Should().Be("not a real tar, just occupying the name");
    }

    [Integration]
    public async Task CompressAsync_MissingSource_ReportsErrorInsteadOfThrowing()
    {
        string missing = Path.Combine(_temp.Path, "does-not-exist.txt");

        var result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [missing],
            DestinationFolder = _temp.Path,
            ArchiveName = "out",
            Format = ArchiveContainerFormat.Tar,
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.SourcePath == missing);
        File.Exists(Path.Combine(_temp.Path, "out.tar")).Should().BeFalse();
        File.Exists(Path.Combine(_temp.Path, "out.tar.tmp")).Should().BeFalse();
    }

    // Confirms the --options <filter>:compression-level=N mapping (T-F105's Phase 0 finding) has
    // a real effect, not just that the flag is accepted — a compressible, repetitive payload
    // large enough for the difference to be measurable regardless of gzip's small-input framing
    // overhead.
    [Integration]
    public async Task CompressAsync_NoCompressionVsSmallestSize_ProducesDifferentSizedOutput()
    {
        string srcFile = Path.Combine(_temp.Path, "big.txt");
        File.WriteAllText(srcFile, string.Concat(Enumerable.Repeat("AAAAAAAAAA", 100_000)));

        var noCompression = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [srcFile],
            DestinationFolder = _temp.Path,
            ArchiveName = "none",
            Format = ArchiveContainerFormat.TarGz,
            CompressionLevel = CompressionLevel.NoCompression,
        });
        var smallest = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [srcFile],
            DestinationFolder = _temp.Path,
            ArchiveName = "smallest",
            Format = ArchiveContainerFormat.TarGz,
            CompressionLevel = CompressionLevel.SmallestSize,
        });

        noCompression.Success.Should().BeTrue();
        smallest.Success.Should().BeTrue();

        long noCompressionSize = new FileInfo(Path.Combine(_temp.Path, "none.tar.gz")).Length;
        long smallestSize = new FileInfo(Path.Combine(_temp.Path, "smallest.tar.gz")).Length;
        smallestSize.Should().BeLessThan(noCompressionSize);
    }

    // T-F140 regression: CompressToArchiveAsync used to compute its progress percentage as
    // reportedFiles * 100 / entryCount, where entryCount only counted top-level SELECTED SOURCE
    // PATHS (here: 1, this single folder) rather than the real number of entries tar.exe recurses
    // into and emits a "-v" line for. Archiving the very first file inside the folder already
    // computed 100/1 = 100%, clamped to 99 by Math.Min — every subsequent file repeated that same
    // 99% for the rest of the run (this is exactly the real user report: a few large folders,
    // dialog stuck near-instantly, not moving again until completion). With the real recursive
    // entry count as denominator, the percentage must advance across multiple distinct values.
    [Integration]
    public async Task CompressAsync_TarWithManyFilesInFolder_ProgressAdvancesGraduallyNotClampedImmediately()
    {
        string sourceDir = Path.Combine(_temp.Path, "many");
        Directory.CreateDirectory(sourceDir);
        const int fileCount = 40;
        for (int i = 0; i < fileCount; i++)
            File.WriteAllText(Path.Combine(sourceDir, $"f{i}.txt"), $"content {i}");

        var reports = new List<ProgressReport>();
        var progress = new Progress<ProgressReport>(reports.Add);

        var result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir],
            DestinationFolder = _temp.Path,
            ArchiveName = "many",
            Format = ArchiveContainerFormat.Tar,
        }, progress);

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));

        // Progress<T> marshals callbacks onto a captured SynchronizationContext asynchronously —
        // give the final posted callback a moment to actually run before asserting on `reports`.
        await WaitUntilAsync(() => reports.Count > 0 && reports[^1].Percent == 100, TimeSpan.FromSeconds(5));

        var distinctNonTerminalPercents = reports.Where(r => r.Percent < 100).Select(r => r.Percent).Distinct().ToList();
        distinctNonTerminalPercents.Should().HaveCountGreaterThan(1,
            "the old top-level-path-count denominator clamped to a single repeated 99% value " +
            "almost immediately for any folder with more than a handful of files");
    }

    // T-F140 follow-up: the dialog used to show a bare percentage for TAR — no filename (SetLine(1)
    // was never called, since CurrentFile was always null) and no byte totals (BytesTransferred/
    // TotalBytes were always 0, so FormatStatus fell back to "{Percent}%" with nothing else),
    // unlike ZIP's "{Percent}% · {bytes}/{total}" plus a live filename. tar.exe's own "-v" output
    // ("a <name>" per entry) already carried the filename — it was just being discarded.
    [Integration]
    public async Task CompressAsync_TarWithMultipleFiles_ReportsRealFilenameAndByteTotals()
    {
        string sourceDir = Path.Combine(_temp.Path, "withbytes");
        Directory.CreateDirectory(sourceDir);
        byte[] contentA = new byte[10_000];
        byte[] contentB = new byte[20_000];
        Array.Fill(contentA, (byte)'A');
        Array.Fill(contentB, (byte)'B');
        File.WriteAllBytes(Path.Combine(sourceDir, "a.bin"), contentA);
        File.WriteAllBytes(Path.Combine(sourceDir, "b.bin"), contentB);
        long expectedTotalBytes = contentA.Length + contentB.Length;

        var reports = new List<ProgressReport>();
        var progress = new Progress<ProgressReport>(reports.Add);

        var result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir],
            DestinationFolder = _temp.Path,
            ArchiveName = "withbytes",
            Format = ArchiveContainerFormat.Tar,
        }, progress);

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        await WaitUntilAsync(() => reports.Count > 0 && reports[^1].Percent == 100, TimeSpan.FromSeconds(5));

        reports.Should().Contain(r => r.CurrentFile != null && r.CurrentFile.Contains("a.bin"),
            "the real entry name from tar.exe's own \"-v\" output must reach the dialog, not stay null");
        reports.Should().Contain(r => r.CurrentFile != null && r.CurrentFile.Contains("b.bin"));
        reports.Should().OnlyContain(r => r.CurrentFile == null || !r.CurrentFile.StartsWith("a ", StringComparison.Ordinal),
            "the \"a \" prefix from tar's raw verbose line must be stripped, not shown to the user");

        reports[^1].TotalBytes.Should().Be(expectedTotalBytes,
            "the real recursive byte sum must reach the dialog instead of the old hardcoded 0");
        reports[^1].BytesTransferred.Should().Be(expectedTotalBytes);
        reports.Should().Contain(r => r.TotalBytes == expectedTotalBytes && r.BytesTransferred > 0 && r.BytesTransferred <= expectedTotalBytes);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met within the timeout.");
            await Task.Delay(10);
        }
    }
}
