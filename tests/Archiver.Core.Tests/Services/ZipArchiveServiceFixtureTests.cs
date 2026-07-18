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

    // Strengthens the assertion above: reporting an error isn't proof the traversal was actually
    // prevented on disk. zipslip_traversal.zip's two malicious entries ("../traversal_attempt.txt",
    // "subdir/../../deep_traversal.txt") both resolve, if unprotected, to directly inside
    // DestinationFolder (_temp.Path) — one level above the per-archive SeparateFolders subfolder.
    // Mirrors libarchive's own test_write_disk_secure.c pattern (assertFileNotExists after a
    // blocked absolute/traversal write), found while comparing against its extraction-safety
    // test suite (T-F115 follow-up research).
    [Fact]
    public async Task Extract_ZipSlipTraversal_DoesNotCreateFileOutsideDestination()
    {
        await _sut.ExtractAsync(SeparateFolders(FixtureHelper.Archive("zipslip_traversal.zip")));

        File.Exists(Path.Combine(_temp.Path, "traversal_attempt.txt")).Should().BeFalse(
            "the '../traversal_attempt.txt' entry must never land outside its intended subfolder");
        File.Exists(Path.Combine(_temp.Path, "deep_traversal.txt")).Should().BeFalse(
            "the 'subdir/../../deep_traversal.txt' entry must never land outside its intended subfolder");
        Directory.GetFiles(_temp.Path, "traversal_attempt.txt", SearchOption.AllDirectories).Should().BeEmpty();
        Directory.GetFiles(_temp.Path, "deep_traversal.txt", SearchOption.AllDirectories).Should().BeEmpty();
    }

    // A ZIP entry whose name is rooted but drive-less (leading '\', no colon) skips the ADS-marker
    // check entirely (HasAlternateDataStreamMarker only looks for ':') and isolates whether the
    // Path.Combine(tempDest, relativePath) + StartsWith(fullTempDest) guard (ZipArchiveService.cs)
    // alone still catches it. That guard relies on a specific, easy-to-regress .NET behavior:
    // Path.Combine returns its second argument unchanged (discarding tempDest) whenever that
    // argument is itself rooted. System.IO.Compression sanitizes entry names on write, so — same
    // technique the zipslip_traversal.zip fixture already uses — the archive is built with a
    // same-byte-length placeholder name, then the raw bytes are patched afterward.
    [Fact]
    public async Task Extract_RootedNonDriveEntryName_DoesNotEscapeDestination()
    {
        const string rooted = @"\evil_root_escape.txt";
        // Same-byte-length placeholder — ASCII-only, so char count == UTF-8 byte count for both.
        string placeholder = new string('X', rooted.Length);
        var archivePath = Path.Combine(_temp.Path, "rooted_entry.zip");

        byte[] raw;
        using (var ms = new MemoryStream())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = zip.CreateEntry(placeholder, CompressionLevel.Optimal);
                using var stream = entry.Open();
                stream.Write(System.Text.Encoding.UTF8.GetBytes("should never land here\n"));
            }
            raw = ms.ToArray();
        }

        var oldBytes = System.Text.Encoding.UTF8.GetBytes(placeholder);
        var newBytes = System.Text.Encoding.UTF8.GetBytes(rooted);
        newBytes.Should().HaveCount(oldBytes.Length, "patched name must keep the ZIP's declared name length");
        int idx = IndexOfSequence(raw, oldBytes);
        idx.Should().BeGreaterThanOrEqualTo(0, "placeholder entry name must be found in the raw ZIP bytes");
        newBytes.CopyTo(raw, idx);
        await File.WriteAllBytesAsync(archivePath, raw);

        await _sut.ExtractAsync(SeparateFolders(archivePath));

        File.Exists(Path.Combine(_temp.Path, "evil_root_escape.txt")).Should().BeFalse(
            "a rooted, drive-less entry name must not escape the destination folder");
        Directory.GetFiles(_temp.Path, "evil_root_escape.txt*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    private static int IndexOfSequence(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { found = false; break; }
            if (found) return i;
        }
        return -1;
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

}
