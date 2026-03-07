using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// Fixture-based integration tests for ZipArchiveService.
/// Requires generated fixtures — run: dotnet run --project tests/Archiver.Core.Tests.GenerateFixtures
/// </summary>
public sealed class ZipArchiveServiceFixtureTests : IDisposable
{
    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // ── Helpers ────────────────────────────────────────────────────────────

    private ExtractOptions SingleFolder(string archivePath) => new()
    {
        ArchivePaths = [archivePath],
        DestinationFolder = _temp.Path,
        Mode = ExtractMode.SingleFolder
    };

    private ExtractOptions SeparateFolders(string archivePath) => new()
    {
        ArchivePaths = [archivePath],
        DestinationFolder = _temp.Path,
        Mode = ExtractMode.SeparateFolders
    };

    // ── Valid archive extraction ───────────────────────────────────────────

    [Fact]
    public async Task Extract_ValidSingleFile_Succeeds()
    {
        var result = await _sut.ExtractAsync(SeparateFolders(FixtureHelper.Archive("valid_single_file.zip")));

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        Directory.GetFiles(_temp.Path, "*", SearchOption.AllDirectories).Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Extract_ValidMultipleFiles_Succeeds()
    {
        var result = await _sut.ExtractAsync(SeparateFolders(FixtureHelper.Archive("valid_multiple_files.zip")));

        result.Success.Should().BeTrue();
        Directory.GetFiles(_temp.Path, "*", SearchOption.AllDirectories).Should().HaveCountGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task Extract_ValidNestedFolders_Succeeds()
    {
        var result = await _sut.ExtractAsync(SeparateFolders(FixtureHelper.Archive("valid_nested_folders.zip")));

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Extract_ValidUnicodeFilenames_Succeeds()
    {
        var result = await _sut.ExtractAsync(SeparateFolders(FixtureHelper.Archive("valid_unicode_filenames.zip")));

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Extract_IncompressibleContent_Succeeds()
    {
        var result = await _sut.ExtractAsync(SeparateFolders(FixtureHelper.Archive("valid_incompressible_content.zip")));

        result.Success.Should().BeTrue();
    }

    // ── Smart extract foldering (T-14) ────────────────────────────────────

    [Fact]
    public async Task Extract_SingleRootFolder_NoDoubleNesting()
    {
        // ZIP contains: project/readme.txt, project/src/main.cs, project/src/utils.cs
        // Smart foldering strips the "project/" prefix → files land directly in destDir
        var result = await _sut.ExtractAsync(SingleFolder(FixtureHelper.Archive("extract_single_root_folder.zip")));

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(_temp.Path, "readme.txt")).Should().BeTrue("file should land directly in destDir");
        Directory.Exists(Path.Combine(_temp.Path, "project")).Should().BeFalse("root prefix should be stripped");
    }

    [Fact]
    public async Task Extract_MultipleRootItems_SubfolderCreated()
    {
        // ZIP contains: readme.txt, main.cs, assets/icon.txt — multiple roots
        // Smart foldering creates a subfolder named after the archive
        var result = await _sut.ExtractAsync(SingleFolder(FixtureHelper.Archive("extract_multiple_root_items.zip")));

        result.Success.Should().BeTrue();
        Directory.GetDirectories(_temp.Path).Should().HaveCountGreaterThanOrEqualTo(1,
            "a subfolder should be created for multi-root archives");
    }

    [Fact]
    public async Task Extract_SingleRootFile_ExtractedDirectly()
    {
        // ZIP contains: report.txt — single file at root
        // Smart foldering extracts it directly to destDir
        var result = await _sut.ExtractAsync(SingleFolder(FixtureHelper.Archive("extract_single_root_file.zip")));

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(_temp.Path, "report.txt")).Should().BeTrue("single root file lands directly in destDir");
    }

    // ── Corrupted archives ────────────────────────────────────────────────

    [Fact]
    public async Task Extract_CorruptedEntryData_ReturnsError()
    {
        var result = await _sut.ExtractAsync(SeparateFolders(FixtureHelper.Archive("corrupted_entry_data.zip")));

        (result.Success == false || result.Errors.Count > 0).Should().BeTrue(
            "corrupted entry data should produce an error");
    }

    [Fact]
    public async Task Extract_CorruptedCentralDirectory_ReturnsError()
    {
        var result = await _sut.ExtractAsync(SeparateFolders(FixtureHelper.Archive("corrupted_central_directory.zip")));

        (result.Success == false || result.Errors.Count > 0).Should().BeTrue(
            "unreadable ZIP should produce an error");
    }

    // ── Encrypted archive (T-25) ──────────────────────────────────────────

    [Fact]
    public async Task Extract_EncryptedZipCrypto_ReturnsError()
    {
        var result = await _sut.ExtractAsync(SeparateFolders(FixtureHelper.Archive("encrypted_zipcrypto.zip")));

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("password-protected"));
    }

    // ── ZIP slip protection (T-14) ────────────────────────────────────────

    [Fact]
    public async Task Extract_ZipSlipTraversal_Blocked()
    {
        var result = await _sut.ExtractAsync(SeparateFolders(FixtureHelper.Archive("zipslip_traversal.zip")));

        (result.Success == false || result.Errors.Count > 0).Should().BeTrue(
            "path traversal entries should be blocked");
    }

    // ── Manual fixtures (skipped if absent) ──────────────────────────────

    [Fact]
    public async Task Extract_CreatedBy7Zip_Succeeds()
    {
        var archivePath = FixtureHelper.ArchiveOptional("created_by_7zip.zip");
        if (archivePath is null) return; // manual fixture absent — skip gracefully

        var result = await _sut.ExtractAsync(SeparateFolders(archivePath));

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Extract_CreatedByWinRAR_Succeeds()
    {
        var archivePath = FixtureHelper.ArchiveOptional("created_by_winrar.zip");
        if (archivePath is null) return;

        var result = await _sut.ExtractAsync(SeparateFolders(archivePath));

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Extract_CreatedByMacOS_Succeeds()
    {
        var archivePath = FixtureHelper.ArchiveOptional("created_by_macos.zip");
        if (archivePath is null) return;

        var result = await _sut.ExtractAsync(SeparateFolders(archivePath));

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    // ── T-34 integrity manifest ───────────────────────────────────────────

    [Fact]
    public async Task Archive_WritesIntegrityManifest()
    {
        var sourceFile = FixtureHelper.PlainFile("compressible.txt");
        var archiveOptions = new ArchiveOptions
        {
            SourcePaths = [sourceFile],
            DestinationFolder = _temp.Path,
            ArchiveName = "test_manifest"
        };

        var archiveResult = await _sut.ArchiveAsync(archiveOptions);

        archiveResult.Success.Should().BeTrue();
        var zipPath = archiveResult.CreatedFiles[0];

        using var archive = ZipFile.OpenRead(zipPath);
        var comment = archive.Comment;

        comment.Should().StartWith("PAKKO-INTEGRITY-V1", "manifest header must be first line");
        comment.Should().Contain("compressible.txt=", "manifest must include a hash for the archived file");
    }

    [Fact]
    public async Task Extract_ValidIntegrityManifest_NoWarnings()
    {
        var sourceFile = FixtureHelper.PlainFile("compressible.txt");
        var archiveOptions = new ArchiveOptions
        {
            SourcePaths = [sourceFile],
            DestinationFolder = _temp.Path,
            ArchiveName = "test_nw"
        };
        var archiveResult = await _sut.ArchiveAsync(archiveOptions);
        archiveResult.Success.Should().BeTrue();

        using var destDir = new TempDirectory();
        var extractOptions = new ExtractOptions
        {
            ArchivePaths = [archiveResult.CreatedFiles[0]],
            DestinationFolder = destDir.Path,
            Mode = ExtractMode.SeparateFolders
        };

        var extractResult = await _sut.ExtractAsync(extractOptions);

        extractResult.Success.Should().BeTrue();
        extractResult.Warnings.Should().BeEmpty("unmodified Pakko archive should produce no integrity warnings");
    }

    [Fact]
    public async Task Extract_TamperedIntegrityManifest_ReturnsWarning()
    {
        var sourceFile = FixtureHelper.PlainFile("compressible.txt");
        var archiveOptions = new ArchiveOptions
        {
            SourcePaths = [sourceFile],
            DestinationFolder = _temp.Path,
            ArchiveName = "test_tampered"
        };
        var archiveResult = await _sut.ArchiveAsync(archiveOptions);
        archiveResult.Success.Should().BeTrue();
        var zipPath = archiveResult.CreatedFiles[0];

        // Overwrite ZIP comment with a manifest containing a wrong hash
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Update))
        {
            archive.Comment = "PAKKO-INTEGRITY-V1\ncompressible.txt=0000000000000000000000000000000000000000000000000000000000000000";
        }

        using var destDir = new TempDirectory();
        var extractOptions = new ExtractOptions
        {
            ArchivePaths = [zipPath],
            DestinationFolder = destDir.Path,
            Mode = ExtractMode.SeparateFolders
        };

        var extractResult = await _sut.ExtractAsync(extractOptions);

        extractResult.Warnings.Should().NotBeEmpty("tampered manifest should produce an integrity warning");
    }
}
