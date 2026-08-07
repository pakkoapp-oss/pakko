using System.Diagnostics;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services.Sandbox;

namespace Archiver.Core.Services;

/// <summary>
/// Extracts tar-family archives (tar, tar.gz, tar.bz2, tar.xz, tar.zst, tar.lzma, 7z, rar) via
/// the system's tar.exe, launched inside a Windows AppContainer (no network capability) with a
/// Job Object (ActiveProcessLimit = 1, RAM/CPU limits) — see TASKS.md's T-F52 entry for the full
/// design and DECISIONS.md for the empirical trail. Never throws to callers — all errors are
/// captured in ArchiveResult.Errors. Replaces the deleted TarProcessService.
/// </summary>
public sealed class TarSandboxedService : ITarService
{
    private const string TarExecutablePath = @"C:\Windows\System32\tar.exe"; // NOSONAR: S1075 — CLAUDE.md's Hard Constraints mandate this exact absolute path, never PATH-resolved (PATH-hijack resistance); moving it to config would reopen that risk

    // DetectCapabilitiesAsync runs synchronously on app startup (App.xaml.cs forces eager
    // resolution) — a hung tar.exe --version must not hang app launch indefinitely.
    private static readonly TimeSpan DetectionTimeout = TimeSpan.FromSeconds(5);

    private readonly GroupPolicyOptions _policy;

    // T-F51: optional so every existing `new TarSandboxedService()` call site keeps compiling —
    // a null policy means "everything allowed", matching today's shipped behavior exactly.
    public TarSandboxedService(GroupPolicyOptions? policy = null)
    {
        _policy = policy ?? new GroupPolicyOptions();
    }

    /// <inheritdoc/>
    public async Task<TarCapabilities> DetectCapabilitiesAsync()
    {
        try
        {
            // Deliberately unsandboxed (no AppContainer/Job Object) — this is a one-shot,
            // eagerly-resolved startup probe, not an untrusted-archive operation. Still gated on
            // the signature check: a tampered tar.exe should fail closed here via the same
            // all-false-defaults path used for "tar.exe absent", not silently run --version.
            if (!TarSignatureVerifier.Verify(TarExecutablePath))
                return new TarCapabilities();

            var startInfo = new ProcessStartInfo
            {
                FileName = TarExecutablePath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using Process? process = Process.Start(startInfo);
            if (process is null)
                return new TarCapabilities();

            using var timeoutCts = new CancellationTokenSource(DetectionTimeout);

            try
            {
                string output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

                return TarVersionParser.Parse(output);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return new TarCapabilities();
            }
        }
        catch (Exception)
        {
            return new TarCapabilities();
        }
    }

    /// <inheritdoc/>
    public async Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ArchiveError>();
        var createdFiles = new List<string>();
        var skippedFiles = new List<SkippedFile>();
        // T-F06: one instance for the whole call — see ZipArchiveService.ExtractAsync's identical
        // comment. Independent of ZipArchiveService's own resolver instance: Zip and tar-family
        // archives are handled by two separate ExtractionRouter calls, so "apply to all" does not
        // cross a mixed zip+tar-family selection (an accepted, documented scope cut).
        var conflictResolver = new ConflictResolver(options.OnConflict, options.ResolveConflictAsync);

        Directory.CreateDirectory(options.DestinationFolder);

        int total = options.ArchivePaths.Count;
        var sink = new ArchiveResultSink(errors, createdFiles, skippedFiles);

        for (int i = 0; i < total; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            bool wasCancelled = await ExtractArchiveAtIndexAsync(
                options, i, total, conflictResolver, sink, progress, cancellationToken).ConfigureAwait(false);
            if (wasCancelled)
                break;
        }

        var result = new ArchiveResult
        {
            Success = errors.Count == 0,
            CreatedFiles = createdFiles,
            Errors = errors,
            SkippedFiles = skippedFiles,
        };

        if (result.Success && options.OpenDestinationFolder)
        {
            ExplorerLauncher.OpenFolder(options.DestinationFolder);
        }

        return result;
    }

    private static bool IsKnownEncryptedRar(string archivePath) =>
        ArchiveFormatDetector.Detect(archivePath) == ArchiveFormat.Rar
            && ArchiveFormatDetector.IsEncryptedRar(archivePath);

    // One iteration of ExtractAsync's per-archive loop — moved out so the loop itself reads as
    // "check cancellation, extract one, check cancellation" at a glance. Returns true exactly
    // when extraction observed real cancellation, in which case the caller must break WITHOUT
    // reporting progress for this iteration (matches the original inline loop's `break` landing
    // before the progress-report line); every other outcome falls through to the report and
    // returns false so the caller's loop continues.
    private async Task<bool> ExtractArchiveAtIndexAsync(
        ExtractOptions options, int i, int total, ConflictResolver conflictResolver,
        ArchiveResultSink sink, IProgress<ProgressReport>? progress, CancellationToken cancellationToken)
    {
        // T-F142: real BytesTransferred/TotalBytes/CurrentFile only make sense for one archive at
        // a time (see ExtractSingleArchiveAsync's own polling-based reporting) — matches
        // ZipArchiveService.ExtractAsync's identical singleArchive convention for a multi-archive
        // selection, where per-archive percent-only progress (bytes = 0,0) is the existing,
        // already-accepted shape.
        bool singleArchive = total == 1;
        string archivePath = options.ArchivePaths[i];
        string destDir = options.Mode == ExtractMode.SeparateFolders
            ? Path.Combine(options.DestinationFolder,
                options.SeparateFolderName ?? ArchiveNaming.GetBaseName(archivePath))
            : options.DestinationFolder;

        // T-F113: cheap proactive check, no sandbox/tar.exe launch needed for a known-encrypted
        // RAR — mirrors ZipArchiveService.ExtractAsync's IsEncryptedZip placement. 7z and RAR's
        // rarer header-encrypted case aren't cheaply detectable this way (see ArchiveFormatDetector.
        // IsEncryptedRar's doc comment) — those are instead caught reactively below via
        // IsLikelyEncryptionFailure once tar.exe actually fails.
        if (IsKnownEncryptedRar(archivePath))
        {
            sink.Errors.Add(new ArchiveError
            {
                SourcePath = archivePath,
                Message = "This archive is password-protected and cannot be extracted."
            });
        }
        else
        {
            IProgress<ProgressReport>? archiveProgress = singleArchive ? progress : null;
            bool wasCancelled = await ExtractOneArchiveAsync(
                archivePath, destDir, options, conflictResolver, sink, archiveProgress, cancellationToken).ConfigureAwait(false);
            if (wasCancelled)
                return true;
        }

        if (!singleArchive)
            progress?.Report(new ProgressReport { Percent = (i + 1) * 100 / total, BytesTransferred = 0, TotalBytes = 0 });

        return false;
    }

    // The three List sinks ExtractOneArchiveAsync/ProcessSeparateArchivesAsync write into —
    // bundled to cut S107's parameter count, same reasoning as ZipArchiveService's own
    // (ConcurrentBag-typed, for its parallel workers) ArchiveResultSink; this one stays List-typed
    // since neither of this file's two call sites is itself parallelized across archives.
    private sealed record ArchiveResultSink(
        List<ArchiveError> Errors,
        List<string> CreatedFiles,
        List<SkippedFile> SkippedFiles);

