using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// Exercises TarProcessService.ExtractAsync (T-F49) against the real system tar.exe. Fixtures
/// are self-generated via TarBuilder (raw USTAR bytes) rather than a prebuilt corpus — T-F50
/// owns the full multi-format fixture set later. The reject-case tests reproduce the exploit
/// documented in DECISIONS.md's T-F49 entry, most importantly the symlink-escape test, which is
/// a regression test for a confirmed sandbox escape found while designing this pipeline.
/// </summary>
public sealed class TarProcessServiceExtractTests : IDisposable
{
    private readonly TarProcessService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Integration]
    public async Task ExtractAsync_ValidTar_ExtractsFilesWithContent()
    {
        string archivePath = Path.Combine(_temp.Path, "valid.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "a.txt", Content = Encoding.ASCII.GetBytes("hello") },
            new TarBuilder.Entry { Name = "sub/b.txt", Content = Encoding.ASCII.GetBytes("world") },
        ]);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        File.ReadAllText(Path.Combine(destDir, "a.txt")).Should().Be("hello");
        File.ReadAllText(Path.Combine(destDir, "sub", "b.txt")).Should().Be("world");
    }

    [Integration]
    public async Task ExtractAsync_RenameConflict_CreatesNumberedCopyWithoutOverwriting()
    {
        string archivePath = Path.Combine(_temp.Path, "valid.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "a.txt", Content = Encoding.ASCII.GetBytes("new content") },
        ]);

        string destDir = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(destDir);
        File.WriteAllText(Path.Combine(destDir, "a.txt"), "original content");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Rename,
        });

        result.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(destDir, "a.txt")).Should().Be("original content");
        File.ReadAllText(Path.Combine(destDir, "a (1).txt")).Should().Be("new content");
    }

    // T-F87: an archive whose only entry conflict-skips must not appear in CreatedFiles, and
    // must record a whole-archive SkippedFile (Path == archivePath) — the signal MainViewModel
    // uses to avoid deleting a source that was never actually extracted when
    // DeleteAfterOperation is on.
    [Integration]
    public async Task ExtractAsync_AllEntriesConflictSkipped_ExcludesArchiveFromCreatedFilesAndRecordsWholeArchiveSkip()
    {
        string archivePath = Path.Combine(_temp.Path, "valid.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "a.txt", Content = Encoding.ASCII.GetBytes("new content") },
        ]);

        string destDir = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(destDir);
        File.WriteAllText(Path.Combine(destDir, "a.txt"), "original content");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Skip,
        });

        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().Contain(s => s.Path == archivePath);
        File.ReadAllText(Path.Combine(destDir, "a.txt")).Should().Be("original content");
    }

    [Integration]
    public async Task ExtractAsync_ArchiveHasZoneIdentifier_PropagatesMotwToExtractedFile()
    {
        string archivePath = Path.Combine(_temp.Path, "valid.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "a.txt", Content = Encoding.ASCII.GetBytes("hello") },
        ]);
        File.WriteAllText(archivePath + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(destDir, "a.txt") + ":Zone.Identifier").Should().BeTrue();
    }

    [Integration]
    public async Task ExtractAsync_ArchiveWithParentTraversalEntry_RejectsWholeArchive()
    {
        string archivePath = Path.Combine(_temp.Path, "evil_traversal.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "innocent.txt", Content = Encoding.ASCII.GetBytes("fine") },
            new TarBuilder.Entry { Name = "../evil.txt", Content = Encoding.ASCII.GetBytes("payload") },
        ]);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        File.Exists(Path.Combine(_temp.Path, "evil.txt")).Should().BeFalse();
    }

    [Integration]
    public async Task ExtractAsync_ArchiveWithAlternateDataStreamEntry_RejectsWholeArchive()
    {
        string archivePath = Path.Combine(_temp.Path, "evil_ads.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "file.txt:hidden", Content = Encoding.ASCII.GetBytes("payload") },
        ]);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle();
    }

    [Integration]
    public async Task ExtractAsync_ArchiveWithReservedDeviceNameEntry_RejectsWholeArchive()
    {
        string archivePath = Path.Combine(_temp.Path, "evil_reserved.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "CON.txt", Content = Encoding.ASCII.GetBytes("payload") },
        ]);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle();
    }

    // T-F50: a truncated/malformed tar - tar.exe's own "-tf" listing pass (the first half of
    // ScanForUnsafeEntriesAsync's pre-scan) fails with a nonzero exit code before any entry
    // name/type check runs, surfacing as an IOException -> ArchiveError, not an unhandled
    // exception or a silently-empty result.
    [Integration]
    public async Task ExtractAsync_TruncatedTar_ReportsErrorAndExtractsNothing()
    {
        string archivePath = Path.Combine(_temp.Path, "corrupted.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "a.txt", Content = Encoding.ASCII.GetBytes("this content will be cut off") },
        ]);
        byte[] truncated = File.ReadAllBytes(archivePath)[..300]; // cut mid-header/data, well short of the real length
        File.WriteAllBytes(archivePath, truncated);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.CreatedFiles.Should().BeEmpty();
    }

    // Regression test for the confirmed exploit in DECISIONS.md's T-F49 entry: a symlink entry
    // named "link" targeting ".." followed by "link/escaped.txt" caused raw tar.exe to write
    // escaped.txt one directory level above the extraction root. This proves the pre-scan blocks
    // the whole archive before -xf ever runs, so the escape can never be attempted.
    [Integration]
    public async Task ExtractAsync_ArchiveWithSymlinkEntry_RejectsWholeArchiveAndDoesNotEscape()
    {
        string archivePath = Path.Combine(_temp.Path, "evil_symlink.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "innocent.txt", Content = Encoding.ASCII.GetBytes("fine") },
            new TarBuilder.Entry { Name = "link", TypeFlag = '2', LinkName = ".." },
            new TarBuilder.Entry { Name = "link/escaped.txt", Content = Encoding.ASCII.GetBytes("escaped payload") },
        ]);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        File.Exists(Path.Combine(_temp.Path, "escaped.txt")).Should().BeFalse();
        Directory.Exists(Path.Combine(destDir, "link")).Should().BeFalse();
    }
}
