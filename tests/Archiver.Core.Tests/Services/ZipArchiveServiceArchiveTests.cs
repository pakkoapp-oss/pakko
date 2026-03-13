using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

public sealed class ZipArchiveServiceArchiveTests : IDisposable
{
    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ArchiveAsync_SingleFile_CreatesZip()
    {
        var file = _temp.CreateFile("document.txt");
        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "output"
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(1);
        File.Exists(result.CreatedFiles[0]).Should().BeTrue();
        result.CreatedFiles[0].Should().EndWith(".zip");
    }

    [Fact]
    public async Task ArchiveAsync_MultipleFiles_SingleArchiveMode_CreatesOneZip()
    {
        var file1 = _temp.CreateFile("a.txt");
        var file2 = _temp.CreateFile("b.txt");
        var options = new ArchiveOptions
        {
            SourcePaths = [file1, file2],
            DestinationFolder = _temp.Path,
            ArchiveName = "combined",
            Mode = ArchiveMode.SingleArchive
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(1);
    }

    [Fact]
    public async Task ArchiveAsync_MultipleFiles_SeparateArchivesMode_CreatesMultipleZips()
    {
        var file1 = _temp.CreateFile("a.txt");
        var file2 = _temp.CreateFile("b.txt");
        var options = new ArchiveOptions
        {
            SourcePaths = [file1, file2],
            DestinationFolder = _temp.Path,
            Mode = ArchiveMode.SeparateArchives
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(2);
    }

    [Fact]
    public async Task ArchiveAsync_NonExistentFile_ReturnsErrorNotThrows()
    {
        var options = new ArchiveOptions
        {
            SourcePaths = [@"C:\does\not\exist.txt"],
            DestinationFolder = _temp.Path
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].SourcePath.Should().Be(@"C:\does\not\exist.txt");
        result.Errors[0].Message.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_CancellationRequested_StopsProcessing()
    {
        var files = Enumerable.Range(1, 10)
            .Select(i => _temp.CreateFile($"file{i}.txt"))
            .ToList();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var options = new ArchiveOptions
        {
            SourcePaths = files,
            DestinationFolder = _temp.Path,
            Mode = ArchiveMode.SeparateArchives
        };

        var result = await _sut.ArchiveAsync(options, cancellationToken: cts.Token);

        // Should process 0 or fewer than 10 items
        result.CreatedFiles.Count.Should().BeLessThan(10);
    }

    [Fact]
    public async Task ArchiveAsync_DeleteSourceFiles_SucceedsWithoutDeletingSource()
    {
        // Deletion is now handled by MainViewModel (RunCleanupAsync), not the service.
        // The service must accept the option and complete successfully; source is NOT deleted.
        var file = _temp.CreateFile("to-delete.txt");
        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "output",
            DeleteSourceFiles = true
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(1);
        File.Exists(file).Should().BeTrue(); // service no longer deletes — ViewModel does
    }

    [Fact]
    public async Task ArchiveAsync_ConflictSkip_DoesNotOverwriteExistingZip()
    {
        var file = _temp.CreateFile("source.txt");
        var existingZip = _temp.CreateFile("output.zip");
        var originalWriteTime = File.GetLastWriteTimeUtc(existingZip);

        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "output",
            OnConflict = ConflictBehavior.Skip
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().BeEmpty();
        File.GetLastWriteTimeUtc(existingZip).Should().Be(originalWriteTime);
    }

    [Fact]
    public async Task ArchiveAsync_ConflictRename_CreatesNumberedZipWhenOutputExists()
    {
        var file = _temp.CreateFile("source.txt");
        _temp.CreateFile("output.zip");

        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "output",
            OnConflict = ConflictBehavior.Rename
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(1);
        result.CreatedFiles[0].Should().EndWith("output (1).zip");
        File.Exists(result.CreatedFiles[0]).Should().BeTrue();
    }

    [Fact]
    public async Task ArchiveAsync_ConflictOverwrite_ReplacesExistingZip()
    {
        var file = _temp.CreateFile("source.txt");
        var existingZip = _temp.CreateFile("output.zip");

        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "output",
            OnConflict = ConflictBehavior.Overwrite
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(1);
        result.CreatedFiles[0].Should().Be(existingZip);
    }

    [Fact]
    public async Task ArchiveAsync_ReportsProgress()
    {
        var files = Enumerable.Range(1, 5)
            .Select(i => _temp.CreateFile($"file{i}.txt"))
            .ToList();

        var reports = new List<ProgressReport>();
        var progress = new Progress<ProgressReport>(r => reports.Add(r));

        var options = new ArchiveOptions
        {
            SourcePaths = files,
            DestinationFolder = _temp.Path,
            Mode = ArchiveMode.SeparateArchives
        };

        await _sut.ArchiveAsync(options, progress);
        await Task.Delay(50); // let Progress<ProgressReport> callbacks fire

        reports.Should().NotBeEmpty();
        reports.Last().Percent.Should().Be(100);
    }

    [Fact]
    public async Task ArchiveAsync_CancelMidArchive_NoUnhandledException()
    {
        // Create 3 files with ~64 KB each so the operation takes measurable time
        string largeContent = new string('x', 64 * 1024);
        var files = Enumerable.Range(1, 3)
            .Select(i => _temp.CreateFile($"large{i}.txt", largeContent))
            .ToList();

        using var destDir = new TempDirectory();
        using var cts = new CancellationTokenSource();

        var options = new ArchiveOptions
        {
            SourcePaths = files,
            DestinationFolder = destDir.Path,
            ArchiveName = "cancel_test",
            Mode = ArchiveMode.SingleArchive
        };

        // Cancel after a short delay — may fire before, during, or after the operation
        _ = Task.Delay(5).ContinueWith(_ => cts.Cancel());

        ArchiveResult? result = null;
        try
        {
            result = await _sut.ArchiveAsync(options, cancellationToken: cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation fires mid-file via CopyToAsync
        }

        // If we got a result it should have no errors (completed before cancel or cancel was a no-op)
        result?.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_Cancelled_LeavesNoTempFile()
    {
        var file1 = _temp.CreateFile("a.txt", "content a");
        var file2 = _temp.CreateFile("b.txt", "content b");
        var file3 = _temp.CreateFile("c.txt", "content c");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var options = new ArchiveOptions
        {
            SourcePaths = [file1, file2, file3],
            DestinationFolder = _temp.Path,
            ArchiveName = "cancelled_output",
            Mode = ArchiveMode.SingleArchive
        };

        try
        {
            await _sut.ArchiveAsync(options, cancellationToken: cts.Token);
        }
        catch (OperationCanceledException) { }

        Directory.GetFiles(_temp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_SingleFile_ReportsMonotonicByteProgress()
    {
        string content = new string('x', 8 * 1024); // 8 KB — enough for multiple progress ticks
        var file = _temp.CreateFile("data.txt", content);

        var reports = new List<ProgressReport>();
        var progress = new Progress<ProgressReport>(r => reports.Add(r));

        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "progress_test",
            Mode = ArchiveMode.SingleArchive,
            CompressionLevel = System.IO.Compression.CompressionLevel.NoCompression
        };

        await _sut.ArchiveAsync(options, progress);
        await Task.Delay(50); // let Progress<ProgressReport> callbacks fire

        reports.Should().NotBeEmpty();
        reports[0].Percent.Should().Be(0);
        reports.Last().Percent.Should().Be(100);
        // Sequence must be non-decreasing
        reports.Select(r => r.Percent).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ArchiveAsync_CyrillicFilename_PreservedAfterRoundTrip()
    {
        string cyrillicName = "документ.txt";
        var file = _temp.CreateFile(cyrillicName);

        await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "cyrillic_test"
        });

        using var destDir = new TempDirectory();
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [Path.Combine(_temp.Path, "cyrillic_test.zip")],
            DestinationFolder = destDir.Path,
            Mode = ExtractMode.SeparateFolders
        });

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        Directory.GetFiles(destDir.Path, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Should().Contain(cyrillicName);
    }

    [Fact]
    public async Task ArchiveAsync_EmojiFilename_PreservedAfterRoundTrip()
    {
        string emojiName = "photo_🇺🇦.txt";
        var file = _temp.CreateFile(emojiName);

        await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "emoji_test"
        });

        using var destDir = new TempDirectory();
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [Path.Combine(_temp.Path, "emoji_test.zip")],
            DestinationFolder = destDir.Path,
            Mode = ExtractMode.SeparateFolders
        });

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        Directory.GetFiles(destDir.Path, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Should().Contain(emojiName);
    }

    // T-F22: Windows Long Path Support
    // Verifies archive/extract round-trip with a source path exceeding 260 characters.
    // Gracefully skips when the OS does not have long path support enabled.
    [Fact]
    public async Task ArchiveAsync_LongSourcePath_SucceedsWithoutTruncation()
    {
        // Build a path that exceeds MAX_PATH (260) by nesting 50-char segments
        string segment = new string('a', 50);
        string deepDir = _temp.Path;
        for (int i = 0; i < 6; i++)
            deepDir = Path.Combine(deepDir, segment);

        try
        {
            Directory.CreateDirectory(deepDir);
        }
        catch (PathTooLongException)
        {
            return; // long paths not enabled on this system — skip
        }
        catch (IOException)
        {
            return; // long paths not enabled on this system — skip
        }

        string longFilePath = Path.Combine(deepDir, "longpath_test.txt");
        File.WriteAllText(longFilePath, "long path content");

        longFilePath.Length.Should().BeGreaterThan(260);

        // --- Archive ---
        var archiveOptions = new ArchiveOptions
        {
            SourcePaths = [longFilePath],
            DestinationFolder = _temp.Path,
            ArchiveName = "longpath_archive"
        };

        var archiveResult = await _sut.ArchiveAsync(archiveOptions);

        archiveResult.Success.Should().BeTrue();
        archiveResult.Errors.Should().BeEmpty();
        archiveResult.CreatedFiles.Should().HaveCount(1);

        string zipPath = archiveResult.CreatedFiles[0];
        File.Exists(zipPath).Should().BeTrue();

        // --- Extract ---
        using var extractTemp = new TempDirectory();
        var extractOptions = new ExtractOptions
        {
            ArchivePaths = [zipPath],
            DestinationFolder = extractTemp.Path,
            Mode = ExtractMode.SingleFolder
        };

        var extractResult = await _sut.ExtractAsync(extractOptions);

        extractResult.Success.Should().BeTrue();
        extractResult.Errors.Should().BeEmpty();

        // The entry was stored under the bare filename — verify round-trip content
        string extractedFile = Path.Combine(extractTemp.Path, "longpath_test.txt");
        File.Exists(extractedFile).Should().BeTrue();
        File.ReadAllText(extractedFile).Should().Be("long path content");
    }

    // T-F23: Symlink and Junction Handling

    [Fact]
    public async Task ArchiveAsync_DirectoryWithFileSymlink_SymlinkSkippedRealFileArchived()
    {
        // Create source directory with a real file and a file symlink
        string sourceDir = Path.Combine(_temp.Path, "source");
        Directory.CreateDirectory(sourceDir);
        string realFile = Path.Combine(sourceDir, "real.txt");
        File.WriteAllText(realFile, "real content");
        string linkFile = Path.Combine(sourceDir, "link.txt");

        try
        {
            File.CreateSymbolicLink(linkFile, realFile);
        }
        catch (IOException)
        {
            return; // symlinks not supported on this system — skip
        }
        catch (UnauthorizedAccessException)
        {
            return; // Developer Mode not enabled — skip
        }

        var options = new ArchiveOptions
        {
            SourcePaths = [sourceDir],
            DestinationFolder = _temp.Path,
            ArchiveName = "symlink_test"
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().HaveCount(1);
        result.SkippedFiles.Should().ContainSingle(s => s.Path == linkFile);

        // The archive must contain the real file but NOT the symlink
        using var zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
        zip.Entries.Select(e => e.Name).Should().Contain("real.txt");
        zip.Entries.Select(e => e.Name).Should().NotContain("link.txt");
    }

    [Fact]
    public async Task ArchiveAsync_DirectoryWithDirectorySymlink_SymlinkSkippedNoInfiniteLoop()
    {
        // Create source directory with a subdirectory and a symlink that points back at it
        string sourceDir = Path.Combine(_temp.Path, "source");
        string realSubDir = Path.Combine(sourceDir, "real_sub");
        Directory.CreateDirectory(realSubDir);
        File.WriteAllText(Path.Combine(realSubDir, "file.txt"), "content");
        string linkDir = Path.Combine(sourceDir, "link_sub");

        try
        {
            // Circular: link_sub → source (ancestor), which would loop without our guard
            Directory.CreateSymbolicLink(linkDir, sourceDir);
        }
        catch (IOException)
        {
            return; // symlinks not supported — skip
        }
        catch (UnauthorizedAccessException)
        {
            return; // Developer Mode not enabled — skip
        }

        var options = new ArchiveOptions
        {
            SourcePaths = [sourceDir],
            DestinationFolder = _temp.Path,
            ArchiveName = "dirsymlink_test"
        };

        // Must complete without hanging (no infinite recursion on the circular symlink)
        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().ContainSingle(s => s.Path == linkDir);

        // real_sub/file.txt must be in the archive
        using var zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
        zip.Entries.Should().Contain(e => e.FullName.Contains("file.txt"));
    }

    // T-F21: Race Condition Handling During Traversal

    [Fact]
    public async Task ArchiveAsync_FileLockedDuringDirectoryTraversal_PerFileErrorRemainingFilesArchived()
    {
        // Simulate the race condition: locked.txt exists when Directory.EnumerateFiles
        // discovers it but is held with FileShare.None so FileStream.Open fails.
        // keep.txt must be archived successfully; the error must name locked.txt specifically.
        string sourceDir = Path.Combine(_temp.Path, "source");
        Directory.CreateDirectory(sourceDir);
        string keepFile = Path.Combine(sourceDir, "keep.txt");
        string lockedFile = Path.Combine(sourceDir, "locked.txt");
        File.WriteAllText(keepFile, "keep content");
        File.WriteAllText(lockedFile, "locked content");

        ArchiveResult result;
        // Exclusive lock: FileShare.None prevents any other FileStream.Open on this file,
        // simulating the race window where the file exists at scan time but is inaccessible at read time.
        using (var exclusiveLock = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = await _sut.ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [sourceDir],
                DestinationFolder = _temp.Path,
                ArchiveName = "race_test"
            });
        }

        // Exactly one error — the specific locked file, not the containing directory
        result.Errors.Should().HaveCount(1);
        result.Errors[0].SourcePath.Should().Be(lockedFile);
        result.Errors[0].Message.Should().NotBeNullOrEmpty();

        // keep.txt was processed without error
        result.Errors.Should().NotContain(e => e.SourcePath == keepFile);

        // The archive was committed and contains keep.txt
        result.CreatedFiles.Should().HaveCount(1);
        using var zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
        zip.Entries.Should().Contain(e => e.Name == "keep.txt");
        zip.Entries.Should().NotContain(e => e.Name == "locked.txt");
    }

    [Fact]
    public async Task ArchiveAsync_TopLevelSymlinkSource_SymlinkSkippedOperationSucceeds()
    {
        // A top-level source path that is itself a symlink should be skipped
        string realFile = _temp.CreateFile("real.txt", "content");
        string linkFile = Path.Combine(_temp.Path, "link.txt");

        try
        {
            File.CreateSymbolicLink(linkFile, realFile);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        var options = new ArchiveOptions
        {
            SourcePaths = [linkFile],
            DestinationFolder = _temp.Path,
            ArchiveName = "toplevel_symlink_test"
        };

        var result = await _sut.ArchiveAsync(options);

        // Symlink skipped → no files archived, no errors, SkippedFiles has the link
        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().ContainSingle(s => s.Path == linkFile);
    }
}
