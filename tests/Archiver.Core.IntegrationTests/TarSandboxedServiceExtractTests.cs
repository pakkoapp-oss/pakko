using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// Exercises TarSandboxedService.ExtractAsync (T-F49, sandboxed since T-F52) against the real
/// system tar.exe. Fixtures are self-generated via TarBuilder (raw USTAR bytes) rather than a
/// prebuilt corpus — T-F50 owns the full multi-format fixture set later. The reject-case tests
/// reproduce the exploit documented in DECISIONS.md's T-F49 entry, most importantly the
/// symlink-escape test, which is a regression test for a confirmed sandbox escape found while
/// designing this pipeline.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceExtractTests : IDisposable
{
    private readonly TarSandboxedService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // T-F118: "a.txt" (root file) + "sub/b.txt" (nested) is a multi-root archive — no single
    // common containing folder — so this now lands under a "valid" subfolder named after the
    // archive, matching ZipArchiveService's smart-foldering for the identical shape.
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
        File.ReadAllText(Path.Combine(destDir, "valid", "a.txt")).Should().Be("hello");
        File.ReadAllText(Path.Combine(destDir, "valid", "sub", "b.txt")).Should().Be("world");
    }

    // T-F118: mirrors ZipArchiveServiceExtractTests.ExtractAsync_SingleRootFolder_
    // ExtractsWithoutDoubleNesting — an archive whose every entry sits under one common top-level
    // folder unwraps that folder entirely rather than doubly nesting it under destDir.
    [Integration]
    public async Task ExtractAsync_SingleRootFolder_ExtractsWithoutDoubleNesting()
    {
        string archivePath = Path.Combine(_temp.Path, "wrapped.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "myFolder/a.txt", Content = Encoding.ASCII.GetBytes("hello") },
            new TarBuilder.Entry { Name = "myFolder/b.txt", Content = Encoding.ASCII.GetBytes("world") },
        ]);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(destDir, "a.txt")).Should().BeTrue();
        File.Exists(Path.Combine(destDir, "b.txt")).Should().BeTrue();
        Directory.Exists(Path.Combine(destDir, "myFolder")).Should().BeFalse();
    }

    // T-F118: mirrors ZipArchiveServiceExtractTests.ExtractAsync_MultipleRootItems_
    // CreatesSubfolderNamedAfterArchive — two root-level files with no common containing folder.
    [Integration]
    public async Task ExtractAsync_MultipleRootItems_CreatesSubfolderNamedAfterArchive()
    {
        string archivePath = Path.Combine(_temp.Path, "bundle.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "file1.txt", Content = Encoding.ASCII.GetBytes("one") },
            new TarBuilder.Entry { Name = "file2.txt", Content = Encoding.ASCII.GetBytes("two") },
        ]);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(destDir, "bundle", "file1.txt")).Should().BeTrue();
        File.Exists(Path.Combine(destDir, "bundle", "file2.txt")).Should().BeTrue();
    }

    // T-F118: SeparateFolders mode already isolates destDir per-archive (alreadyIsolated=true) —
    // smart-foldering must not double-wrap on top of that, matching ZIP's identical rule.
    [Integration]
    public async Task ExtractAsync_SeparateFoldersMode_MultiRootArchive_DoesNotDoubleWrap()
    {
        string archivePath = Path.Combine(_temp.Path, "bundle.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "file1.txt", Content = Encoding.ASCII.GetBytes("one") },
            new TarBuilder.Entry { Name = "file2.txt", Content = Encoding.ASCII.GetBytes("two") },
        ]);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SeparateFolders,
        });

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(destDir, "bundle", "file1.txt")).Should().BeTrue();
        File.Exists(Path.Combine(destDir, "bundle", "file2.txt")).Should().BeTrue();
        Directory.Exists(Path.Combine(destDir, "bundle", "bundle")).Should().BeFalse();
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

    // T-F06: per-entry Ask conflict resolution against a real tar.exe extraction — mirrors
    // ZipArchiveServiceExtractTests' equivalent case.
    [Integration]
    public async Task ExtractAsync_ConflictAsk_RenameResolution_CreatesNumberedCopyWithoutOverwriting()
    {
        string archivePath = Path.Combine(_temp.Path, "valid.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "a.txt", Content = Encoding.ASCII.GetBytes("new content") },
        ]);

        string destDir = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(destDir);
        File.WriteAllText(Path.Combine(destDir, "a.txt"), "original content");

        int callCount = 0;
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Ask,
            ResolveConflictAsync = _ =>
            {
                callCount++;
                return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Rename });
            }
        });

        result.Success.Should().BeTrue();
        callCount.Should().Be(1);
        File.ReadAllText(Path.Combine(destDir, "a.txt")).Should().Be("original content");
        File.ReadAllText(Path.Combine(destDir, "a (1).txt")).Should().Be("new content");
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
    public async Task ExtractAsync_MotwModeDisabled_DoesNotPropagateZoneIdentifier()
    {
        string archivePath = Path.Combine(_temp.Path, "valid.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "a.txt", Content = Encoding.ASCII.GetBytes("hello") },
        ]);
        File.WriteAllText(archivePath + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        string destDir = Path.Combine(_temp.Path, "out");
        var sut = new TarSandboxedService(new GroupPolicyOptions { MotwMode = MotwMode.Disabled });
        var result = await sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(destDir, "a.txt") + ":Zone.Identifier").Should().BeFalse();
    }

    [Integration]
    public async Task ExtractAsync_MotwModeUnsafeExtensionsOnly_PropagatesOnlyToUnsafeExtensions()
    {
        string archivePath = Path.Combine(_temp.Path, "valid.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "payload.exe", Content = Encoding.ASCII.GetBytes("hello") },
        ]);
        File.WriteAllText(archivePath + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        string destDir = Path.Combine(_temp.Path, "out");
        var sut = new TarSandboxedService(new GroupPolicyOptions { MotwMode = MotwMode.UnsafeExtensionsOnly });
        var result = await sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(destDir, "payload.exe") + ":Zone.Identifier").Should().BeTrue();
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

    // T-F94: a tar.gz whose declared uncompressed content is a huge run of a single repeated
    // byte (compresses to a tiny fraction of its declared size) is skipped by default (no
    // confirm callback wired) rather than extracted — a SkippedFile, not an ArchiveError, since
    // T-F94 changed this from an auto-reject to a declined-by-default confirmation. See
    // DECISIONS.md's T-F94 entry (supersedes T-F90's original auto-reject-only design).
    [Integration]
    public async Task ExtractAsync_ArchiveWithExtremeCompressionRatio_NoCallback_SkipsWholeArchive()
    {
        string archivePath = Path.Combine(_temp.Path, "bomb.tar.gz");
        string bombContent = new string('A', 5_000_000);
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-czf", [("bomb.txt", bombContent)]);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().Contain(s => s.Path == archivePath);
        File.Exists(Path.Combine(destDir, "bomb.txt")).Should().BeFalse();
    }

    // T-F94: the same bomb-shaped archive, but with a confirm callback that returns true and
    // ample real disk space — extraction proceeds normally.
    [Integration]
    public async Task ExtractAsync_ArchiveWithExtremeCompressionRatio_CallbackConfirms_ExtractsNormally()
    {
        string archivePath = Path.Combine(_temp.Path, "bomb.tar.gz");
        string bombContent = new string('A', 5_000_000);
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-czf", [("bomb.txt", bombContent)]);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            ConfirmCompressionBombExtraction = _ => Task.FromResult(true),
        });

        result.Success.Should().BeTrue();
        result.SkippedFiles.Should().BeEmpty();
        result.CreatedFiles.Should().ContainSingle();
        File.ReadAllText(Path.Combine(destDir, "bomb.txt")).Should().Be(bombContent);
    }

    // T-F05: SelectedEntryPaths restricts extraction to just the named entries — used by the
    // archive browser's "Extract selected" command. Whole-archive pre-scan still runs
    // unconditionally (this is exercised implicitly: extraction would fail here if it didn't,
    // since ScanForUnsafeEntriesAsync is what produces the name list ExpandSelection consumes).
    [Integration]
    public async Task ExtractAsync_SelectedEntryPaths_FilesOnly_ExtractsOnlySelectedFiles()
    {
        string archivePath = Path.Combine(_temp.Path, "nested.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "root.txt", Content = Encoding.ASCII.GetBytes("root") },
            new TarBuilder.Entry { Name = "docs/readme.txt", Content = Encoding.ASCII.GetBytes("readme") },
            new TarBuilder.Entry { Name = "docs/manual.txt", Content = Encoding.ASCII.GetBytes("manual") },
            new TarBuilder.Entry { Name = "src/main.cs", Content = Encoding.ASCII.GetBytes("code") },
        ]);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            SelectedEntryPaths = ["root.txt", "docs/readme.txt"],
        });

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(destDir, "root.txt")).Should().BeTrue();
        File.Exists(Path.Combine(destDir, "docs", "readme.txt")).Should().BeTrue();
        File.Exists(Path.Combine(destDir, "docs", "manual.txt")).Should().BeFalse();
        Directory.Exists(Path.Combine(destDir, "src")).Should().BeFalse();
    }

    // T-F05: a selected folder path pulls in every entry nested under it, expanded to explicit
    // descendant member names before the "-xf" call (see DECISIONS.md's T-F05 spike entry).
    [Integration]
    public async Task ExtractAsync_SelectedEntryPaths_FolderPath_ExtractsAllDescendants()
    {
        string archivePath = Path.Combine(_temp.Path, "nested.tar");
        TarBuilder.WriteTar(archivePath,
        [
            new TarBuilder.Entry { Name = "root.txt", Content = Encoding.ASCII.GetBytes("root") },
            new TarBuilder.Entry { Name = "docs/readme.txt", Content = Encoding.ASCII.GetBytes("readme") },
            new TarBuilder.Entry { Name = "docs/sub/appendix.txt", Content = Encoding.ASCII.GetBytes("appendix") },
            new TarBuilder.Entry { Name = "src/main.cs", Content = Encoding.ASCII.GetBytes("code") },
        ]);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            SelectedEntryPaths = ["docs"],
        });

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(destDir, "docs", "readme.txt")).Should().BeTrue();
        File.Exists(Path.Combine(destDir, "docs", "sub", "appendix.txt")).Should().BeTrue();
        File.Exists(Path.Combine(destDir, "root.txt")).Should().BeFalse();
        Directory.Exists(Path.Combine(destDir, "src")).Should().BeFalse();
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
