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

    // T-F103: a compound extension (".tar.gz") must be stripped as a unit — Path
    // .GetFileNameWithoutExtension alone would leave "browse_test.tar" instead of "browse_test".
    // The content here is a plain zip; only the file name string is exercising the naming bug.
    [Fact]
    public async Task ExtractAsync_SeparateFoldersMode_StripsCompoundTarExtension()
    {
        var zip = CreateTestZip("browse_test.tar.gz", "a.txt");
        var destDir = Path.Combine(_temp.Path, "extracted");

        var options = new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = destDir,
            Mode = ExtractMode.SeparateFolders
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        Directory.Exists(Path.Combine(destDir, "browse_test")).Should().BeTrue();
        Directory.Exists(Path.Combine(destDir, "browse_test.tar")).Should().BeFalse();
    }

    // T-F67: SeparateFolderName lets a caller (Archiver.Shell) put the extracted contents
    // under a caller-chosen subfolder name instead of the archive's own file name — used to
    // pick a numbered name when the archive's own name is already taken on disk.
    [Fact]
    public async Task ExtractAsync_SeparateFolderName_OverridesArchiveDerivedName()
    {
        var zip = CreateTestZip("first.zip", "a.txt");
        var destDir = Path.Combine(_temp.Path, "extracted");

        var options = new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = destDir,
            Mode = ExtractMode.SeparateFolders,
            SeparateFolderName = "first (1)"
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        Directory.Exists(Path.Combine(destDir, "first (1)")).Should().BeTrue();
        Directory.Exists(Path.Combine(destDir, "first")).Should().BeFalse();
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

    // T-F87: an archive whose only entry conflict-skips must not appear in CreatedFiles, and
    // must record a whole-archive SkippedFile (Path == archivePath) — this is the signal
    // MainViewModel uses to avoid deleting a source that was never actually extracted when
    // DeleteAfterOperation is on.
    [Fact]
    public async Task ExtractAsync_AllEntriesConflictSkipped_ExcludesArchiveFromCreatedFilesAndRecordsWholeArchiveSkip()
    {
        var zip = CreateTestZip("archive.zip", "file.txt");
        var destDir = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(destDir);
        File.WriteAllText(Path.Combine(destDir, "file.txt"), "original content");

        var options = new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Skip
        };

        var result = await _sut.ExtractAsync(options);

        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().Contain(s => s.Path == zip);
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

    // T-F06: per-entry Ask conflict resolution — the callback decides Overwrite/Rename/Skip
    // instead of a pre-selected ConflictBehavior.
    [Theory]
    [InlineData(ConflictResolution.Overwrite)]
    [InlineData(ConflictResolution.Rename)]
    [InlineData(ConflictResolution.Skip)]
    public async Task ExtractAsync_ConflictAsk_PerEntry_AppliesCallbackResolution(ConflictResolution resolution)
    {
        var zip = CreateTestZip("archive.zip", "file.txt");
        var destDir = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(destDir);
        File.WriteAllText(Path.Combine(destDir, "file.txt"), "original content");

        var options = new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Ask,
            ResolveConflictAsync = _ => Task.FromResult(new ConflictDecision { Resolution = resolution })
        };

        await _sut.ExtractAsync(options);

        switch (resolution)
        {
            case ConflictResolution.Overwrite:
                File.Exists(Path.Combine(destDir, "file.txt")).Should().BeTrue();
                File.ReadAllText(Path.Combine(destDir, "file.txt")).Should().NotBe("original content");
                break;
            case ConflictResolution.Rename:
                File.Exists(Path.Combine(destDir, "file (1).txt")).Should().BeTrue();
                File.ReadAllText(Path.Combine(destDir, "file.txt")).Should().Be("original content");
                break;
            case ConflictResolution.Skip:
                File.ReadAllText(Path.Combine(destDir, "file.txt")).Should().Be("original content");
                File.Exists(Path.Combine(destDir, "file (1).txt")).Should().BeFalse();
                break;
        }
    }

    [Fact]
    public async Task ExtractAsync_ConflictAsk_NoConflicts_CallbackNeverInvoked()
    {
        var zip = CreateTestZip("archive.zip", "file.txt");
        var destDir = Path.Combine(_temp.Path, "out");

        int callCount = 0;
        var options = new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Ask,
            ResolveConflictAsync = _ =>
            {
                callCount++;
                return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Skip });
            }
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        callCount.Should().Be(0);
    }

    // T-F06: the ConflictResolver instance is constructed once per ExtractAsync call, before the
    // loop over ArchivePaths — this is what makes ApplyToAll survive from one archive to the
    // next, not just within a single archive's own entries.
    [Fact]
    public async Task ExtractAsync_ConflictAsk_ApplyToAll_AcrossMultipleArchives_InvokesCallbackOnce()
    {
        var zip1 = CreateTestZip("first.zip", "file.txt");
        var zip2 = CreateTestZip("second.zip", "file.txt");
        var destDir = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(destDir);
        File.WriteAllText(Path.Combine(destDir, "file.txt"), "original content");

        int callCount = 0;
        var options = new ExtractOptions
        {
            ArchivePaths = [zip1, zip2],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Ask,
            ResolveConflictAsync = _ =>
            {
                callCount++;
                return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Rename, ApplyToAll = true });
            }
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(destDir, "file (1).txt")).Should().BeTrue();
        File.Exists(Path.Combine(destDir, "file (2).txt")).Should().BeTrue();
        callCount.Should().Be(1);
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
    public async Task ExtractAsync_EntryWithColonInName_IsSkipped()
    {
        using var tempDir = new TempDirectory();
        string archivePath = Path.Combine(tempDir.Path, "ads_test.zip");

        // Create a ZIP with an entry whose name contains ':'
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("file.txt:payload.exe");
            using var stream = entry.Open();
            await stream.WriteAsync("malicious"u8.ToArray());
        }

        var svc = new ZipArchiveService();
        var options = new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = tempDir.Path,
            Mode = ExtractMode.SeparateFolders,
            OnConflict = ConflictBehavior.Overwrite
        };

        var result = await svc.ExtractAsync(options);

        // T-F87: since every entry in this archive was skipped, a second whole-archive
        // SkippedFile (Path == archivePath) is also recorded so DeleteAfterOperation cleanup
        // knows this source was never actually extracted.
        result.SkippedFiles.Should().HaveCount(2);
        result.SkippedFiles[0].Path.Should().Contain(":");
        result.SkippedFiles[0].Reason.Should().Contain("Alternate Data Stream");
        result.SkippedFiles[1].Path.Should().Be(archivePath);
    }

    [Theory]
    [InlineData("NUL")]
    [InlineData("CON")]
    [InlineData("PRN")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    [InlineData("nul.txt")]
    [InlineData("con.log")]
    public async Task ExtractAsync_ReservedWindowsName_IsSkipped(string reservedName)
    {
        using var tempDir = new TempDirectory();
        string archivePath = Path.Combine(tempDir.Path, "reserved_test.zip");

        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(reservedName);
            using var stream = entry.Open();
            await stream.WriteAsync("data"u8.ToArray());
        }

        var svc = new ZipArchiveService();
        var options = new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = tempDir.Path,
            Mode = ExtractMode.SeparateFolders,
            OnConflict = ConflictBehavior.Overwrite
        };

        var result = await svc.ExtractAsync(options);

        // T-F87: whole-archive skip also recorded — see the ADS test's comment above.
        result.SkippedFiles.Should().HaveCount(2);
        result.SkippedFiles[0].Reason.Should().Contain("reserved Windows device name");
        result.SkippedFiles[1].Path.Should().Be(archivePath);
    }

    [Fact]
    public async Task ExtractAsync_EntryWithControlCharacters_IsSkipped()
    {
        using var tempDir = new TempDirectory();
        string archivePath = Path.Combine(tempDir.Path, "ctrl_char_test.zip");

        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            // Entry name with a null byte (0x00)
            var entry = archive.CreateEntry("file\x01name.txt");
            using var stream = entry.Open();
            await stream.WriteAsync("data"u8.ToArray());
        }

        var svc = new ZipArchiveService();
        var options = new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = tempDir.Path,
            Mode = ExtractMode.SeparateFolders,
            OnConflict = ConflictBehavior.Overwrite
        };

        var result = await svc.ExtractAsync(options);

        // T-F87: whole-archive skip also recorded — see the ADS test's comment above.
        result.SkippedFiles.Should().HaveCount(2);
        result.SkippedFiles[0].Reason.Should().Contain("control characters");
        result.SkippedFiles[1].Path.Should().Be(archivePath);
    }

    // T-F59: extraction total computed from entry.Length (uncompressed), not CompressedLength
    [Fact]
    public async Task ExtractAsync_CompressedArchive_ProgressNeverExceedsHundredPercent()
    {
        // 10 KB of identical bytes compresses dramatically (~300:1 ratio),
        // well under the 1000:1 ZIP bomb threshold so extraction proceeds normally.
        // If the progress total were computed from CompressedLength, bytesRead would
        // vastly exceed it and Percent would shoot past 100 — regression for T-F59.
        string content = new string('A', 10 * 1024);
        var file = _temp.CreateFile("compressible.txt", content);
        var zipPath = Path.Combine(_temp.Path, "overshoot_test.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            archive.CreateEntryFromFile(file, "compressible.txt", CompressionLevel.SmallestSize);

        // Confirm the scenario is meaningful: compressed must be < 1/10 of uncompressed
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var entry = zip.GetEntry("compressible.txt")!;
            entry.CompressedLength.Should().BeLessThan(entry.Length / 10,
                "the entry must be at least 10x compressed for this test to distinguish Length from CompressedLength");
        }

        var reports = new List<ProgressReport>();
        var progress = new Progress<ProgressReport>(r => reports.Add(r));

        var destDir = Path.Combine(_temp.Path, "overshoot_output");
        var options = new ExtractOptions
        {
            ArchivePaths = [zipPath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder
        };

        await _sut.ExtractAsync(options, progress);
        await Task.Delay(50); // let Progress<ProgressReport> callbacks fire

        reports.Should().NotBeEmpty();
        reports.Last().Percent.Should().Be(100);
        reports.Should().OnlyContain(r => r.Percent >= 0 && r.Percent <= 100,
            "progress percent must stay within [0, 100] — regression for T-F59");
    }

    [Fact]
    public async Task ExtractAsync_SingleArchive_ReportsMonotonicByteProgress()
    {
        // Use NoCompression so CompressedLength == uncompressed length for predictable progress
        string content = new string('x', 8 * 1024); // 8 KB
        var file = _temp.CreateFile("data.txt", content);
        var zipPath = Path.Combine(_temp.Path, "progress_test.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            archive.CreateEntryFromFile(file, "data.txt", System.IO.Compression.CompressionLevel.NoCompression);

        var reports = new List<ProgressReport>();
        var progress = new Progress<ProgressReport>(r => reports.Add(r));

        var destDir = Path.Combine(_temp.Path, "extract_output");
        var options = new ExtractOptions
        {
            ArchivePaths = [zipPath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder
        };

        await _sut.ExtractAsync(options, progress);
        await Task.Delay(50); // let Progress<ProgressReport> callbacks fire

        reports.Should().NotBeEmpty();
        reports.Last().Percent.Should().Be(100);
        // Sequence must be non-decreasing
        reports.Select(r => r.Percent).Should().BeInAscendingOrder();
    }

    // T-F94: whole-archive ratio check, no confirm callback wired — default (declined) behavior
    // is a whole-archive SkippedFile, not a per-entry skip (that model was replaced; see
    // DECISIONS.md's T-F94 entry). 50 MB of a single repeated byte comfortably clears the
    // 1000:1 threshold under deflate.
    [Fact]
    public async Task ExtractAsync_SuspiciousCompressionRatio_NoCallback_SkipsWholeArchive()
    {
        string content = new string('A', 50 * 1024 * 1024);
        var file = _temp.CreateFile("compressible.txt", content);

        await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "test_bomb",
            CompressionLevel = System.IO.Compression.CompressionLevel.SmallestSize
        });
        string archivePath = Path.Combine(_temp.Path, "test_bomb.zip");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = _temp.Path,
            Mode = ExtractMode.SeparateFolders
        });

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().Contain(s => s.Path == archivePath);
    }

    // T-F94: same bomb-shaped archive, but with a confirm callback returning true and ample
    // real disk space — extraction proceeds normally.
    [Fact]
    public async Task ExtractAsync_SuspiciousCompressionRatio_CallbackConfirms_ExtractsNormally()
    {
        string content = new string('A', 50 * 1024 * 1024);
        var file = _temp.CreateFile("compressible.txt", content);

        await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "test_bomb",
            CompressionLevel = System.IO.Compression.CompressionLevel.SmallestSize
        });
        string archivePath = Path.Combine(_temp.Path, "test_bomb.zip");
        string destDir = Path.Combine(_temp.Path, "extracted");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SeparateFolders,
            ConfirmCompressionBombExtraction = _ => Task.FromResult(true),
        });

        result.Success.Should().BeTrue();
        result.SkippedFiles.Should().BeEmpty();
        result.CreatedFiles.Should().ContainSingle();
        File.ReadAllText(Path.Combine(destDir, "test_bomb", "compressible.txt")).Should().Be(content);
    }

    [Fact]
    public async Task ExtractAsync_ZipWithMotw_PropagatesZoneIdentifierToExtractedFiles()
    {
        // Arrange: create a ZIP with two files
        var file1 = _temp.CreateFile("doc.txt", "hello");
        var file2 = _temp.CreateFile("data.txt", "world");
        var zipPath = Path.Combine(_temp.Path, "motw_test.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(file1, "doc.txt");
            archive.CreateEntryFromFile(file2, "data.txt");
        }

        // Write Zone.Identifier ADS to the archive — skip test if NTFS ADS not supported
        const string zoneContent = "[ZoneTransfer]\r\nZoneId=3\r\n";
        byte[] zoneBytes = System.Text.Encoding.ASCII.GetBytes(zoneContent);
        try
        {
            using var adsStream = new FileStream(
                zipPath + ":Zone.Identifier",
                FileMode.Create, FileAccess.Write, FileShare.None);
            adsStream.Write(zoneBytes);
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException)
        {
            // ADS not supported on this volume (non-NTFS, network, etc.) — skip gracefully
            return;
        }

        var destDir = Path.Combine(_temp.Path, "motw_output");
        var options = new ExtractOptions
        {
            ArchivePaths = [zipPath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder
        };

        // Act
        var result = await _sut.ExtractAsync(options);

        // Assert
        result.Errors.Should().BeEmpty();
        var extractedFiles = Directory.GetFiles(destDir, "*", SearchOption.AllDirectories);
        extractedFiles.Should().HaveCount(2);

        foreach (string extractedFile in extractedFiles)
        {
            byte[] actual;
            try
            {
                actual = File.ReadAllBytes(extractedFile + ":Zone.Identifier");
            }
            catch
            {
                // ADS read failed — MOTW not propagated
                true.Should().BeFalse($"Zone.Identifier ADS missing on {Path.GetFileName(extractedFile)}");
                return;
            }
            actual.Should().Equal(zoneBytes, $"Zone.Identifier content should match on {Path.GetFileName(extractedFile)}");
        }
    }

    // T-F30: Duplicate Filename Detection Inside Archive (extract side)
    //
    // ZIP format allows two entries with the identical name; System.IO.Compression does not
    // reject them on read. Without dedup, the second entry silently overwrote the first inside
    // the temp extraction directory, since the pre-existing conflict check only looked at the
    // real destination's state, which nothing had been committed to yet.

    private static string CreateZipWithDuplicateEntryNames(string zipPath, string entryName, string contentA, string contentB)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        using (var s = archive.CreateEntry(entryName).Open())
        using (var w = new StreamWriter(s))
            w.Write(contentA);
        using (var s = archive.CreateEntry(entryName).Open())
        using (var w = new StreamWriter(s))
            w.Write(contentB);
        return zipPath;
    }

    [Fact]
    public async Task ExtractAsync_DuplicateEntryNames_RenameKeepsBothFilesDistinct()
    {
        string zipPath = CreateZipWithDuplicateEntryNames(
            Path.Combine(_temp.Path, "dup.zip"), "dup.txt", "first", "second");

        var options = new ExtractOptions
        {
            ArchivePaths = [zipPath],
            DestinationFolder = _temp.Path,
            Mode = ExtractMode.SeparateFolders,
            OnConflict = ConflictBehavior.Rename,
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        var extractedFiles = Directory.GetFiles(_temp.Path, "*.txt", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .ToList();
        extractedFiles.Should().HaveCount(2);

        var contents = extractedFiles.Select(File.ReadAllText).OrderBy(c => c).ToList();
        contents.Should().Equal("first", "second");
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    // T-F05: SelectedEntryPaths restricts extraction to just the named entries — used by the
    // archive browser's "Extract selected" command.
    [Fact]
    public async Task ExtractAsync_SelectedEntryPaths_FilesOnly_ExtractsOnlySelectedFiles()
    {
        var zipPath = Path.Combine(_temp.Path, "nested.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "root.txt", "root");
            WriteEntry(archive, "docs/readme.txt", "readme");
            WriteEntry(archive, "docs/manual.txt", "manual");
            WriteEntry(archive, "src/main.cs", "code");
        }
        var destDir = Path.Combine(_temp.Path, "output");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zipPath],
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

    // T-F05: a selected folder path pulls in every entry nested under it, matching 7-Zip/NanaZip's
    // own "extract selected folder" behavior.
    [Fact]
    public async Task ExtractAsync_SelectedEntryPaths_FolderPath_ExtractsAllDescendants()
    {
        var zipPath = Path.Combine(_temp.Path, "nested.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "root.txt", "root");
            WriteEntry(archive, "docs/readme.txt", "readme");
            WriteEntry(archive, "docs/sub/appendix.txt", "appendix");
            WriteEntry(archive, "src/main.cs", "code");
        }
        var destDir = Path.Combine(_temp.Path, "output");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zipPath],
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

    [Fact]
    public async Task ExtractAsync_DuplicateEntryNames_SkipKeepsOnlyFirst()
    {
        string zipPath = CreateZipWithDuplicateEntryNames(
            Path.Combine(_temp.Path, "dup.zip"), "dup.txt", "first", "second");

        var options = new ExtractOptions
        {
            ArchivePaths = [zipPath],
            DestinationFolder = _temp.Path,
            Mode = ExtractMode.SeparateFolders,
            OnConflict = ConflictBehavior.Skip,
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        var extractedFiles = Directory.GetFiles(_temp.Path, "*.txt", SearchOption.AllDirectories);
        extractedFiles.Should().ContainSingle();
        File.ReadAllText(extractedFiles[0]).Should().Be("first");
    }
}
