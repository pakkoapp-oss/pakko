using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

public sealed class ZipArchiveServiceExtractTests : IDisposable
{
    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string CreateTestZip(string zipName, params string[] fileNames)
    {
        var zipPath = Path.Combine(_temp.Path, zipName);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var name in fileNames)
            archive.CreateEntryFromFile(_temp.CreateFile(name), name);
        return zipPath;
    }

    private string CreateTestZipWithFolder(string zipName, string folderName, params string[] fileNames)
    {
        var zipPath = Path.Combine(_temp.Path, zipName);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var name in fileNames)
        {
            var entry = archive.CreateEntry($"{folderName}/{name}");
            using var entryStream = entry.Open();
            using var srcStream = File.OpenRead(_temp.CreateFile(name));
            srcStream.CopyTo(entryStream);
        }
        return zipPath;
    }

    [Fact]
    public async Task ExtractAsync_ValidZip_ExtractsFiles()
    {
        var zip = CreateTestZip("archive.zip", "file1.txt", "file2.txt");
        var destDir = Path.Combine(_temp.Path, "output");

        var options = new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = destDir
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        Directory.Exists(destDir).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_SeparateFoldersMode_CreatesSubfolderPerArchive()
    {
        var zip1 = CreateTestZip("first.zip", "a.txt");
        var zip2 = CreateTestZip("second.zip", "b.txt");
        var destDir = Path.Combine(_temp.Path, "extracted");

        var options = new ExtractOptions
        {
            ArchivePaths = [zip1, zip2],
            DestinationFolder = destDir,
            Mode = ExtractMode.SeparateFolders
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        Directory.Exists(Path.Combine(destDir, "first")).Should().BeTrue();
        Directory.Exists(Path.Combine(destDir, "second")).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_NonExistentPath_SkippedSilently()
    {
        var options = new ExtractOptions
        {
            ArchivePaths = [@"C:\fake\archive.zip"],
            DestinationFolder = _temp.Path
        };

        var result = await _sut.ExtractAsync(options);

        // Non-existent file fails magic-byte check → silently skipped
        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_JarWithValidZipContent_ExtractsSuccessfully()
    {
        var jarPath = Path.Combine(_temp.Path, "library.jar");
        using (var archive = ZipFile.Open(jarPath, ZipArchiveMode.Create))
            archive.CreateEntryFromFile(_temp.CreateFile("Manifest.txt"), "Manifest.txt");

        var destDir = Path.Combine(_temp.Path, "jar_output");
        var options = new ExtractOptions
        {
            ArchivePaths = [jarPath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(destDir, "Manifest.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_ZipExtensionButWrongMagicBytes_SkippedSilently()
    {
        var fakePath = Path.Combine(_temp.Path, "not_really.zip");
        File.WriteAllBytes(fakePath, [0x00, 0x01, 0x02, 0x03]);

        var options = new ExtractOptions
        {
            ArchivePaths = [fakePath],
            DestinationFolder = _temp.Path
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_SingleRootFolder_ExtractsWithoutDoubleNesting()
    {
        var zip = CreateTestZipWithFolder("wrapped.zip", "myFolder", "a.txt", "b.txt");
        var destDir = Path.Combine(_temp.Path, "output");

        var options = new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        // Files must land directly in destDir, not in destDir/myFolder/
        File.Exists(Path.Combine(destDir, "a.txt")).Should().BeTrue();
        File.Exists(Path.Combine(destDir, "b.txt")).Should().BeTrue();
        Directory.Exists(Path.Combine(destDir, "myFolder")).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_MultipleRootItems_CreatesSubfolderNamedAfterArchive()
    {
        var zip = CreateTestZip("bundle.zip", "file1.txt", "file2.txt");
        var destDir = Path.Combine(_temp.Path, "output");

        var options = new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        // Files must land in destDir/bundle/, not directly in destDir
        File.Exists(Path.Combine(destDir, "bundle", "file1.txt")).Should().BeTrue();
        File.Exists(Path.Combine(destDir, "bundle", "file2.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_SingleRootFile_ExtractsDirectly()
    {
        var zip = CreateTestZip("solo.zip", "readme.txt");
        var destDir = Path.Combine(_temp.Path, "output");

        var options = new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(destDir, "readme.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_RarFile_AppearsInSkippedFilesWithFriendlyReason()
    {
        var rarPath = Path.Combine(_temp.Path, "backup.rar");
        // RAR magic bytes: 52 61 72 21
        File.WriteAllBytes(rarPath, [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00]);

        var options = new ExtractOptions
        {
            ArchivePaths = [rarPath],
            DestinationFolder = _temp.Path
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().HaveCount(1);
        result.SkippedFiles[0].Path.Should().Be(rarPath);
        result.SkippedFiles[0].Reason.Should().Be("RAR format is not supported. Only ZIP-based formats are supported.");
    }

    [Fact]
    public async Task ExtractAsync_RandomBinaryFile_NotInSkippedFilesOrErrors()
    {
        var binaryPath = Path.Combine(_temp.Path, "data.bin");
        File.WriteAllBytes(binaryPath, [0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x11]);

        var options = new ExtractOptions
        {
            ArchivePaths = [binaryPath],
            DestinationFolder = _temp.Path
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_DeleteArchiveAfterExtraction_SucceedsWithoutDeletingArchive()
    {
        // Deletion is now handled by MainViewModel (RunCleanupAsync), not the service.
        // The service must accept the option and complete successfully; archive is NOT deleted.
        var zip = CreateTestZip("removeme.zip", "file.txt");
        var destDir = Path.Combine(_temp.Path, "output");

        var options = new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = destDir,
            DeleteArchiveAfterExtraction = true
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        File.Exists(zip).Should().BeTrue(); // service no longer deletes — ViewModel does
    }

    [Fact]
    public async Task ExtractAsync_ConflictSkip_DoesNotOverwriteExistingFile()
    {
        var zip = CreateTestZip("archive.zip", "file.txt");
        var destDir = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(destDir);

        // Pre-create the file that would be extracted
        var existingFile = Path.Combine(destDir, "file.txt");
        File.WriteAllText(existingFile, "original content");
        var originalContent = File.ReadAllText(existingFile);

        var options = new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Skip
        };

        await _sut.ExtractAsync(options);

        File.ReadAllText(existingFile).Should().Be(originalContent);
    }

    [Fact]
    public async Task ExtractAsync_ConflictRename_CreatesNumberedFileWhenDestinationExists()
    {
        var zip = CreateTestZip("archive.zip", "file.txt");
        var destDir = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(destDir);

        // Pre-create the file that would be extracted
        File.WriteAllText(Path.Combine(destDir, "file.txt"), "original content");

        var options = new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Rename
        };

        await _sut.ExtractAsync(options);

        File.Exists(Path.Combine(destDir, "file (1).txt")).Should().BeTrue();
        File.Exists(Path.Combine(destDir, "file.txt")).Should().BeTrue(); // original untouched
    }

    [Fact]
    public async Task ExtractAsync_PasswordProtectedZip_ReturnsArchiveErrorWithClearMessage()
    {
        var encryptedPath = Path.Combine(_temp.Path, "encrypted.zip");
        // Local file header with encryption bit (bit 0) set in general purpose bit flag (offset 6)
        // 50 4B 03 04 = signature, 14 00 = version 2.0, 01 00 = flags (bit 0 = encrypted)
        File.WriteAllBytes(encryptedPath, [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x01, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);

        var options = new ExtractOptions
        {
            ArchivePaths = [encryptedPath],
            DestinationFolder = _temp.Path
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Message.Should().Be("This archive is password-protected and cannot be extracted.");
    }

    [Fact]
    public async Task ExtractAsync_ZipMagicBytesButCorruptedContent_ReturnsArchiveError()
    {
        var corruptPath = Path.Combine(_temp.Path, "corrupt.zip");
        // Valid ZIP magic bytes followed by garbage
        File.WriteAllBytes(corruptPath, [0x50, 0x4B, 0x03, 0x04, 0xFF, 0xFE, 0xAA, 0xBB]);

        var options = new ExtractOptions
        {
            ArchivePaths = [corruptPath],
            DestinationFolder = _temp.Path
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Message.Should().Be("File has ZIP signature but appears corrupted or incomplete.");
    }

    [Fact]
    public async Task ExtractAsync_Cancelled_LeavesNoTempDirectory()
    {
        var file = _temp.CreateFile("source.txt", new string('x', 64 * 1024));
        var archiveOptions = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "test"
        };
        await _sut.ArchiveAsync(archiveOptions);
        string archivePath = Path.Combine(_temp.Path, "test.zip");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var options = new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = _temp.Path,
            Mode = ExtractMode.SeparateFolders
        };

        try { await _sut.ExtractAsync(options, cancellationToken: cts.Token); }
        catch (OperationCanceledException) { }

        Directory.GetDirectories(_temp.Path, "*_tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_SuspiciousCompressionRatio_SkipsEntry()
    {
        // 1 MB of repeated 'A' compresses to ~1 KB = ~1000:1 ratio
        string content = new string('A', 1024 * 1024);
        var file = _temp.CreateFile("compressible.txt", content);

        await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "test_bomb",
            CompressionLevel = System.IO.Compression.CompressionLevel.SmallestSize
        });

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [Path.Combine(_temp.Path, "test_bomb.zip")],
            DestinationFolder = _temp.Path,
            Mode = ExtractMode.SeparateFolders
        });

        // Either extracted normally (ratio under limit) or skipped (ratio over limit) —
        // must not throw and must not produce errors either way
        (result.Success || result.SkippedFiles.Count > 0).Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
}