    // The try/6-catch error-mapping body of ExtractAsync's per-archive loop, pulled out so the
    // loop itself reads as "known-encrypted-RAR short-circuit, else extract-and-map-errors" at a
    // glance. Returns true when extraction observed real cancellation (the loop breaks in that
    // case, matching ExtractAsync's original inline `break`); every other outcome is recorded
    // into errors/createdFiles and returns false so the loop continues to the next archive.
    private async Task<bool> ExtractOneArchiveAsync(
        string archivePath, string destDir, ExtractOptions options, ConflictResolver conflictResolver,
        ArchiveResultSink sink, IProgress<ProgressReport>? archiveProgress, CancellationToken cancellationToken)
    {
        try
        {
            bool alreadyIsolated = options.Mode == ExtractMode.SeparateFolders;
            var context = new TarExtractionContext(
                conflictResolver, sink.SkippedFiles, options.ConfirmCompressionBombExtraction, _policy.MotwMode, archiveProgress);
            var (actualDest, anyExtracted) = await ExtractSingleArchiveAsync(
                archivePath, destDir, alreadyIsolated, options.SelectedEntryPaths, context, cancellationToken)
                .ConfigureAwait(false);

            // T-F87: an archive whose entries were all individually skipped (e.g. every entry
            // already exists at the destination with OnConflict=Skip) must not be reported as
            // CreatedFiles — MainViewModel uses this list to decide whether DeleteAfterOperation
            // may delete the source archive.
            if (anyExtracted)
                sink.CreatedFiles.Add(actualDest);
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (TarArchiveRejectedException ex)
        {
            sink.Errors.Add(new ArchiveError { SourcePath = archivePath, Message = ex.Message });
            return false;
        }
        catch (TarSignatureVerificationException ex)
        {
            sink.Errors.Add(new ArchiveError { SourcePath = archivePath, Message = ex.Message });
            return false;
        }
        catch (SandboxSetupException ex)
        {
            sink.Errors.Add(new ArchiveError { SourcePath = archivePath, Message = ex.Message, Exception = ex });
            return false;
        }
        catch (IOException ex)
        {
            // T-F113: covers 7z (both encryption modes) and RAR's header-encrypted case — the
            // proactive check above only catches RAR's more common data-only case before staging
            // even begins.
            sink.Errors.Add(new ArchiveError
            {
                SourcePath = archivePath,
                Message = IsLikelyEncryptionFailure(ex.Message)
                    ? "This archive is password-protected and cannot be extracted."
                    : $"Cannot extract archive: {ex.Message}",
                Exception = ex
            });
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            sink.Errors.Add(new ArchiveError
            {
                SourcePath = archivePath,
                Message = $"Access denied extracting archive: {ex.Message}",
                Exception = ex
            });
            return false;
        }
    }

    // T-F49: whole-archive pre-scan (name + type) runs before any -xf call. tar.exe does not
    // abort extraction on a single bad entry (confirmed: it logs an error and keeps writing the
    // rest of the archive, only returning a delayed nonzero exit code) and a symlink entry can
    // be created and then written through to escape the quarantine directory entirely before any
    // C# code gets a chance to inspect the result — see DECISIONS.md's T-F49 entry for the
    // reproduced exploit. Post-hoc validation of quarantine contents therefore cannot be the
    // primary defense; rejecting the whole archive before -xf runs is.
    //
    // T-F52: the scope (profile + ACLs + staging + Job Object + signature check) is created
    // FIRST, before the compression-bomb decision — unlike the pre-sandbox design, the pre-scan
    // itself must now run inside the sandbox too, which needs a staged copy of the archive to
    // already exist in quarantine\in\. This means a declined/blocked bomb no longer leaves
    // "nothing to clean up" the way it used to — the `using` on scope below disposes the
    // quarantine directory on every exit path, early or not.
    // Already split via TarExtractionContext/TryMoveSingleEntryAsync (T-F147); the residual
    // complexity below is the whole-archive pre-scan/smart-foldering decision/compression-bomb
    // gate, all order-sensitive and security-relevant (T-F49/T-F52/T-F94 history) — further
    // splitting risks separating checks whose safety currently reads directly off this one method
    // body. Kept paired 1:1 with ZipArchiveService.ExtractWithSmartFolderingAsync (T-F118).
    private static async Task<(string ActualDest, bool AnyExtracted)> ExtractSingleArchiveAsync( // NOSONAR: S3776 — see comment above
        string archivePath,
        string destDir,
        bool alreadyIsolated,
        IReadOnlyList<string>? selectedEntryPaths,
        TarExtractionContext context,
        CancellationToken cancellationToken)
    {
        List<SkippedFile> skippedFiles = context.SkippedFiles;
        Func<CompressionBombWarning, Task<bool>>? confirmCompressionBombExtraction = context.ConfirmCompressionBombExtraction;
        IProgress<ProgressReport>? progress = context.Progress;

        using TarSandboxScope scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: true, cancellationToken)
            .ConfigureAwait(false);

        // T-F05: the whole-archive pre-scan below runs unconditionally, exactly as it did before
        // SelectedEntryPaths existed — it must NEVER be skipped or narrowed just because only a
        // subset will be extracted (see T-F49's exploit finding in DECISIONS.md: a symlink entry
        // can escape quarantine before any per-entry check runs, so the whole archive must be
        // validated regardless of what subset the caller eventually asks tar.exe to extract).
        var (declaredUncompressedSize, allNames, sizeByName) = await ScanForUnsafeEntriesAsync(scope, cancellationToken)
            .ConfigureAwait(false);

        // T-F118: mirrors ZipArchiveService.ExtractWithSmartFolderingAsync's identical algorithm
        // exactly — allNames already carries tar's own trailing '/' convention for directory
        // entries (see ScanForUnsafeEntriesAsync's comment), so "file entries" can be derived the
        // same way ZIP derives them from ZipArchiveEntry.FullName, with no second tar.exe call.
        // A selected subset (T-F05/T-F98 drill-down) has no single meaningful "root" to collapse,
        // same reasoning as ZIP's isSelectedSubset — always extract straight into destDir.
        bool isSelectedSubset = selectedEntryPaths is { Count: > 0 };
        var fileNames = allNames.Where(n => !n.EndsWith('/')).ToList();

        // T-F142: the exact set of names that will actually be passed to "-xf" (computed once
        // here, reused below both for the tar.exe argument list and for the progress byte total)
        // — a subset selection must progress-report against its OWN byte total, not
        // declaredUncompressedSize (deliberately whole-archive, for the T-F94 bomb check below).
        // Using the whole-archive total for a subset extraction would stall the poll well short
        // of 94% and claim bytes in the terminal report that were never written.
        List<string>? expandedSelection = isSelectedSubset ? ExpandSelection(allNames, selectedEntryPaths!) : null;
        long progressTotalBytes = expandedSelection != null
            ? expandedSelection.Sum(n => sizeByName.GetValueOrDefault(n, 0L))
            : declaredUncompressedSize;

        bool isSingleRootFolder = !isSelectedSubset
            && fileNames.Count > 0
            && fileNames.All(n => n.Contains('/'))
            && fileNames
                .Select(n => n[..n.IndexOf('/')])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 1;

        bool isSingleRootFile = !isSelectedSubset && fileNames.Count == 1 && !fileNames[0].Contains('/');

        string actualDest = (isSingleRootFolder || isSingleRootFile || alreadyIsolated || isSelectedSubset)
            ? destDir
            : Path.Combine(destDir, ArchiveNaming.GetBaseName(archivePath));

        // T-F94: whole-archive compression-ratio decision. compressedFileSize reads the
        // ORIGINAL archivePath (not the staged copy — same size either way, hardlink or copy,
        // but this is the path the caller/UI actually knows about for any error messages).
        long compressedFileSize = new FileInfo(archivePath).Length;
        var bombOutcome = await ArchiveEntrySecurity.EvaluateCompressionBombAsync(
            archivePath, declaredUncompressedSize, compressedFileSize,
            ArchiveEntrySecurity.GetAvailableFreeSpace(destDir),
            confirmCompressionBombExtraction).ConfigureAwait(false);

        if (bombOutcome == CompressionBombOutcome.InsufficientDiskSpace)
        {
            skippedFiles.Add(new SkippedFile
            {
                Path = archivePath,
                Reason = $"Archive declares {declaredUncompressedSize:N0} bytes uncompressed, " +
                         $"but the destination only has {ArchiveEntrySecurity.GetAvailableFreeSpace(destDir):N0} bytes free. " +
                         "Extraction was blocked."
            });
            return (destDir, false);
        }

        if (bombOutcome == CompressionBombOutcome.UserDeclined)
        {
            long ratio = compressedFileSize > 0 ? declaredUncompressedSize / compressedFileSize : 0;
            skippedFiles.Add(new SkippedFile
            {
                Path = archivePath,
                Reason = $"Suspicious compression ratio ({ratio}:1, {declaredUncompressedSize:N0} bytes declared) " +
                         "across the whole archive. Extraction was declined as a precaution against decompression bombs."
            });
            return (destDir, false);
        }

        // T-F52: pre-create every directory the archive implies, at Pakko's own (unsandboxed)
        // identity, before tar.exe ever runs inside the AppContainer. Found empirically: when a
        // nested file entry (e.g. "sub/b.txt") has no preceding explicit "sub/" directory entry,
        // libarchive's own implicit parent-directory creation fails under the AppContainer even
        // though "out\" itself is correctly ACL'd with inheritable Modify — an explicit "sub/"
        // entry extracts fine, so this is specific to libarchive's own implicit-mkdir path, not a
        // general ACL problem (isolated via a throwaway diagnostic against the real tar.exe; see
        // DECISIONS.md's T-F52 entry). Pre-creating here sidesteps it entirely: Directory.
        // CreateDirectory, run by Pakko's own trusted process, correctly inherits "out\"'s ACEs
        // for every directory it creates, so tar.exe's own directory ever needs to create one.
        foreach (string name in allNames)
        {
            string? relativeDir = name.EndsWith('/') ? name.TrimEnd('/') : Path.GetDirectoryName(name);
            if (!string.IsNullOrEmpty(relativeDir))
                Directory.CreateDirectory(Path.Combine(scope.OutputDirectory!, relativeDir));
        }

        var tarArgs = new List<string> { "-xf", scope.StagedArchivePath, "-C", scope.OutputDirectory! };
        if (expandedSelection != null)
            tarArgs.AddRange(expandedSelection);

        // T-F142: real byte-level progress for a single-archive extraction (progress is only
        // non-null in that case — see ExtractAsync's singleArchive gate). tar.exe runs sandboxed
        // here (unlike CompressAsync's unsandboxed launch), so there is no per-entry stderr line
        // to hook the way T-F140 did for archiving — SandboxedProcessLauncher only returns
        // buffered output once the whole process exits, and adding a streaming channel out of the
        // AppContainer would mean touching security-critical sandbox code for a progress readout.
        // Instead, Pakko's own (unsandboxed) process polls how many bytes tar.exe has actually
        // written into the quarantine "out\" directory while extraction runs — real bytes landing
        // on disk, not an approximation. progressTotalBytes (computed above) is already the right
        // total for whatever is actually being extracted (whole archive or a selected subset), so
        // no second tar.exe listing pass is needed.
        Task<(int ExitCode, string StdOut, string StdErr)> extractionTask = scope.RunAsync(tarArgs, cancellationToken);

        if (progress != null && progressTotalBytes > 0)
        {
            await PollExtractionProgressAsync(
                scope.OutputDirectory!, progressTotalBytes, progress, extractionTask, cancellationToken)
                .ConfigureAwait(false);
        }

        var (exitCode, _, stdErr) = await extractionTask.ConfigureAwait(false);

        if (exitCode != 0)
            throw new IOException($"tar.exe extraction failed: {stdErr.Trim()}");

        Directory.CreateDirectory(actualDest);

        int totalFiles = 0;
        int extractedCount = 0;
        // The move phase (quarantine "out\" -> the real destination) is not free — a cross-volume
        // move is a real copy, not a rename — so it gets its own slice of the percentage (95-99)
        // rather than leaving the dialog sitting at whatever the extraction-phase poll last saw.
        // Uses the same expandedSelection-vs-whole-archive distinction as progressTotalBytes above
        // — a subset selection only ever moves its own subset of files, not fileNames.Count.
        int totalFileEntries = expandedSelection != null
            ? expandedSelection.Count(n => !n.EndsWith('/'))
            : fileNames.Count;
        var moveReportStopwatch = System.Diagnostics.Stopwatch.StartNew();
        long lastMoveReportMs = -MoveReportThrottleMs;

        foreach (string file in EnumerateFilesGuarded(scope.OutputDirectory!))
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalFiles++;

            var (extracted, relativePath) = await TryMoveSingleEntryAsync(
                file, scope.OutputDirectory!, isSingleRootFolder, actualDest, archivePath, context).ConfigureAwait(false);
            if (!extracted)
                continue;

            extractedCount++;

            if (progress != null && totalFileEntries > 0 &&
                (moveReportStopwatch.ElapsedMilliseconds - lastMoveReportMs >= MoveReportThrottleMs
                    || extractedCount == totalFileEntries))
            {
                lastMoveReportMs = moveReportStopwatch.ElapsedMilliseconds;
                int movePercent = 95 + (int)(extractedCount * 4L / totalFileEntries);
                progress.Report(new ProgressReport
                {
                    Percent = Math.Min(99, movePercent),
                    BytesTransferred = progressTotalBytes,
                    TotalBytes = progressTotalBytes,
                    CurrentFile = relativePath,
                });
            }
        }

