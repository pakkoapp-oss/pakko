using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

public sealed class ZipArchiveServiceArchiveTests : IDisposable
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private sealed class SynchronousProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    [Fact]
    public async Task ArchiveAsync_SingleFile_CreatesZip()
    {
        string file = _temp.CreateFile("document.txt");
        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "output"
        };

        ArchiveResult result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(1);
        File.Exists(result.CreatedFiles[0]).Should().BeTrue();
        result.CreatedFiles[0].Should().EndWith(".zip");
    }

    // T-F178 (test-coverage audit): MAX_PATH (260 chars) was only tested at Archiver.Shell's
    // argument-parser layer (ExtractHere_PathExceeding260Chars_ParsedCorrectlyNoTruncation) — never
    // where the actual I/O happens. Builds a real on-disk source path over 260 chars via nested
    // directories (not just a long single component) to match how a real deep folder tree gets
    // long, then archives it — must never throw unhandled, per this project's own "IO exceptions
    // caught per-item, methods never throw to callers" hard constraint.
    [Fact]
    public async Task ArchiveAsync_SourcePathBeyond260Chars_SucceedsOrRecordsPerItemErrorNeverThrows()
    {
        string deepDir = _temp.Path;
        while (deepDir.Length < 300)
        {
            deepDir = Path.Combine(deepDir, new string('a', 40));
            Directory.CreateDirectory(deepDir);
        }
        deepDir.Length.Should().BeGreaterThan(260);
        File.WriteAllText(Path.Combine(deepDir, "deep.txt"), "long path content");

        var options = new ArchiveOptions
        {
            SourcePaths = [deepDir],
            DestinationFolder = _temp.Path,
            ArchiveName = "long-path-output",
        };

        ArchiveResult result = await _sut.ArchiveAsync(options);

        result.Should().NotBeNull();
        (result.Success || result.Errors.Count > 0).Should().BeTrue(
            "a >260-char source path must either succeed or record a per-item ArchiveError, " +
            "never silently produce neither");
    }

    [Fact]
    public async Task ArchiveAsync_NullArchiveName_SingleSource_AutoNamesFromSource()
    {
        string dir = Path.Combine(_temp.Path, "my_folder");
        Directory.CreateDirectory(dir);
        var options = new ArchiveOptions
        {
            SourcePaths = [dir],
            DestinationFolder = _temp.Path,
            ArchiveName = null
        };

        ArchiveResult result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().ContainSingle(f => Path.GetFileName(f) == "my_folder.zip");
    }

    // T-F153: a source path ending in an ordinary directory separator (e.g. typed with tab-
    // completion, or "my_folder\") must still resolve to that folder's own real name, not fall
    // back to "archive" — ArchiveAsync normalizes the trailing separator away before naming, so
    // this now behaves identically to the no-trailing-separator case. The "falls back to archive"
    // scenario (ArchiveNamingTests.ResolveSingleArchiveName_NoExplicitName_DriveRootSource_...)
    // is reserved for a REAL drive root ("Z:\", where TrimEndingDirectorySeparator deliberately
    // leaves the separator in place) — this test used to conflate the two.
    [Fact]
    public async Task ArchiveAsync_NullArchiveName_SingleSourceEndingInSeparator_UsesFolderNameNotArchive()
    {
        string dir = Path.Combine(_temp.Path, "my_folder");
        Directory.CreateDirectory(dir);
        var options = new ArchiveOptions
        {
            SourcePaths = [dir + Path.DirectorySeparatorChar],
            DestinationFolder = _temp.Path,
            ArchiveName = null
        };

        ArchiveResult result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().ContainSingle(f => Path.GetFileName(f) == "my_folder.zip");
    }

    // T-F153: the real bug this was found from — a trailing separator on the source path made
    // Path.GetFileName(sourcePath) return "", so every entry was written rooted at the archive's
    // own top level ("/binary.dat") instead of under the source folder's own name
    // ("my_folder/binary.dat"). Confirmed via a real 7za.exe read-back during the original repro
    // (an independent reader, not just .NET's own lenient ZipArchive), not just this test's own
    // ZipFile-based assertion.
    [Fact]
    public async Task ArchiveAsync_SourceEndingInSeparator_EntriesAreRootedUnderFolderName()
    {
        string dir = Path.Combine(_temp.Path, "my_folder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "inner.txt"), "content");
        var options = new ArchiveOptions
        {
            SourcePaths = [dir + Path.DirectorySeparatorChar],
            DestinationFolder = _temp.Path,
            ArchiveName = "output"
        };

        ArchiveResult result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        using ZipArchive archive = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
        archive.Entries.Should().ContainSingle(e => e.FullName == "my_folder/inner.txt");
    }

    [Fact]
    public async Task ArchiveAsync_MultipleFiles_SingleArchiveMode_CreatesOneZip()
    {
        string file1 = _temp.CreateFile("a.txt");
        string file2 = _temp.CreateFile("b.txt");
        var options = new ArchiveOptions
        {
            SourcePaths = [file1, file2],
            DestinationFolder = _temp.Path,
            ArchiveName = "combined",
            Mode = ArchiveMode.SingleArchive
        };

        ArchiveResult result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(1);
    }

    [Fact]
    public async Task ArchiveAsync_MultipleFiles_SeparateArchivesMode_CreatesMultipleZips()
    {
        string file1 = _temp.CreateFile("a.txt");
        string file2 = _temp.CreateFile("b.txt");
        var options = new ArchiveOptions
        {
            SourcePaths = [file1, file2],
            DestinationFolder = _temp.Path,
            Mode = ArchiveMode.SeparateArchives
        };

        ArchiveResult result = await _sut.ArchiveAsync(options);

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

        ArchiveResult result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].SourcePath.Should().Be(@"C:\does\not\exist.txt");
        result.Errors[0].Message.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_CancellationRequested_ThrowsAndCreatesNothing()
    {
        // T-F260: this used to return an empty, successful-looking result, which let "Delete
        // after operation" delete every source; a cancel now always throws.
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

        Func<Task<ArchiveResult>> act = () => _sut.ArchiveAsync(options, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFiles(_temp.Path, "*.zip").Should().BeEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_ConflictSkip_DoesNotOverwriteExistingZip()
    {
        string file = _temp.CreateFile("source.txt");
        string existingZip = _temp.CreateFile("output.zip");
        DateTime originalWriteTime = File.GetLastWriteTimeUtc(existingZip);

        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "output",
            OnConflict = ConflictBehavior.Skip
        };

        ArchiveResult result = await _sut.ArchiveAsync(options);

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
        string file = _temp.CreateFile("source.txt");
        _temp.CreateFile("source.zip"); // pre-existing destination for SeparateArchives naming

        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            Mode = ArchiveMode.SeparateArchives,
            OnConflict = ConflictBehavior.Skip
        };

        ArchiveResult result = await _sut.ArchiveAsync(options);

        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().Contain(s => s.Path == file);
    }

    [Fact]
    public async Task ArchiveAsync_ConflictRename_CreatesNumberedZipWhenOutputExists()
    {
        string file = _temp.CreateFile("source.txt");
        _temp.CreateFile("output.zip");

        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "output",
            OnConflict = ConflictBehavior.Rename
        };

        ArchiveResult result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(1);
        result.CreatedFiles[0].Should().EndWith("output (1).zip");
        File.Exists(result.CreatedFiles[0]).Should().BeTrue();
    }

    [Fact]
    public async Task ArchiveAsync_ConflictOverwrite_ReplacesExistingZip()
    {
        string file = _temp.CreateFile("source.txt");
        string existingZip = _temp.CreateFile("output.zip");

        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "output",
            OnConflict = ConflictBehavior.Overwrite
        };

        ArchiveResult result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(1);
        result.CreatedFiles[0].Should().Be(existingZip);
    }

    // T-F06: SingleArchive mode's Ask path — a single one-shot conflict, resolved via the caller's
    // ResolveConflictAsync callback instead of a pre-selected ConflictBehavior.
    [Theory]
    [InlineData(ConflictResolution.Overwrite)]
    [InlineData(ConflictResolution.Rename)]
    [InlineData(ConflictResolution.Skip)]
    public async Task ArchiveAsync_ConflictAsk_SingleArchive_AppliesCallbackResolution(ConflictResolution resolution)
    {
        string file = _temp.CreateFile("source.txt");
        string existingZip = _temp.CreateFile("output.zip");

        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "output",
            OnConflict = ConflictBehavior.Ask,
            ResolveConflictAsync = _ => Task.FromResult(new ConflictDecision { Resolution = resolution })
        };

        ArchiveResult result = await _sut.ArchiveAsync(options);

        switch (resolution)
        {
            case ConflictResolution.Overwrite:
                result.CreatedFiles.Should().ContainSingle().Which.Should().Be(existingZip);
                break;
            case ConflictResolution.Rename:
                result.CreatedFiles.Should().ContainSingle(f => f.EndsWith("output (1).zip"));
                break;
            case ConflictResolution.Skip:
                result.CreatedFiles.Should().BeEmpty();
                result.SkippedFiles.Should().Contain(s => s.Path == file);
                break;
        }
    }

    // T-F220 item 2: the App's "cancel all" ends the prompt as a cancelled task; creation stops
    // there as a cancel (T-F260) and the existing archive stays as it was.
    [Theory]
    [InlineData(ArchiveMode.SingleArchive)]
    [InlineData(ArchiveMode.SeparateArchives)]
    public async Task ArchiveAsync_ConflictPromptCancelled_ThrowsAndLeavesTheExistingArchive(ArchiveMode mode)
    {
        string file = _temp.CreateFile("source.txt");
        string existingZip = Path.Combine(_temp.Path, mode == ArchiveMode.SingleArchive ? "output.zip" : "source.zip");
        File.WriteAllText(existingZip, "old");
        using var cts = new CancellationTokenSource();

        Func<Task<ArchiveResult>> act = () => _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = mode == ArchiveMode.SingleArchive ? "output" : null,
            Mode = mode,
            OnConflict = ConflictBehavior.Ask,
            ResolveConflictAsync = _ =>
            {
                cts.Cancel();
                return Task.FromCanceled<ConflictDecision>(cts.Token);
            },
        }, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllText(existingZip).Should().Be("old");
        Directory.GetFiles(_temp.Path, "*.zip").Should().ContainSingle();
    }

    // T-F268 step 5: a new archive has no size or time yet, so the prompt shows only the existing one.
    [Fact]
    public async Task ArchiveAsync_ConflictAsk_HasNoIncomingSizeOrTime()
    {
        string file = _temp.CreateFile("source.txt");
        _temp.CreateFile("output.zip");

        ConflictInfo? asked = null;
        await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "output",
            OnConflict = ConflictBehavior.Ask,
            ResolveConflictAsync = info =>
            {
                asked = info;
                return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Skip });
            }
        });

        asked.Should().NotBeNull();
        asked!.IncomingSize.Should().BeNull();
        asked.IncomingModified.Should().BeNull();
    }

    // T-F06: SeparateArchives mode's sequential pre-pass loop shares one ConflictResolver
    // instance across all sources in the batch — ApplyToAll on the first conflict must suppress
    // the callback for every subsequent conflicting source, not just apply to the first one.
    [Fact]
    public async Task ArchiveAsync_ConflictAsk_SeparateArchives_ApplyToAll_InvokesCallbackOnce()
    {
        string file1 = _temp.CreateFile("first.txt");
        string file2 = _temp.CreateFile("second.txt");
        _temp.CreateFile("first.zip");
        _temp.CreateFile("second.zip");

        int callCount = 0;
        var options = new ArchiveOptions
        {
            SourcePaths = [file1, file2],
            DestinationFolder = _temp.Path,
            Mode = ArchiveMode.SeparateArchives,
            OnConflict = ConflictBehavior.Ask,
            ResolveConflictAsync = _ =>
            {
                callCount++;
                return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Rename, ApplyToAll = true });
            }
        };

        ArchiveResult result = await _sut.ArchiveAsync(options);

        result.CreatedFiles.Should().HaveCount(2);
        result.CreatedFiles.Should().Contain(f => f.EndsWith("first (1).zip"));
        result.CreatedFiles.Should().Contain(f => f.EndsWith("second (1).zip"));
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task ArchiveAsync_ReportsProgress()
    {
        var files = Enumerable.Range(1, 5)
            .Select(i => _temp.CreateFile($"file{i}.txt"))
            .ToList();

        // T-F162's pattern (failed once in a full-suite run, 2026-10-01); SeparateArchives reports
        // from several workers, so the list is locked.
        var reports = new List<ProgressReport>();
        var progress = new SynchronousProgress<ProgressReport>(r => { lock (reports) reports.Add(r); });

        var options = new ArchiveOptions
        {
            SourcePaths = files,
            DestinationFolder = _temp.Path,
            Mode = ArchiveMode.SeparateArchives
        };

        await _sut.ArchiveAsync(options, progress);

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
        string file1 = _temp.CreateFile("a.txt", "content a");
        string file2 = _temp.CreateFile("b.txt", "content b");
        string file3 = _temp.CreateFile("c.txt", "content c");

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
        string file = _temp.CreateFile("data.txt", content);

        var reports = new List<ProgressReport>();
        // T-F162's pattern: Progress<T> posts to the ThreadPool, and under a full-suite run the
        // posts could land after the assertions (seen 2026-09-30); report on the calling thread.
        var progress = new SynchronousProgress<ProgressReport>(reports.Add);

        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "progress_test",
            Mode = ArchiveMode.SingleArchive,
            CompressionLevel = System.IO.Compression.CompressionLevel.NoCompression
        };

        await _sut.ArchiveAsync(options, progress);

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
        string file = _temp.CreateFile(cyrillicName);

        await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "cyrillic_test"
        });

        using var destDir = new TempDirectory();
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
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
        string file = _temp.CreateFile(emojiName);

        await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "emoji_test"
        });

        using var destDir = new TempDirectory();
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
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

    // T-F184 (test-coverage audit): the Unicode right-to-left-override character (U+202E) is a
    // known filename-spoofing vector (e.g. disguising a real ".exe" as an apparent ".txt" when
    // rendered right-to-left) — this test only proves the ZIP round-trip itself doesn't corrupt,
    // truncate, or mishandle a filename containing it (a real security concern for THIS archiver:
    // a crash/mis-parse on a hostile filename), not that Explorer's rendering is spoof-proof
    // (that's a Windows shell display concern, out of this project's scope).
    [Fact]
    public async Task ArchiveAsync_RtlOverrideFilename_RoundTripsWithoutCorruptionOrCrash()
    {
        string rtlName = "invoice_\u202Etxt.exe";
        string file = _temp.CreateFile(rtlName);

        ArchiveResult archiveResult = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "rtl_test"
        });
        archiveResult.Success.Should().BeTrue();

        using var destDir = new TempDirectory();
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [Path.Combine(_temp.Path, "rtl_test.zip")],
            DestinationFolder = destDir.Path,
            Mode = ExtractMode.SeparateFolders
        });

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        Directory.GetFiles(destDir.Path, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Should().Contain(rtlName);
    }

    // Zalgo-style heavily-stacked combining diacritical marks (U+0300-U+036F) — a real, if
    // unusual, valid NTFS filename. Round-trips or fails safely (no crash, no silent truncation
    // that could collide two distinct entries into one on extraction).
    [Fact]
    public async Task ArchiveAsync_ZalgoStyleCombiningCharacterFilename_RoundTripsOrFailsSafely()
    {
        IEnumerable<char> combining = Enumerable.Range(0, 40).Select(i => (char)(0x0300 + (i % 0x20)));
        string zalgoName = "z" + new string(combining.ToArray()) + ".txt";
        string file = _temp.CreateFile(zalgoName);

        ArchiveResult archiveResult = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "zalgo_test"
        });

        // "Fails safely" is an accepted outcome per this test's own name — but if it succeeds, the
        // round trip must be byte-for-byte faithful, never a silently mangled/truncated name.
        if (!archiveResult.Success)
        {
            archiveResult.Errors.Should().NotBeEmpty();
            return;
        }

        using var destDir = new TempDirectory();
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [Path.Combine(_temp.Path, "zalgo_test.zip")],
            DestinationFolder = destDir.Path,
            Mode = ExtractMode.SeparateFolders
        });

        result.Success.Should().BeTrue();
        Directory.GetFiles(destDir.Path, "*", SearchOption.AllDirectories).Should().HaveCount(1);
        Directory.GetFiles(destDir.Path, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Should().Contain(zalgoName);
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

        ArchiveResult archiveResult = await _sut.ArchiveAsync(archiveOptions);

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

        ArchiveResult extractResult = await _sut.ExtractAsync(extractOptions);

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

        ArchiveResult result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().HaveCount(1);
        result.SkippedFiles.Should().ContainSingle(s => s.Path == linkFile);

        // The archive must contain the real file but NOT the symlink
        using ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
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
        ArchiveResult result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().ContainSingle(s => s.Path == linkDir);

        // real_sub/file.txt must be in the archive
        using ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
        zip.Entries.Should().Contain(e => e.FullName.Contains("file.txt"));
    }

    // T-F166: a real NTFS junction is a different reparse tag (IO_REPARSE_TAG_MOUNT_POINT) than a
    // symlink (IO_REPARSE_TAG_SYMLINK) — .NET has no CreateJunction API, so this shells out to the
    // same `mklink /J` a developer would use manually, via cmd.exe's absolute path per this
    // project's system-executable rule. Confirms ArchiveEntrySecurity.IsReparsePoint's generic
    // FileAttributes.ReparsePoint check (used by the T-F23 symlink tests above) also catches a
    // junction specifically, not just symlinks.
    [Fact]
    public async Task ArchiveAsync_DirectoryWithJunction_JunctionSkippedRealFileArchived()
    {
        string sourceDir = Path.Combine(_temp.Path, "source");
        Directory.CreateDirectory(sourceDir);
        string realTargetDir = Path.Combine(_temp.Path, "junction_target");
        Directory.CreateDirectory(realTargetDir);
        File.WriteAllText(Path.Combine(realTargetDir, "target_file.txt"), "target content");
        string junctionDir = Path.Combine(sourceDir, "link_junction");

        if (!TryCreateJunction(junctionDir, realTargetDir))
            return; // junctions not supported on this system — skip

        try
        {
            string realFile = Path.Combine(sourceDir, "real.txt");
            File.WriteAllText(realFile, "real content");

            var options = new ArchiveOptions
            {
                SourcePaths = [sourceDir],
                DestinationFolder = _temp.Path,
                ArchiveName = "junction_test"
            };

            ArchiveResult result = await _sut.ArchiveAsync(options);

            result.Success.Should().BeTrue();
            result.Errors.Should().BeEmpty();
            result.SkippedFiles.Should().ContainSingle(s => s.Path == junctionDir);

            using ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
            zip.Entries.Select(e => e.Name).Should().Contain("real.txt");
            zip.Entries.Should().NotContain(e => e.FullName.Contains("target_file.txt"));
        }
        finally
        {
            // .NET's recursive Directory.Delete (used by TempDirectory.Dispose) does not safely
            // skip over a junction it encounters mid-tree — remove the reparse point itself
            // (non-recursive: this deletes the junction, never the real target it points to)
            // before the outer temp-dir cleanup runs.
            Directory.Delete(junctionDir, recursive: false);
        }
    }

    // Returns false (caller should skip the test) if junction creation isn't possible on this
    // system — matches the existing symlink tests' IOException/UnauthorizedAccessException
    // tolerance above.
    internal static bool TryCreateJunction(string linkPath, string targetPath)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = @"C:\Windows\System32\cmd.exe",
            ArgumentList = { "/c", "mklink", "/J", linkPath, targetPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = System.Diagnostics.Process.Start(psi);
        process!.WaitForExit();
        return process.ExitCode == 0 && Directory.Exists(linkPath);
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
        using ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
        zip.Entries.Should().Contain(e => e.Name == "keep.txt");
        zip.Entries.Should().NotContain(e => e.Name == "locked.txt");
    }

    [Fact]
    public async Task ArchiveAsync_SingleArchiveMode_OlderFixedTempNameHeld_StillCreatesArchive()
    {
        // T-F312: "<archive>.tmp" left by a killed run, or held by a sync client, failed every later
        // run. It is not Pakko's to delete (its owner cannot be proved), so it is left as it is.
        string file = _temp.CreateFile("document.txt");
        string destPath = Path.Combine(_temp.Path, "locked_archive.zip");
        string olderTempPath = destPath + ".tmp";

        ArchiveResult result;
        using (new FileStream(olderTempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            result = await _sut.ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [file],
                DestinationFolder = _temp.Path,
                ArchiveName = "locked_archive",
                Mode = ArchiveMode.SingleArchive
            });
        }

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().Equal(destPath);
        File.Exists(olderTempPath).Should().BeTrue();
        (File.GetAttributes(destPath) & FileAttributes.Hidden).Should().Be((FileAttributes)0);
        Directory.GetFiles(_temp.Path, ".pakko-a-*").Should().BeEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_SingleArchiveMode_ArchiveNameIsAFolder_RecordsErrorAndLeavesNoTemp()
    {
        // T-F143: covers ArchiveAsync's SingleArchive-mode OUTER IOException catch — the commit
        // cannot replace a folder, and a folder is not a held file worth retrying for.
        string file = _temp.CreateFile("document.txt");
        string destPath = Path.Combine(_temp.Path, "folder_archive.zip");
        Directory.CreateDirectory(destPath);

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "folder_archive",
            Mode = ArchiveMode.SingleArchive
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.SourcePath == destPath);
        result.CreatedFiles.Should().BeEmpty();
        Directory.GetFiles(_temp.Path, "*.tmp").Should().BeEmpty();
    }

    // T-F321: two runs creating the same archive name at once (two Explorer clicks, two `pakko a`)
    // both found the name free; the later commit replaced the earlier archive and both reported
    // success. Whatever the timing, every archive a run reports must be on disk with its own entries.
    [Fact]
    public async Task ArchiveAsync_TwoRunsCreateTheSameNameAtOnce_NeitherReportedArchiveIsLost()
    {
        string[] folders = ["first", "second"];
        foreach (string folder in folders)
            Directory.CreateDirectory(Path.Combine(_temp.Path, folder));
        foreach (string folder in folders)
            for (int i = 0; i < 200; i++)
                _temp.CreateFile(Path.Combine(folder, $"{folder}_{i}.txt"), folder);

        ArchiveResult[] results = await Task.WhenAll(folders.Select(folder => Task.Run(() => _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [Path.Combine(_temp.Path, folder)],
            DestinationFolder = _temp.Path,
            ArchiveName = "same",
            Mode = ArchiveMode.SingleArchive,
            OnConflict = ConflictBehavior.Skip,
        }))));

        for (int run = 0; run < results.Length; run++)
        {
            foreach (string created in results[run].CreatedFiles)
            {
                using ZipArchive zip = ZipFile.OpenRead(created);
                zip.Entries.Should().Contain(e => e.FullName.StartsWith(folders[run] + "/"),
                    because: $"run '{folders[run]}' reported {Path.GetFileName(created)} as its archive");
            }
        }
    }

    // T-F321: the SeparateArchives commit — two runs each archiving a folder named "x" from
    // different parents into one destination folder both target x.zip.
    [Fact]
    public async Task ArchiveAsync_SeparateArchives_TwoRunsCreateTheSameNameAtOnce_NeitherReportedArchiveIsLost()
    {
        string[] parents = ["first", "second"];
        string destination = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(destination);
        foreach (string parent in parents)
        {
            Directory.CreateDirectory(Path.Combine(_temp.Path, parent, "x"));
            for (int i = 0; i < 200; i++)
                _temp.CreateFile(Path.Combine(parent, "x", $"{parent}_{i}.txt"), parent);
        }

        ArchiveResult[] results = await Task.WhenAll(parents.Select(parent => Task.Run(() => _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [Path.Combine(_temp.Path, parent, "x")],
            DestinationFolder = destination,
            Mode = ArchiveMode.SeparateArchives,
            OnConflict = ConflictBehavior.Skip,
        }))));

        for (int run = 0; run < results.Length; run++)
        {
            foreach (string created in results[run].CreatedFiles)
            {
                using ZipArchive zip = ZipFile.OpenRead(created);
                zip.Entries.Should().Contain(e => e.Name.StartsWith(parents[run] + "_"),
                    because: $"run '{parents[run]}' reported {Path.GetFileName(created)} as its archive");
            }
        }
    }

    // T-F312: Overwrite onto an archive a sync client holds for a moment — the old archive stays
    // until the new one is complete, and the rename waits for the holder.
    [Fact]
    public async Task ArchiveAsync_OverwriteArchiveHeldBriefly_ReplacesIt()
    {
        string file = _temp.CreateFile("document.txt", "new");
        string destPath = _temp.CreateFile("held.zip", "old archive");
        var held = new FileStream(destPath, FileMode.Open, FileAccess.Read, FileShare.None);
        _ = Task.Delay(300).ContinueWith(_ => held.Dispose(), TaskScheduler.Default);

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "held",
            Mode = ArchiveMode.SingleArchive,
            OnConflict = ConflictBehavior.Overwrite,
        });

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        using ZipArchive archive = ZipFile.OpenRead(destPath);
        archive.Entries.Should().ContainSingle(e => e.Name == "document.txt");
    }

    [Fact]
    public async Task ArchiveAsync_OverwriteArchiveHeldThroughout_RecordsErrorKeepsOldArchive()
    {
        string file = _temp.CreateFile("document.txt", "new");
        string destPath = _temp.CreateFile("held.zip", "old archive");

        ArchiveResult result;
        using (new FileStream(destPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = await _sut.ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [file],
                DestinationFolder = _temp.Path,
                ArchiveName = "held",
                Mode = ArchiveMode.SingleArchive,
                OnConflict = ConflictBehavior.Overwrite,
            });
        }

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.SourcePath == destPath);
        File.ReadAllText(destPath).Should().Be("old archive");
        Directory.GetFiles(_temp.Path, "*.tmp").Should().BeEmpty();
    }

    // T-F312 closing review: with the up-front delete gone, Overwrite of an archive that lies inside
    // the folder being archived packed the old archive into the new one.
    [Theory]
    [InlineData(ArchiveMode.SingleArchive)]
    [InlineData(ArchiveMode.SeparateArchives)]
    public async Task ArchiveAsync_OverwriteArchiveInsideTheSourceFolder_OldArchiveNotPacked(ArchiveMode mode)
    {
        string folder = Path.Combine(_temp.Path, "folder");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "a.txt"), "a");
        string destPath = Path.Combine(folder, "folder.zip");
        File.WriteAllText(destPath, "old archive");

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [folder],
            DestinationFolder = folder,
            ArchiveName = mode == ArchiveMode.SingleArchive ? "folder" : null,
            Mode = mode,
            OnConflict = ConflictBehavior.Overwrite,
        });

        result.CreatedFiles.Should().Equal(destPath);
        using ZipArchive archive = ZipFile.OpenRead(destPath);
        archive.Entries.Select(e => e.Name).Should().NotContain("folder.zip");
        archive.Entries.Select(e => e.Name).Should().Contain("a.txt");
    }

    [Fact]
    public async Task ArchiveAsync_LongArchiveName_Works()
    {
        // T-F312: the temporary name does not repeat the archive's name, so a name near the
        // 255-character limit still fits.
        string file = _temp.CreateFile("document.txt");
        string name = new('n', 240);

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = name,
            Mode = ArchiveMode.SingleArchive
        });

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(_temp.Path, name + ".zip")).Should().BeTrue();
    }

    [Fact]
    public async Task ArchiveAsync_SeparateArchivesMode_OlderFixedTempNameHeld_StillCreatesArchive()
    {
        // T-F312: mirrors the SingleArchive-mode test above for ArchiveSingleSeparatePathAsync.
        string sourceDir = Path.Combine(_temp.Path, "source_dir");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "inner.txt"), "content");
        string destPath = Path.Combine(_temp.Path, "source_dir.zip");
        string olderTempPath = destPath + ".tmp";

        ArchiveResult result;
        using (new FileStream(olderTempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            result = await _sut.ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [sourceDir],
                DestinationFolder = _temp.Path,
                Mode = ArchiveMode.SeparateArchives
            });
        }

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().Equal(destPath);
        File.Exists(olderTempPath).Should().BeTrue();
        Directory.GetFiles(_temp.Path, ".pakko-a-*").Should().BeEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_SeparateArchivesMode_ArchiveNameIsAFolder_RecordsErrorAndLeavesNoTemp()
    {
        // T-F143: ArchiveSingleSeparatePathAsync's own outer IOException catch.
        string sourceDir = Path.Combine(_temp.Path, "source_dir");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "inner.txt"), "content");
        Directory.CreateDirectory(Path.Combine(_temp.Path, "source_dir.zip"));

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir],
            DestinationFolder = _temp.Path,
            Mode = ArchiveMode.SeparateArchives
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.SourcePath == sourceDir);
        result.CreatedFiles.Should().BeEmpty();
        Directory.GetFiles(_temp.Path, "*.tmp").Should().BeEmpty();
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

        ArchiveResult result = await _sut.ArchiveAsync(options);

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

        ArchiveResult result1 = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir],
            DestinationFolder = dest1.Path,
            ArchiveName = "run1",
            CompressionLevel = System.IO.Compression.CompressionLevel.NoCompression
        });
        ArchiveResult result2 = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir],
            DestinationFolder = dest2.Path,
            ArchiveName = "run2",
            CompressionLevel = System.IO.Compression.CompressionLevel.NoCompression
        });

        result1.Success.Should().BeTrue();
        result2.Success.Should().BeTrue();

        using ZipArchive zip1 = System.IO.Compression.ZipFile.OpenRead(result1.CreatedFiles[0]);
        using ZipArchive zip2 = System.IO.Compression.ZipFile.OpenRead(result2.CreatedFiles[0]);

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

        ArchiveResult result1 = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir],
            DestinationFolder = dest1.Path,
            ArchiveName = "run1",
            CompressionLevel = System.IO.Compression.CompressionLevel.NoCompression
        });
        ArchiveResult result2 = await _sut.ArchiveAsync(new ArchiveOptions
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

        ArchiveResult result = await _sut.ArchiveAsync(options);

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
        string validFile = _temp.CreateFile("real.txt", "hello");
        string missing = Path.Combine(_temp.Path, "ghost.txt");
        var options = new ArchiveOptions
        {
            SourcePaths = [validFile, missing],
            DestinationFolder = _temp.Path,
            ArchiveName = "partial"
        };

        ArchiveResult result = await _sut.ArchiveAsync(options);

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

        ArchiveResult result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().HaveCount(1);
        File.Exists(result.CreatedFiles[0]).Should().BeTrue();

        using ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
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

        ArchiveResult result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(1);

        using ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
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

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [root],
            DestinationFolder = _temp.Path,
            ArchiveName = "three_level"
        });

        result.Success.Should().BeTrue();
        using ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
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

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [root],
            DestinationFolder = _temp.Path,
            ArchiveName = "collision_test"
        });

        result.Success.Should().BeTrue();
        using ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        names.Should().Contain("notes/a/file.txt");
        names.Should().Contain("notes/b/a/file.txt");
        names.Should().OnlyHaveUniqueItems();

        using var extractDest = new TempDirectory();
        ArchiveResult extractResult = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [result.CreatedFiles[0]],
            DestinationFolder = extractDest.Path,
            Mode = ExtractMode.SingleFolder
        });

        extractResult.Success.Should().BeTrue();
        // T-F205: the "notes" root folder is kept.
        File.ReadAllText(Path.Combine(extractDest.Path, "notes", "a", "file.txt")).Should().Be("from a");
        File.ReadAllText(Path.Combine(extractDest.Path, "notes", "b", "a", "file.txt")).Should().Be("from b/a");
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

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [fileA, fileB],
            DestinationFolder = _temp.Path,
            ArchiveName = "dup_files"
        });

        result.Success.Should().BeTrue();
        using ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
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

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [dirA, dirB],
            DestinationFolder = _temp.Path,
            ArchiveName = "dup_dirs"
        });

        result.Success.Should().BeTrue();
        using ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(result.CreatedFiles[0]);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        names.Should().Contain("notes/file.txt");
        names.Should().Contain("notes (1)/file.txt");
        names.Should().OnlyHaveUniqueItems();

        using var extractDest = new TempDirectory();
        ArchiveResult extractResult = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [result.CreatedFiles[0]],
            DestinationFolder = extractDest.Path,
            Mode = ExtractMode.SingleFolder
        });

        extractResult.Success.Should().BeTrue();
        // T-F156: "notes" and "notes (1)" are two distinct roots, but SingleFolder mode no longer
        // wraps a multi-root archive in a subfolder named after the archive (T-14/T-F118 used to);
        // see DECISIONS.md's T-F156 entry.
        File.ReadAllText(Path.Combine(extractDest.Path, "notes", "file.txt")).Should().Be("from A");
        File.ReadAllText(Path.Combine(extractDest.Path, "notes (1)", "file.txt")).Should().Be("from B");
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

        ArchiveResult result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(fileCount);

        for (int i = 1; i <= fileCount; i++)
        {
            string expectedZip = Path.Combine(_temp.Path, $"item{i}.zip");
            result.CreatedFiles.Should().Contain(expectedZip);
            using ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(expectedZip);
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

        ArchiveResult result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(2);

        var contents = result.CreatedFiles.Select(zipPath =>
        {
            using ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
            ZipArchiveEntry entry = zip.Entries.Single(e => e.FullName.EndsWith("pic.txt", StringComparison.Ordinal));
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }).ToList();

        contents.Should().BeEquivalentTo(["from A", "from B"]);
    }

    // T-F158: closes a real coverage gap found while unifying the conflict decision into
    // DestinationConflictResolver — no prior test exercised Overwrite specifically against a
    // same-run collision (only the on-disk case, ArchiveAsync_ConflictOverwrite_ReplacesExistingZip,
    // and the Rename sibling above, which reaches the same outcome via a different arm). Under a
    // same-run collision there may be nothing on disk yet to overwrite (another in-flight worker
    // owns creating it) — Overwrite renames instead of deleting, same as Rename would.
    [Fact]
    public async Task ArchiveAsync_SeparateArchivesMode_TwoSourcesShareBasename_OverwriteRenamesInsteadOfDeleting()
    {
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
            OnConflict = ConflictBehavior.Overwrite
        };

        ArchiveResult result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(2);

        var contents = result.CreatedFiles.Select(zipPath =>
        {
            using ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
            ZipArchiveEntry entry = zip.Entries.Single(e => e.FullName.EndsWith("pic.txt", StringComparison.Ordinal));
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

        ArchiveResult result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().HaveCount(12);
        result.CreatedFiles.Should().OnlyHaveUniqueItems();
    }
}
