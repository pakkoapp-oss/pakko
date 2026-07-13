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
    public async Task ArchiveAsync_NullArchiveName_SingleSource_AutoNamesFromSource()
    {
        var dir = Path.Combine(_temp.Path, "my_folder");
        Directory.CreateDirectory(dir);
        var options = new ArchiveOptions
        {
            SourcePaths = [dir],
            DestinationFolder = _temp.Path,
            ArchiveName = null
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().ContainSingle(f => Path.GetFileName(f) == "my_folder.zip");
    }

    // T-F99: Path.GetFileNameWithoutExtension returns "" for a path ending in a directory
    // separator with no name component (a real drive root, e.g. "Z:\", behaves the same way) -
    // a bare-drive-root single source became reachable once the shell extension registered a
    // Drive ItemType. Without the fallback this silently created a file literally named ".zip".
    [Fact]
    public async Task ArchiveAsync_NullArchiveName_SingleSourceEndingInSeparator_FallsBackToArchive()
    {
        var dir = Path.Combine(_temp.Path, "my_folder");
        Directory.CreateDirectory(dir);
        var options = new ArchiveOptions
        {
            SourcePaths = [dir + Path.DirectorySeparatorChar],
            DestinationFolder = _temp.Path,
            ArchiveName = null
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().ContainSingle(f => Path.GetFileName(f) == "archive.zip");
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
        // T-F87: the skipped source must be reported so MainViewModel's DeleteAfterOperation
        // cleanup knows this source was never actually archived.
        result.SkippedFiles.Should().Contain(s => s.Path == file);
    }

    // T-F87: SeparateArchives mode has its own conflict-skip branch (SingleArchive's is tested
    // above) — each skipped source must be recorded, not just silently continued past, so
    // DeleteAfterOperation cleanup doesn't delete a source that was never archived.
    [Fact]
    public async Task ArchiveAsync_SeparateArchivesConflictSkip_RecordsSkippedSource()
    {
        var file = _temp.CreateFile("source.txt");
        _temp.CreateFile("source.zip"); // pre-existing destination for SeparateArchives naming

        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            Mode = ArchiveMode.SeparateArchives,
            OnConflict = ConflictBehavior.Skip
        };

        var result = await _sut.ArchiveAsync(options);

        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().Contain(s => s.Path == file);
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

    // T-F31/T-F32: Deterministic Archive Output + Directory Traversal Ordering

    [Fact]
    public async Task ArchiveAsync_SameDirectoryTwice_EntryOrderIdentical()
    {
        // Populate a directory with multiple files whose names would sort differently
        // under filesystem order vs. ordinal order (upper/lower mix, numeric suffixes).
        string sourceDir = Path.Combine(_temp.Path, "source");
        Directory.CreateDirectory(sourceDir);
        string subDir = Path.Combine(sourceDir, "sub");
        Directory.CreateDirectory(subDir);

        File.WriteAllText(Path.Combine(sourceDir, "charlie.txt"), "c");
        File.WriteAllText(Path.Combine(sourceDir, "Alpha.txt"), "a");
        File.WriteAllText(Path.Combine(sourceDir, "bravo.txt"), "b");
        File.WriteAllText(Path.Combine(subDir, "zulu.txt"), "z");
        File.WriteAllText(Path.Combine(subDir, "echo.txt"), "e");

        using var dest1 = new TempDirectory();
        using var dest2 = new TempDirectory();

        var result1 = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir],
            DestinationFolder = dest1.Path,
            ArchiveName = "run1",
            CompressionLevel = System.IO.Compression.CompressionLevel.NoCompression
        });
        var result2 = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir],
            DestinationFolder = dest2.Path,
            ArchiveName = "run2",
            CompressionLevel = System.IO.Compression.CompressionLevel.NoCompression
        });

        result1.Success.Should().BeTrue();
        result2.Success.Should().BeTrue();

        using var zip1 = System.IO.Compression.ZipFile.OpenRead(result1.CreatedFiles[0]);
        using var zip2 = System.IO.Compression.ZipFile.OpenRead(result2.CreatedFiles[0]);

        var entries1 = zip1.Entries.Select(e => e.FullName).ToList();
        var entries2 = zip2.Entries.Select(e => e.FullName).ToList();

        entries1.Should().Equal(entries2);

        // Entries must be in ascending ordinal case-insensitive order within each directory level
        entries1.Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ArchiveAsync_SameDirectoryTwice_ProducesByteIdenticalZips()
    {
        // Byte-identical output requires: (1) sorted entry order, (2) deterministic timestamps
        // (entry.LastWriteTime pinned to source file's LastWriteTime), (3) deterministic
        // compression (Deflate is deterministic). NoCompression avoids any compression
        // variance and keeps the test fast.
        string sourceDir = Path.Combine(_temp.Path, "source");
        Directory.CreateDirectory(sourceDir);

        File.WriteAllText(Path.Combine(sourceDir, "beta.txt"), "hello beta");
        File.WriteAllText(Path.Combine(sourceDir, "alpha.txt"), "hello alpha");

        using var dest1 = new TempDirectory();
        using var dest2 = new TempDirectory();

        var result1 = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir],
            DestinationFolder = dest1.Path,
            ArchiveName = "run1",
            CompressionLevel = System.IO.Compression.CompressionLevel.NoCompression
        });
        var result2 = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir],
            DestinationFolder = dest2.Path,
            ArchiveName = "run2",
            CompressionLevel = System.IO.Compression.CompressionLevel.NoCompression
        });

        result1.Success.Should().BeTrue();
        result2.Success.Should().BeTrue();

        byte[] bytes1 = File.ReadAllBytes(result1.CreatedFiles[0]);
        byte[] bytes2 = File.ReadAllBytes(result2.CreatedFiles[0]);

        bytes1.Should().Equal(bytes2,
            "two archive runs over identical inputs must produce byte-identical ZIPs " +
            "(sorted entry order + source-file LastWriteTime pinned per T-F31)");
    }

    // T-F60 — cleanup bug: all sources missing leaves no .tmp and no .zip on disk
    [Fact]
    public async Task ArchiveAsync_AllSourcesMissing_LeavesNoDiskArtifacts()
    {
        string missing1 = Path.Combine(_temp.Path, "does_not_exist_1.txt");
        string missing2 = Path.Combine(_temp.Path, "does_not_exist_2.txt");
        var options = new ArchiveOptions
        {
            SourcePaths = [missing1, missing2],
            DestinationFolder = _temp.Path,
            ArchiveName = "output"
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.CreatedFiles.Should().BeEmpty();
        Directory.EnumerateFiles(_temp.Path).Should().BeEmpty(
            "no .zip or .tmp should be left when every source path is missing");
    }

    // T-F60 — partial success: one valid file + one missing path → partial archive committed
    [Fact]
    public async Task ArchiveAsync_OneValidOneInvalidSource_CreatesPartialArchive()
    {
        var validFile = _temp.CreateFile("real.txt", "hello");
        string missing = Path.Combine(_temp.Path, "ghost.txt");
        var options = new ArchiveOptions
        {
            SourcePaths = [validFile, missing],
            DestinationFolder = _temp.Path,
            ArchiveName = "partial"
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.CreatedFiles.Should().HaveCount(1, "the valid file must still be archived");
        File.Exists(result.CreatedFiles[0]).Should().BeTrue();
        Directory.EnumerateFiles(_temp.Path, "*.tmp").Should().BeEmpty(
            "no .tmp should remain after a partial-success archive");
    }

    // T-F66 — an empty folder writes no ZIP entry by itself, which used to make the T-F60
    // "no entries → discard" cleanup silently delete the archive. Archiving an empty folder
    // must still produce a .zip containing that folder as a directory entry.
    [Fact]
    public async Task ArchiveAsync_EmptyFolder_CreatesArchiveWithDirectoryEntry()
    {
        string emptyFolder = Path.Combine(_temp.Path, "EmptyFolder");
        Directory.CreateDirectory(emptyFolder);
        var options = new ArchiveOptions
        {
            SourcePaths = [emptyFolder],
            DestinationFolder = _temp.Path,
            ArchiveName = "output"
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().HaveCount(1);
        File.Exists(result.CreatedFiles[0]).Should().BeTrue();

        using var zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
        zip.Entries.Should().ContainSingle(e => e.FullName == "EmptyFolder/");
    }

    // T-F66 — an empty subfolder nested inside a non-empty folder must also be preserved.
    [Fact]
    public async Task ArchiveAsync_FolderWithEmptySubfolder_PreservesEmptySubfolderEntry()
    {
        string folder = Path.Combine(_temp.Path, "Parent");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "file.txt"), "hello");
        Directory.CreateDirectory(Path.Combine(folder, "EmptyChild"));
        var options = new ArchiveOptions
        {
            SourcePaths = [folder],
            DestinationFolder = _temp.Path,
            ArchiveName = "output"
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(1);

        using var zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
        // T-F75: entry names are relative to the archived root ("Parent/"), not to the
        // subfolder's own immediate parent — a nested empty folder keeps its full path.
        zip.Entries.Should().Contain(e => e.FullName == "Parent/EmptyChild/");
    }

    // T-F75: AddDirectoryToArchiveAsync previously recomputed each recursion level's relative
    // path against its own immediate parent instead of the original archived root, so every
    // level below the first lost its accumulated prefix entirely.
    [Fact]
    public async Task ArchiveAsync_ThreeLevelNesting_EntryNamesIncludeFullPathFromRoot()
    {
        string root = Path.Combine(_temp.Path, "notes");
        string level1 = Path.Combine(root, "level1");
        string level2 = Path.Combine(level1, "level2");
        Directory.CreateDirectory(level2);
        File.WriteAllText(Path.Combine(root, "top.txt"), "top");
        File.WriteAllText(Path.Combine(level1, "mid.txt"), "mid");
        File.WriteAllText(Path.Combine(level2, "deep.txt"), "deep");

        var result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [root],
            DestinationFolder = _temp.Path,
            ArchiveName = "three_level"
        });

        result.Success.Should().BeTrue();
        using var zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        names.Should().Contain("notes/top.txt");
        names.Should().Contain("notes/level1/mid.txt");
        names.Should().Contain("notes/level1/level2/deep.txt");
    }

    // T-F75: before the fix, two files at different depths whose paths relative to their OWN
    // immediate parent happened to match (both "a/file.txt" relative to their parent) collided
    // into the SAME entry name — CreateEntry allows duplicates, so both were written, and
    // extraction would silently clobber one with the other. This proves that data-loss case
    // is closed: both files must survive as distinct, correctly-prefixed entries.
    [Fact]
    public async Task ArchiveAsync_SiblingSubdirectoriesWithMatchingRelativeStructure_NoEntryCollision()
    {
        string root = Path.Combine(_temp.Path, "notes");
        string branchA = Path.Combine(root, "a");
        string branchB = Path.Combine(root, "b", "a");
        Directory.CreateDirectory(branchA);
        Directory.CreateDirectory(branchB);
        File.WriteAllText(Path.Combine(branchA, "file.txt"), "from a");
        File.WriteAllText(Path.Combine(branchB, "file.txt"), "from b/a");

        var result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [root],
            DestinationFolder = _temp.Path,
            ArchiveName = "collision_test"
        });

        result.Success.Should().BeTrue();
        using var zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        names.Should().Contain("notes/a/file.txt");
        names.Should().Contain("notes/b/a/file.txt");
        names.Should().OnlyHaveUniqueItems();

        using var extractDest = new TempDirectory();
        var extractResult = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [result.CreatedFiles[0]],
            DestinationFolder = extractDest.Path,
            Mode = ExtractMode.SingleFolder
        });

        extractResult.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(extractDest.Path, "a", "file.txt")).Should().Be("from a");
        File.ReadAllText(Path.Combine(extractDest.Path, "b", "a", "file.txt")).Should().Be("from b/a");
    }

    // T-F30: Duplicate Filename Detection Inside Archive

    [Fact]
    public async Task ArchiveAsync_TwoSourceFilesShareBasename_SecondRenamedWithSuffix()
    {
        string folderA = Path.Combine(_temp.Path, "A");
        string folderB = Path.Combine(_temp.Path, "B");
        Directory.CreateDirectory(folderA);
        Directory.CreateDirectory(folderB);
        string fileA = Path.Combine(folderA, "report.txt");
        string fileB = Path.Combine(folderB, "report.txt");
        File.WriteAllText(fileA, "content from A");
        File.WriteAllText(fileB, "content from B");

        var result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [fileA, fileB],
            DestinationFolder = _temp.Path,
            ArchiveName = "dup_files"
        });

        result.Success.Should().BeTrue();
        using var zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        // Sorted ordinal-case-insensitive input order (T-F31/T-F32) means fileA is processed
        // first and keeps the plain name; fileB collides and is renamed.
        names.Should().Contain("report.txt");
        names.Should().Contain("report (1).txt");
        names.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ArchiveAsync_TwoSourceDirectoriesShareBasename_SecondRenamedWithSuffix()
    {
        string parentA = Path.Combine(_temp.Path, "ParentA");
        string parentB = Path.Combine(_temp.Path, "ParentB");
        string dirA = Path.Combine(parentA, "notes");
        string dirB = Path.Combine(parentB, "notes");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        File.WriteAllText(Path.Combine(dirA, "file.txt"), "from A");
        File.WriteAllText(Path.Combine(dirB, "file.txt"), "from B");

        var result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [dirA, dirB],
            DestinationFolder = _temp.Path,
            ArchiveName = "dup_dirs"
        });

        result.Success.Should().BeTrue();
        using var zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        names.Should().Contain("notes/file.txt");
        names.Should().Contain("notes (1)/file.txt");
        names.Should().OnlyHaveUniqueItems();

        using var extractDest = new TempDirectory();
        var extractResult = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [result.CreatedFiles[0]],
            DestinationFolder = extractDest.Path,
            Mode = ExtractMode.SingleFolder
        });

        extractResult.Success.Should().BeTrue();
        // T-14 smart foldering: "notes" and "notes (1)" are two distinct roots, so extraction
        // wraps them in a subfolder named after the archive itself ("dup_dirs"), same as any
        // other multi-root archive (see extract_multiple_root_items.zip fixture scenario).
        string wrapped = Path.Combine(extractDest.Path, "dup_dirs");
        File.ReadAllText(Path.Combine(wrapped, "notes", "file.txt")).Should().Be("from A");
        File.ReadAllText(Path.Combine(wrapped, "notes (1)", "file.txt")).Should().Be("from B");
    }

    // T-F12: SeparateArchives now runs each SourcePath's archive in parallel via
    // Parallel.ForEachAsync. These tests target the concurrency-specific risks that don't exist
    // in a sequential loop: output corruption under real parallel writers, and correctness when
    // multiple SourcePaths would produce the same output filename.

    [Fact]
    public async Task ArchiveAsync_SeparateArchivesMode_ManyFiles_AllProduceCorrectContentNoCorruption()
    {
        const int fileCount = 20;
        var files = Enumerable.Range(1, fileCount)
            .Select(i => _temp.CreateFile($"item{i}.txt", $"content-{i}"))
            .ToList();

        var options = new ArchiveOptions
        {
            SourcePaths = files,
            DestinationFolder = _temp.Path,
            Mode = ArchiveMode.SeparateArchives
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(fileCount);

        for (int i = 1; i <= fileCount; i++)
        {
            string expectedZip = Path.Combine(_temp.Path, $"item{i}.zip");
            result.CreatedFiles.Should().Contain(expectedZip);
            using var zip = System.IO.Compression.ZipFile.OpenRead(expectedZip);
            zip.Entries.Should().ContainSingle();
            using var reader = new StreamReader(zip.Entries[0].Open());
            reader.ReadToEnd().Should().Be($"content-{i}");
        }
    }

    [Fact]
    public async Task ArchiveAsync_SeparateArchivesMode_TwoSourcesShareBasename_BothPreservedDistinctly()
    {
        // Two different directories named "Photos" under different parents — both would
        // naturally produce "Photos.zip", which is exactly the race T-F12's sequential
        // planning pre-pass exists to prevent (see ZipArchiveService.ArchiveAsync's
        // SeparateArchives branch).
        string parentA = Path.Combine(_temp.Path, "A");
        string parentB = Path.Combine(_temp.Path, "B");
        Directory.CreateDirectory(Path.Combine(parentA, "Photos"));
        Directory.CreateDirectory(Path.Combine(parentB, "Photos"));
        File.WriteAllText(Path.Combine(parentA, "Photos", "pic.txt"), "from A");
        File.WriteAllText(Path.Combine(parentB, "Photos", "pic.txt"), "from B");

        var options = new ArchiveOptions
        {
            SourcePaths = [Path.Combine(parentA, "Photos"), Path.Combine(parentB, "Photos")],
            DestinationFolder = _temp.Path,
            Mode = ArchiveMode.SeparateArchives,
            OnConflict = ConflictBehavior.Rename
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(2);

        var contents = result.CreatedFiles.Select(zipPath =>
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
            var entry = zip.Entries.Single(e => e.FullName.EndsWith("pic.txt"));
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }).ToList();

        contents.Should().BeEquivalentTo(["from A", "from B"]);
    }

    [Fact]
    public async Task ArchiveAsync_SeparateArchivesMode_MaxDegreeOfParallelismCapped_StillCompletesCorrectly()
    {
        // Not a direct assertion on concurrency (Environment.ProcessorCount cap is enforced by
        // Parallel.ForEachAsync itself) — this exercises a batch comfortably larger than typical
        // core counts to make sure the cap doesn't drop or duplicate any work.
        var files = Enumerable.Range(1, 12)
            .Select(i => _temp.CreateFile($"batch{i}.txt", $"payload-{i}"))
            .ToList();

        var options = new ArchiveOptions
        {
            SourcePaths = files,
            DestinationFolder = _temp.Path,
            Mode = ArchiveMode.SeparateArchives
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().HaveCount(12);
        result.CreatedFiles.Should().OnlyHaveUniqueItems();
    }
}