        // T-F87: every extracted file was individually skipped (already existed at the
        // destination) — nothing was actually written, so the caller must not count this
        // archive as CreatedFiles (that list gates whether DeleteAfterOperation may delete
        // the source archive).
        if (totalFiles > 0 && extractedCount == 0)
        {
            skippedFiles.Add(new SkippedFile
            {
                Path = archivePath,
                Reason = "No entries were extracted from this archive — every entry was skipped."
            });
            progress?.Report(new ProgressReport { Percent = 100, BytesTransferred = progressTotalBytes, TotalBytes = progressTotalBytes });
            return (actualDest, false);
        }

        progress?.Report(new ProgressReport { Percent = 100, BytesTransferred = progressTotalBytes, TotalBytes = progressTotalBytes });
        return (actualDest, true);
    }

    // The plumbing every call to ExtractSingleArchiveAsync shares, cut from that method's own
    // parameter list to fix S107 — same field names as ZipArchiveService.ZipExtractionContext
    // (T-F118: the two methods are deliberately kept algorithmically identical), though it's its
    // own record type since the two services share no common base to hang a single shared type on.
    private sealed record TarExtractionContext(
        ConflictResolver ConflictResolver,
        List<SkippedFile> SkippedFiles,
        Func<CompressionBombWarning, Task<bool>>? ConfirmCompressionBombExtraction,
        MotwMode MotwMode,
        IProgress<ProgressReport>? Progress);

    // One file of ExtractSingleArchiveAsync's move-phase loop (quarantine "out\" -> the real
    // destination) — conflict-resolve, move, propagate MOTW. Returns whether the file was
    // actually moved (false for both the already-exists+Skip case and the defensive-only
    // isSingleRootFolder edge case, matching the original inline loop's two `continue` sites) and
    // the relative path actually used, for the caller's own progress-report CurrentFile.
    private static async Task<(bool Extracted, string? RelativePath)> TryMoveSingleEntryAsync(
        string file, string outputDirectory, bool isSingleRootFolder, string actualDest, string archivePath,
        TarExtractionContext context)
    {
        string relativePath = Path.GetRelativePath(outputDirectory, file);

        // T-F118: matches ZipArchiveService.ExtractWithSmartFolderingAsync's identical strip —
        // when the whole archive collapses to one root folder, actualDest already stands in for
        // that folder, so its own name is dropped from the path being written.
        if (isSingleRootFolder)
        {
            int sep = relativePath.IndexOf(Path.DirectorySeparatorChar);
            if (sep < 0)
            {
                // Defensive only — every file walked here came from a fileNames entry that was
                // confirmed to contain '/' for isSingleRootFolder to be true at all.
                return (false, null);
            }
            relativePath = relativePath[(sep + 1)..];
        }

        string finalFilePath = Path.GetFullPath(Path.Combine(actualDest, relativePath));

        if (File.Exists(finalFilePath))
        {
            ConflictBehavior resolvedConflict = await context.ConflictResolver.ResolveAsync(finalFilePath).ConfigureAwait(false);
            if (resolvedConflict == ConflictBehavior.Skip)
            {
                context.SkippedFiles.Add(new SkippedFile { Path = relativePath, Reason = "File already exists at destination." });
                return (false, relativePath);
            }
            if (resolvedConflict == ConflictBehavior.Rename)
            {
                finalFilePath = GetUniqueFilePath(finalFilePath);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(finalFilePath)!);
        File.Move(file, finalFilePath, overwrite: true);

        // T-F45: propagate Zone.Identifier ADS from the ORIGINAL archive (never the staged
        // quarantine copy) to the extracted file — the staged copy is a Pakko-internal
        // implementation detail and may not even carry a Zone.Identifier depending on
        // hardlink-vs-copy staging; MOTW must reflect the real source the user chose.
        ArchiveEntrySecurity.TryPropagateMotw(archivePath, finalFilePath, context.MotwMode);

        return (true, relativePath);
    }

    // T-F142: how often the move-phase loop is allowed to report progress — same ~100ms throttle
    // convention T-F140's ProgressTracker already uses for the (much higher-frequency) ZIP
    // parallel-pipeline case. This loop is single-threaded/sequential (one archive extracted at a
    // time, never concurrent workers), so a plain Stopwatch is enough — no Interlocked/thread-
    // safety needed here, unlike ProgressTracker.
    private const long MoveReportThrottleMs = 100;

    // T-F142: polls how many bytes tar.exe has written into the quarantine output directory while
    // extractionTask runs, reporting real (not approximated) BytesTransferred against the known
    // declaredUncompressedSize total. Reserves 94% as the ceiling for this phase — the remaining
    // 95-99% belongs to the move-to-destination phase that runs after tar.exe exits (see the
    // caller). Percent/BytesTransferred are clamped monotonic (never allowed to regress) since a
    // directory enumeration racing tar.exe's own writes can observe a transient dip (a file
    // renamed/replaced mid-write) — a real dip reported to the UI would look like a bug.
    //
    // The wait between polls is adaptive, not a fixed 250ms: a real measurement against a 20,000-
    // file tree found ComputeDirectoryByteSize's own full recursive walk+stat already costs
    // ~450-550ms on ordinary local storage — a fixed short interval would make this loop spend most
    // of its time re-walking the tree instead of waiting, competing with tar.exe's own I/O for no
    // real reporting benefit (the UI can't usefully show updates faster than a poll can produce
    // them anyway). Backing the next wait off to twice the last poll's own cost keeps the walk
    // itself bounded to roughly a third of this loop's time regardless of tree size, while still
    // polling every ~250ms for the common case of an archive small/fast enough that the walk itself
    // is cheap.
    // internal (not private) so a test can drive the polling loop directly against a real temp
    // directory with a controllable "extraction" Task, without needing a real tar.exe run (T-F143).
    internal static async Task PollExtractionProgressAsync(
        string outputDirectory, long totalBytes, IProgress<ProgressReport> progress,
        Task extractionTask, CancellationToken cancellationToken)
    {
        const int MinPollIntervalMs = 250;
        const int ExtractionPhaseMaxPercent = 94;
        long lastReportedBytes = 0;
        int nextWaitMs = MinPollIntervalMs;

        while (!extractionTask.IsCompleted)
        {
            await Task.WhenAny(extractionTask, Task.Delay(nextWaitMs, cancellationToken)).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
                break;
            if (extractionTask.IsCompleted)
                break;

            var pollStopwatch = System.Diagnostics.Stopwatch.StartNew();
            (long observedBytes, string? currentFile) = ComputeDirectoryStateSnapshot(outputDirectory);
            nextWaitMs = Math.Max(MinPollIntervalMs, (int)Math.Min(int.MaxValue, pollStopwatch.ElapsedMilliseconds * 2));

            long clampedBytes = Math.Max(lastReportedBytes, Math.Min(observedBytes, totalBytes));
            lastReportedBytes = clampedBytes;

            int percent = (int)Math.Min(ExtractionPhaseMaxPercent, clampedBytes * 100L / totalBytes);
            progress.Report(new ProgressReport
            {
                Percent = percent,
                BytesTransferred = clampedBytes,
                TotalBytes = totalBytes,
                CurrentFile = currentFile,
            });
        }
    }

    // Best-effort: the directory tree is actively being written to by a concurrently-running
    // tar.exe process, so a file can appear, grow, or vanish between being listed and being
    // stat'd — any such race is tolerated as a slightly stale progress reading, never a thrown
    // exception (this is a progress estimate, not a correctness-critical read).
    //
    // T-F142: also returns the most-recently-written file's relative path, as an approximate
    // "currently extracting" name — there is no real per-entry signal available during this phase
    // (see PollExtractionProgressAsync's own remarks on why), but tar.exe writes files roughly in
    // archive order and the one most recently modified is very likely the one it's actively
    // writing right now. Without this, Archiver.Shell's dialog would show a blank filename line for
    // the whole extraction phase (only the move phase afterward sets CurrentFile) — the same
    // missing-filename complaint T-F140 already fixed once, just arriving from a different code
    // path. FileInfo.LastWriteTimeUtc costs nothing extra here — it's read from the same stat
    // FileInfo.Length already performs, not a second syscall.
    // internal (not private) — same T-F143 rationale as PollExtractionProgressAsync above.
    internal static (long TotalBytes, string? MostRecentFileRelativePath) ComputeDirectoryStateSnapshot(string directory)
    {
        long total = 0;
        string? mostRecentRelativePath = null;
        DateTime mostRecentWriteTimeUtc = DateTime.MinValue;
        try
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var info = new FileInfo(file);
                    total += info.Length;
                    if (info.LastWriteTimeUtc > mostRecentWriteTimeUtc)
                    {
                        mostRecentWriteTimeUtc = info.LastWriteTimeUtc;
                        mostRecentRelativePath = Path.GetRelativePath(directory, file);
                    }
                }
                catch (IOException) { /* file mid-write or vanished since being listed — best-effort estimate */ }
                catch (UnauthorizedAccessException) { /* same */ }
            }
        }
        catch (IOException) { /* directory tree changing under us — best-effort estimate */ }
        catch (UnauthorizedAccessException) { /* same */ }
        return (total, mostRecentRelativePath);
    }

    // Rejects the whole archive (throws TarArchiveRejectedException) if any entry name is
    // unsafe, or if any entry is a symlink/hardlink/device/fifo/socket. Two tar.exe invocations,
    // both run through the sandboxed scope: "-tf" lists plain entry names (one per line, no
    // locale-dependent formatting — used for the name checks) and "-tvf" lists the same entries
    // with a leading ls-style type character ('-' regular, 'd' directory, 'l' symlink, 'h'
    // hardlink, etc.) that is rendered deterministically by libarchive regardless of locale —
    // unlike the rest of that line (its date column was observed locale-mangled on a
    // Cyrillic-locale machine, same bug class as T-F84) — so only character 0 of each "-tvf" line
    // is read.
    // Returns the sum of declared uncompressed sizes for every regular-file entry (T-F94) — the
    // ratio-threshold decision itself lives in ExtractSingleArchiveAsync via the shared
    // ArchiveEntrySecurity.EvaluateCompressionBombAsync evaluator, but the size sum is still
    // accumulated here, in the same single "-tvf" pass that already reads the type column, to
    // avoid a second tar.exe invocation just to re-derive it (matches T-F90's original rationale
    // for extending this one pass in the first place).
    // T-F146: internal (was private) so AntivirusScanService can reuse the exact same T-F49
    // pre-scan for its own tar-family quarantine-extraction flow, without a second implementation
    // that could silently drift from what real extraction actually rejects.
    internal static async Task<(long TotalDeclaredSize, string[] Names, Dictionary<string, long> SizeByName)> ScanForUnsafeEntriesAsync(
        TarSandboxScope scope, CancellationToken cancellationToken)
    {
        var (nameExitCode, nameStdOut, nameStdErr) = await scope.RunAsync(
            ["-tf", scope.StagedArchivePath], cancellationToken).ConfigureAwait(false);
        if (nameExitCode != 0)
            throw new IOException($"Cannot read archive: {nameStdErr.Trim()}");

        string[] names = SplitLines(nameStdOut);

        string? unsafeName = names.FirstOrDefault(IsDangerousEntryName);
        if (unsafeName != null)
            throw new TarArchiveRejectedException(
                $"Archive contains an unsafe entry path ('{unsafeName}') and cannot be safely extracted.");

        var (typeExitCode, typeStdOut, typeStdErr) = await scope.RunAsync(
            ["-tvf", scope.StagedArchivePath], cancellationToken).ConfigureAwait(false);
        if (typeExitCode != 0)
            throw new IOException($"Cannot read archive: {typeStdErr.Trim()}");

        string[] typeLines = SplitLines(typeStdOut);
        if (typeLines.Length != names.Length)
            throw new TarArchiveRejectedException(
                "Archive listing is inconsistent and cannot be safely extracted.");

        // T-F90: column 4 (size) is accumulated alongside the existing column-0 (type) check in
        // the same pass — see DECISIONS.md's T-F90 entry for why the size column, unlike the
        // date column, is safe to parse regardless of locale.
        long totalDeclaredSize = 0;
        // T-F142: per-entry sizes, keyed by the same raw name form as `names` — needed so a
        // selected-subset extraction (Archive Browser -> Extract Selected) can compute its own
        // subset byte total for progress reporting, distinct from totalDeclaredSize (deliberately
        // whole-archive, for the T-F94 compression-bomb check — see that check's own comment).
        // Retained here rather than re-parsed with a second "-tvf" pass, since this loop already
        // reads every line's size column once.
        var sizeByName = new Dictionary<string, long>(names.Length, StringComparer.Ordinal);

        for (int i = 0; i < typeLines.Length; i++)
        {
            string line = typeLines[i];
            char typeChar = line.Length > 0 ? line[0] : '?';
            if (typeChar != '-' && typeChar != 'd')
                throw new TarArchiveRejectedException(
                    "Archive contains a symlink, hardlink, device, or other special entry and cannot be safely extracted.");

            if (typeChar == '-')
            {
                long size = ParseTarListingSize(line);
                totalDeclaredSize += size;
                sizeByName[names[i]] = size;
            }
        }

        // T-F05: the raw names (with tar's own trailing '/' on directory entries preserved) are
        // returned so ExtractSingleArchiveAsync's selected-subset extraction can build a "-xf"
        // member argument list without a second "-tf" invocation, and so the exact path form
        // tar.exe itself uses is what's ever passed back to it (see DECISIONS.md's T-F05 spike
        // entry — an unmatched/mismatched member name makes the whole "-xf" call fail non-zero).
        return (totalDeclaredSize, names, sizeByName);
    }

    // T-F05: expands a UI-selected set of archive-internal paths (ArchiveEntryInfo.Path's
    // convention — no trailing slash, even for folders) into the exact literal member names
    // tar.exe's "-tf" reported, for a "-xf archive member..." selective-extraction call. A
    // selected folder path is expanded to every one of its descendants explicitly, rather than
    // relying on tar.exe auto-recursing a bare directory-member argument — confirmed empirically
    // (DECISIONS.md's T-F05 entry) that tar.exe does auto-recurse, but this method doesn't depend
    // on that behavior continuing to hold.
    // T-F146: internal (was private) — AntivirusScanService's own selected-subset scan reuses this
    // exact expansion rather than re-deriving it.
    internal static List<string> ExpandSelection(string[] allNames, IReadOnlyList<string> selectedEntryPaths)
    {
        var allNamesSet = new HashSet<string>(allNames, StringComparer.Ordinal);
        var result = new List<string>();

        foreach (string selected in selectedEntryPaths)
        {
            if (allNamesSet.Contains(selected))
                result.Add(selected);
            else if (allNamesSet.Contains(selected + "/"))
                result.Add(selected + "/");

            string descendantPrefix = selected + "/";
            result.AddRange(allNames.Where(name => name.StartsWith(descendantPrefix, StringComparison.Ordinal)));
        }

        return result.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc/>
    public async Task<ArchiveListResult> ListEntriesAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        // T-F113: cheap proactive check for the header-encrypted case only (unlike ExtractAsync's
        // IsEncryptedRar check) — a data-only-encrypted RAR's filenames are still readable, so
        // listing should still succeed there, matching ZipArchiveService.ListEntriesAsync's and
        // 7z's own parity (only extraction refuses for data-only encryption, not browsing).
        if (IsHeaderEncryptedRar(archivePath))
        {
            return new ArchiveListResult
            {
                Success = false,
                ErrorMessage = "This archive is password-protected and cannot be browsed."
            };
        }

        try
        {
            using TarSandboxScope scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: false, cancellationToken)
                .ConfigureAwait(false);

            var (names, nameError) = await RunListingCommandAsync(scope, "-tf", cancellationToken).ConfigureAwait(false);
            if (nameError is not null)
                return nameError;

            var (typeLines, typeError) = await RunListingCommandAsync(scope, "-tvf", cancellationToken).ConfigureAwait(false);
            if (typeError is not null)
                return typeError;

            if (typeLines.Length != names.Length)
                return new ArchiveListResult { Success = false, ErrorMessage = "Archive listing is inconsistent." };

            return new ArchiveListResult { Success = true, Entries = BuildEntryList(names, typeLines) };
        }
        catch (TarSignatureVerificationException ex)
        {
            return new ArchiveListResult { Success = false, ErrorMessage = ex.Message };
        }
        catch (SandboxSetupException ex)
        {
            return new ArchiveListResult { Success = false, ErrorMessage = ex.Message };
        }
        catch (IOException ex)
        {
            return new ArchiveListResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private static bool IsHeaderEncryptedRar(string archivePath) =>
        ArchiveFormatDetector.Detect(archivePath) == ArchiveFormat.Rar
            && ArchiveFormatDetector.IsRarHeaderEncrypted(archivePath);

    // Runs one of ListEntriesAsync's two tar.exe listing calls ("-tf" for names, "-tvf" for
    // type/size lines) and maps a nonzero exit code to an ArchiveListResult the same way both
    // calls already did identically. Error is non-null exactly when the call failed.
    private static async Task<(string[] Lines, ArchiveListResult? Error)> RunListingCommandAsync(
        TarSandboxScope scope, string listFlag, CancellationToken cancellationToken)
    {
        var (exitCode, stdOut, stdErr) = await scope.RunAsync(
            [listFlag, scope.StagedArchivePath], cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            return ([], new ArchiveListResult
            {
                Success = false,
                ErrorMessage = IsLikelyEncryptionFailure(stdErr)
                    ? "This archive is password-protected and cannot be browsed."
                    : stdErr.Trim()
            });
        }

        return (SplitLines(stdOut), null);
    }

    private static List<ArchiveEntryInfo> BuildEntryList(string[] names, string[] typeLines)
    {
        var entries = new List<ArchiveEntryInfo>(names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            char typeChar = typeLines[i].Length > 0 ? typeLines[i][0] : '?';
            entries.Add(new ArchiveEntryInfo
            {
                Path = names[i].TrimEnd('/'),
                Size = typeChar == '-' ? ParseTarListingSize(typeLines[i]) : 0,
                CompressedSize = 0,
                // Date column was observed locale-mangled (see this method's sibling
                // ScanForUnsafeEntriesAsync's comment and DECISIONS.md's T-F84 entry) — left null
                // rather than risk a half-correct parse; the UI shows "—" instead.
                Modified = null,
                IsDirectory = typeChar == 'd',
            });
        }
        return entries;
    }

    /// <inheritdoc/>
    // T-F105: deliberately unsandboxed — SourcePaths are trusted local files the user selected,
    // not an untrusted archive being parsed, so T-F52's threat model (a hostile archive driving
    // libarchive into misbehaving) does not apply. See SECURITY.md's tar.exe Trust Model section
    // for the extraction-vs-creation distinction. Still runs the same Authenticode signature
    // check as every other tar.exe launch site (SandboxedProcessLauncher.RunAsync,
    // DetectCapabilitiesAsync above) — cheap, not a substitute for the sandbox, but no launch
    // site should skip it.
    public async Task<ArchiveResult> CompressAsync(
        ArchiveOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ArchiveError>();
        var createdFiles = new List<string>();
        var skippedFiles = new List<SkippedFile>();
        var conflictResolver = new ConflictResolver(options.OnConflict, options.ResolveConflictAsync);

        if (!TarSignatureVerifier.Verify(TarExecutablePath))
        {
            errors.Add(new ArchiveError
            {
                SourcePath = options.DestinationFolder,
                Message = "tar.exe failed Authenticode signature verification; refusing to run it."
            });
            return new ArchiveResult { Success = false, CreatedFiles = createdFiles, Errors = errors, SkippedFiles = skippedFiles };
        }

        Directory.CreateDirectory(options.DestinationFolder);
        string extension = ArchiveNaming.GetExtension(options.Format);

        if (options.Mode == ArchiveMode.SingleArchive)
        {
            // T-F99: same drive-root/empty-name fallback ZipArchiveService.ArchiveAsync already
            // uses for a single-source drive-root selection (e.g. "Z:\" via the shell extension's
            // Drive ItemType) instead of silently naming the archive after the bare extension.
            string archiveName = ArchiveNaming.ResolveSingleArchiveName(options.ArchiveName, options.SourcePaths);
            string destPath = Path.Combine(options.DestinationFolder, archiveName + extension);

            var (outcome, resolvedDestPath) = await ResolveDestinationConflictAsync(destPath, conflictResolver).ConfigureAwait(false);
            if (outcome == DestinationConflictOutcome.Skip)
            {
                return new ArchiveResult
                {
                    Success = true,
                    CreatedFiles = [],
                    Errors = [],
                    SkippedFiles = [.. options.SourcePaths.Select(p => new SkippedFile
                    {
                        Path = p,
                        Reason = $"Archive '{Path.GetFileName(destPath)}' already exists at the destination and was skipped."
                    })],
                };
            }

            await CompressToArchiveAsync(options, resolvedDestPath, createdFiles, errors, skippedFiles, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        else // ArchiveMode.SeparateArchives — one archive per top-level source path
        {
            var sink = new ArchiveResultSink(errors, createdFiles, skippedFiles);
            await ProcessSeparateArchivesAsync(options, extension, conflictResolver, sink, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        var result = new ArchiveResult
        {
            Success = errors.Count == 0,
            CreatedFiles = createdFiles,
            Errors = errors,
            SkippedFiles = skippedFiles,
        };

        if (result.Success && options.OpenDestinationFolder)
        {
            ExplorerLauncher.OpenFolder(options.DestinationFolder);
        }

        return result;
    }

    private enum DestinationConflictOutcome { Proceed, Skip }

    // Shared by CompressAsync's SingleArchive branch and ProcessSeparateArchivesAsync -- both used
    // to run the identical File.Exists+switch(ConflictBehavior) block, just with different Skip // NOSONAR: prose, not commented-out code (S125 false positive)
    // handling (a whole-method early return vs. a per-item skippedFiles.Add+continue), which stays
    // the caller's job. Returns the destination path to actually write to (renamed if the conflict
    // resolution was Rename, unchanged otherwise).
    private static async Task<(DestinationConflictOutcome Outcome, string DestPath)> ResolveDestinationConflictAsync(
        string destPath, ConflictResolver conflictResolver)
    {
        if (!File.Exists(destPath))
            return (DestinationConflictOutcome.Proceed, destPath);

        switch (await conflictResolver.ResolveAsync(destPath).ConfigureAwait(false))
        {
            case ConflictBehavior.Skip:
                return (DestinationConflictOutcome.Skip, destPath);
            case ConflictBehavior.Overwrite:
                File.Delete(destPath);
                return (DestinationConflictOutcome.Proceed, destPath);
            case ConflictBehavior.Rename:
                return (DestinationConflictOutcome.Proceed, GetUniqueFilePath(destPath));
            default:
                return (DestinationConflictOutcome.Proceed, destPath);
        }
    }

    private static async Task ProcessSeparateArchivesAsync(
        ArchiveOptions options, string extension, ConflictResolver conflictResolver,
        ArchiveResultSink sink, IProgress<ProgressReport>? progress, CancellationToken cancellationToken)
    {
        var sortedSourcePaths = options.SourcePaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (string sourcePath in sortedSourcePaths)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            {
                sink.Errors.Add(new ArchiveError { SourcePath = sourcePath, Message = $"Source path does not exist: {sourcePath}" });
                continue;
            }

            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            string destPath = Path.Combine(options.DestinationFolder, baseName + extension);

            var (outcome, resolvedDestPath) = await ResolveDestinationConflictAsync(destPath, conflictResolver).ConfigureAwait(false);
            if (outcome == DestinationConflictOutcome.Skip)
            {
                sink.SkippedFiles.Add(new SkippedFile
                {
                    Path = sourcePath,
                    Reason = $"Archive '{Path.GetFileName(destPath)}' already exists at the destination and was skipped."
                });
                continue;
            }

            var singleOptions = options with { SourcePaths = [sourcePath] };
            await CompressToArchiveAsync(singleOptions, resolvedDestPath, sink.CreatedFiles, sink.Errors, sink.SkippedFiles, progress, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // Runs one tar.exe -cf invocation writing to a ".tmp" path, then atomically moves it to
    // destPath only if at least one entry was actually written — mirrors ZipArchiveService's
    // temp-then-commit pattern (no partial files on cancel or failure, CLAUDE.md hard
    // constraint). Reparse-point sources are skipped (T-F23 precedent); missing sources are
    // reported as ArchiveError, matching ZipArchiveService.ArchiveAsync's per-item handling.
    private static async Task CompressToArchiveAsync(
        ArchiveOptions options,
        string destPath,
        List<string> createdFiles,
        List<ArchiveError> errors,
        List<SkippedFile> skippedFiles,
        IProgress<ProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        string tempPath = destPath + ".tmp";

        var tarArgs = new List<string>();
        AppendCompressionFilterArgs(tarArgs, options.Format, options.CompressionLevel);
        tarArgs.Add("-v");
        tarArgs.Add("-cf");
        tarArgs.Add(tempPath);

        var sortedSourcePaths = options.SourcePaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        (int entryCount, long totalEntriesForProgress, long totalBytesForProgress) =
            AppendSourcesToTarArgs(tarArgs, sortedSourcePaths, errors, skippedFiles);

        if (entryCount == 0)
            return;

        int reportedFiles = 0;
        void OnVerboseLine(string line)
        {
            reportedFiles++;
            long denominator = Math.Max(totalEntriesForProgress, 1);
            long bytesTransferred = totalBytesForProgress > 0
                ? Math.Min(totalBytesForProgress, totalBytesForProgress * reportedFiles / denominator)
                : 0;
            progress?.Report(new ProgressReport
            {
                Percent = Math.Min(99, (int)(reportedFiles * 100L / denominator)),
                BytesTransferred = bytesTransferred,
                TotalBytes = totalBytesForProgress,
                CurrentFile = ParseTarVerboseEntryName(line),
            });
        }

        try
        {
            var (exitCode, _, stdErr) = await RunUnsandboxedTarAsync(tarArgs, OnVerboseLine, cancellationToken).ConfigureAwait(false);

            if (exitCode != 0 || !File.Exists(tempPath))
            {
                TryDeleteBestEffort(tempPath);
                errors.Add(new ArchiveError { SourcePath = destPath, Message = $"tar.exe failed to create archive: {stdErr.Trim()}" });
                return;
            }

            File.Move(tempPath, destPath, overwrite: true);
            createdFiles.Add(destPath);
            progress?.Report(new ProgressReport { Percent = 100, BytesTransferred = totalBytesForProgress, TotalBytes = totalBytesForProgress });
        }
        catch (OperationCanceledException)
        {
            TryDeleteBestEffort(tempPath);
            throw;
        }
        catch (IOException ex)
        {
            TryDeleteBestEffort(tempPath);
            errors.Add(new ArchiveError { SourcePath = destPath, Message = $"Cannot create archive: {ex.Message}", Exception = ex });
        }
        catch (UnauthorizedAccessException ex)
        {
            TryDeleteBestEffort(tempPath);
            errors.Add(new ArchiveError { SourcePath = destPath, Message = $"Access denied creating archive: {ex.Message}", Exception = ex });
        }
    }

    private static void TryDeleteBestEffort(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }

    // T-F140: the percent denominator used by CompressToArchiveAsync's OnVerboseLine must reflect
    // the real number of entries tar.exe will emit a "-v" line for (every file AND directory
    // recursed into), not just the count of top-level selected source paths — a source folder
    // tree with more than a handful of files made reportedFiles race past entryCount almost
    // instantly, clamping the dialog at 99% for the rest of a multi-minute operation.
    // TotalBytes is the real sum of source file sizes, used only to give the dialog a
    // "X GB / Y GB" readout matching ZIP's — tar.exe's "-v" output during creation carries no
    // per-file byte/throughput info (only "a <name>" once a whole entry is done), so
    // BytesTransferred in OnVerboseLine is deliberately entry-count-weighted, not a real running
    // byte total. See DECISIONS.md's T-F140 entry.
    private static (int EntryCount, long TotalEntriesForProgress, long TotalBytesForProgress) AppendSourcesToTarArgs(
        List<string> tarArgs, IReadOnlyList<string> sortedSourcePaths, List<ArchiveError> errors, List<SkippedFile> skippedFiles)
    {
        int entryCount = 0;
        long totalEntriesForProgress = 0;
        long totalBytesForProgress = 0;

        foreach (string sourcePath in sortedSourcePaths)
        {
            if (ArchiveEntrySecurity.IsReparsePoint(sourcePath))
            {
                skippedFiles.Add(new SkippedFile
                {
                    Path = sourcePath,
                    Reason = "Symbolic links and NTFS junctions are not archived."
                });
                continue;
            }

            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            {
                errors.Add(new ArchiveError { SourcePath = sourcePath, Message = $"Source path does not exist: {sourcePath}" });
                continue;
            }

            string fullSource = Path.GetFullPath(sourcePath);
            string? parent = Path.GetDirectoryName(fullSource);
            string name = Path.GetFileName(fullSource);

            if (string.IsNullOrEmpty(name))
            {
                // Drive-root source (e.g. "Z:\") — GetFileName returns "" and GetDirectoryName
                // returns null. tar.exe strips the drive letter from a rooted absolute-path
                // argument on its own (see IsDangerousEntryName's comment above) — pass it
                // through directly rather than via -C. Same edge case T-F99 already handles for
                // ZipArchiveService; needs its own on-device confirmation in Phase C/D.
                tarArgs.Add(fullSource);
            }
            else
            {
                tarArgs.Add("-C");
                tarArgs.Add(parent!);
                tarArgs.Add(name);
            }

            entryCount++;
            (long entries, long bytes) = CountRecursiveEntriesAndBytes(fullSource);
            totalEntriesForProgress += entries;
            totalBytesForProgress += bytes;
        }

        return (entryCount, totalEntriesForProgress, totalBytesForProgress);
    }

    // T-F140: real count of entries tar.exe will emit a "-v" line for when archiving this one
    // source path — a plain file is exactly 1; a directory is itself plus every file/subdirectory
    // recursed into. Only an approximation of the denominator used for the progress percentage
    // (not exact — e.g. it doesn't replicate tar.exe's own symlink-following rules), which is fine
    // since OnVerboseLine's Math.Min(99, ...) clamp already tolerates undercounting, and a rough
    // but roughly-linear percentage is a large improvement over the prior top-level-path-only count
    // that clamped to 99% almost instantly for any folder with more than a handful of files.
    // TotalBytes is the real sum of file sizes (directories contribute 0) — used only for the
    // dialog's "X GB / Y GB" readout (see OnVerboseLine's own remarks on why BytesTransferred is
    // entry-count-weighted, not a real running byte total).
    private static (long EntryCount, long TotalBytes) CountRecursiveEntriesAndBytes(string sourcePath)
    {
        if (!Directory.Exists(sourcePath))
        {
            long size = 0;
            try { size = new FileInfo(sourcePath).Length; }
            catch (IOException) { /* best-effort estimate */ }
            catch (UnauthorizedAccessException) { /* same */ }
            return (1, size); // plain file (or something that no longer exists by the time we get here)
        }

        long count = 1; // the directory itself gets its own tar entry
        long totalBytes = 0;
        try
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(sourcePath, "*", SearchOption.AllDirectories))
            {
                count++;
                try
                {
                    if ((File.GetAttributes(entry) & FileAttributes.Directory) == 0)
                        totalBytes += new FileInfo(entry).Length;
                }
                catch (IOException) { /* best-effort estimate — the Math.Min(99, ...) clamp above tolerates undercounting */ }
                catch (UnauthorizedAccessException) { /* same */ }
            }
        }
        catch (UnauthorizedAccessException) { /* same */ }
        catch (IOException) { /* same */ }

        return (count, totalBytes);
    }

    // tar.exe's "-v" creation-mode output is "a <name>" per entry (confirmed empirically —
    // RunUnsandboxedTarAsync's own comment documents this), with no size/throughput info. Strips
    // the fixed "a " prefix so the dialog can show the real entry name instead of the raw line; // NOSONAR: prose, not commented-out code (S125 false positive)
    // falls back to the raw line unchanged if it doesn't match the expected shape, so a format
    // surprise degrades to "a slightly odd-looking filename shown", never a thrown exception.
    private static string ParseTarVerboseEntryName(string verboseLine) =>
        verboseLine.StartsWith("a ", StringComparison.Ordinal) ? verboseLine[2..] : verboseLine;

    // Maps the selected container format + the existing ZIP CompressionLevel enum (reused as the
    // UI-facing knob rather than inventing a second one) to tar.exe's real
    // "--options <filter>:compression-level=N" mechanism — confirmed empirically during T-F105
    // planning that a bare "-9"-style flag does NOT work (exit 1), but --options does, for all
    // five write filters (gzip/bzip2/xz/zstd/lzma), and that "compression-level=0" is a real
    // store/no-compression mode (see DECISIONS.md's T-F105 entry for the raw command output).
    // Plain Tar gets no filter flag and no --options at all — passing --options without an
    // active filter fails with "Unknown module name", confirmed empirically the same round.
    private static void AppendCompressionFilterArgs(List<string> tarArgs, ArchiveContainerFormat format, System.IO.Compression.CompressionLevel level)
    {
        (string? filterFlag, string? moduleName, int max) = format switch
        {
            ArchiveContainerFormat.Tar => ((string?)null, (string?)null, 0),
            ArchiveContainerFormat.TarGz => ("-z", "gzip", 9),
            ArchiveContainerFormat.TarBz2 => ("-j", "bzip2", 9),
            ArchiveContainerFormat.TarXz => ("-J", "xz", 9),
            ArchiveContainerFormat.TarZst => ("--zstd", "zstd", 19),
            ArchiveContainerFormat.TarLzma => ("--lzma", "lzma", 9),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };

        if (filterFlag is null)
            return;

        tarArgs.Add(filterFlag);

        int numericLevel = level switch
        {
            System.IO.Compression.CompressionLevel.NoCompression => 0,
            System.IO.Compression.CompressionLevel.Fastest => 1,
            System.IO.Compression.CompressionLevel.SmallestSize => max,
            _ => Math.Max(1, max / 2), // Optimal (and any future enum value) — libarchive's own
                                        // conventional mid-range default (gzip's default is 6 of 9)
        };

        tarArgs.Add("--options");
        tarArgs.Add($"{moduleName}:compression-level={numericLevel}");
    }

    // Unsandboxed tar.exe launch for archive CREATION only (see CompressAsync's own comment for
    // why this is safe to run outside the AppContainer). Mirrors TarSandboxScope.RunAsync's
    // (exitCode, stdOut, stdErr) shape for consistency, but has no quarantine/ACL/Job-Object
    // setup — just a plain redirected-IO process launch, the same shape
    // DetectCapabilitiesAsync above already uses for its own deliberately-unsandboxed probe.
    // onStdErrLine is invoked once per non-empty stderr line as it streams in — tar.exe's "-v"
    // writes each added entry's "a <name>" line to STDERR during creation (confirmed
    // empirically; NOT stdout), so this is how per-entry progress is derived.
    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunUnsandboxedTarAsync(
        IReadOnlyList<string> arguments,
        Action<string>? onStdErrLine,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = TarExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in arguments)
            startInfo.ArgumentList.Add(arg);

        using Process process = new() { StartInfo = startInfo };

        var stdOutBuilder = new System.Text.StringBuilder();
        var stdErrBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdOutBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
                return;
            stdErrBuilder.AppendLine(e.Data);
            if (e.Data.Length > 0)
                onStdErrLine?.Invoke(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw;
        }

        return (process.ExitCode, stdOutBuilder.ToString(), stdErrBuilder.ToString());
    }

    // Column 4 (0-based) of "tar -tvf" output: mode, link-count, owner, group, size, month, day,
    // time, name. Locale-independent (plain ASCII decimal), unlike the date columns — see
    // DECISIONS.md's T-F90 entry.
    private static long ParseTarListingSize(string line)
    {
        string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 4 && long.TryParse(fields[4], out long size) ? size : 0;
    }

    private static string[] SplitLines(string text)
        => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsDangerousEntryName(string entryName)
    {
        if (string.IsNullOrEmpty(entryName))
            return false;

        // T-F49: path-traversal segment check (tar.exe itself also rejects a raw ".." entry,
        // but this is rejected here first regardless — defense-in-depth, not reliance on tar's
        // own behavior).
        if (entryName.Split('/').Any(segment => segment == ".."))
            return true;

        // Rooted paths (leading '/', UNC "\\server\share", or "C:/...") — tar.exe strips the
        // drive letter and keeps these contained (confirmed empirically), but reject outright
        // rather than trust that sanitization.
        if (Path.IsPathRooted(entryName))
            return true;

        if (ArchiveEntrySecurity.HasAlternateDataStreamMarker(entryName))
            return true;

        if (ArchiveEntrySecurity.HasReservedName(entryName))
            return true;

        if (ArchiveEntrySecurity.HasControlCharacters(entryName))
            return true;

        return false;
    }

    // T-F113: reactive classification of a tar.exe/libarchive failure as encryption-related —
    // used for 7z (both data-only and header-encrypted) and RAR's rarer header-encrypted case,
    // where ArchiveFormatDetector.IsEncryptedRar's proactive byte check can't apply (RAR's
    // data-only case is caught proactively instead; see ExtractAsync/ListEntriesAsync). Unlike
    // the RAR byte check, 7z's header metadata is itself typically LZMA-compressed, so a
    // fixed-offset check isn't feasible without a partial 7z reader — see DECISIONS.md's T-F113
    // entry. Confirmed empirically against real 7-Zip/WinRAR-encrypted fixtures that libarchive's
    // own stderr always contains "encrypt" (case-insensitive) for every encryption-related
    // failure it produces: "The file content is encrypted, but currently not supported",
    // "The archive header is encrypted, but currently not supported",
    // "Reading encrypted data is not currently supported", "Encryption is not supported".
    private static bool IsLikelyEncryptionFailure(string stdErr)
        => stdErr.Contains("encrypt", StringComparison.OrdinalIgnoreCase);

    // Walks the sandbox scope's output directory without ever recursing into a reparse-point
    // subdirectory — a plain Directory.EnumerateFiles(..., AllDirectories) would follow such a
    // directory and could walk straight out of quarantine. The pre-scan already rejects any
    // archive containing a symlink entry, so this is defense-in-depth for anything the scan
    // didn't anticipate, not the primary safety mechanism.
    // T-F146: internal (was private) — AntivirusScanService walks the same quarantine output
    // directory shape and reuses this exact reparse-point-safe walk.
    internal static IEnumerable<string> EnumerateFilesGuarded(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string dir = pending.Pop();

            List<string> files;
            List<string> subDirs;
            try
            {
                files = Directory.EnumerateFiles(dir).ToList();
                subDirs = Directory.EnumerateDirectories(dir).ToList();
            }
            catch
            {
                continue;
            }

            foreach (string file in files)
                yield return file;

            foreach (string subDir in subDirs.Where(d => !ArchiveEntrySecurity.IsReparsePoint(d)))
                pending.Push(subDir);
        }
    }

    // Same "name (1)", "name (2)", ... convention as ZipArchiveService.GetUniqueFilePath. Not
    // shared via ArchiveEntrySecurity — this is a naming convenience, not a security check, and
    // each file here is moved (not written) one at a time, so File.Exists sees every prior move
    // in this same run without needing an in-memory claimed-paths set the way ZIP's single-pass
    // write-then-commit flow does.
    private static string GetUniqueFilePath(string path)
    {
        string dir = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        int i = 1;
        string candidate;
        do { candidate = Path.Combine(dir, $"{name} ({i++}){ext}"); }
        while (File.Exists(candidate));
        return candidate;
    }

    // T-F146: internal (was private) — AntivirusScanService catches this the same way
    // ExtractSingleArchiveAsync's own callers do, to map a rejected archive to an Inconclusive
    // finding rather than an unhandled throw.
    internal sealed class TarArchiveRejectedException(string message) : Exception(message); // NOSONAR: S3871 — deliberately internal, never escapes Archiver.Core's public surface (always caught and converted to ArchiveError/Inconclusive, per this project's "services never throw to callers" rule); public would be pure API-surface bloat
}
