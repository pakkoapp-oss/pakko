using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Archiver.Core.IO;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services.Zip;
using Archiver.Core.Services.Zip.Decryption;

namespace Archiver.Core.Services;

/// <summary>
/// ZIP archive service using System.IO.Compression.
/// Never throws to callers — all errors are captured in ArchiveResult.Errors.
/// </summary>
public sealed class ZipArchiveService : IArchiveService
{
    private const int CopyBufferSize = 81920;        // 80 KB — CopyToAsync transfer buffer
    private const int FileStreamBufferSize = 262144; // 256 KB — FileStream read buffer (archiving)

    // T-F35: below this file count, SingleArchive mode uses the original, unmodified, always-
    // sequential ZipArchive-based path below (proven, low-risk, and the overwhelming majority of
    // real usage). Above it, ArchiveAsync routes into Zip.ParallelSingleArchiveWriter's hand-
    // rolled writer instead — a materially larger and newer code path, deliberately narrowed to
    // only the workload shape (many files, one shared archive) where T-F114 measured a real ~6x
    // regression against a 7z reference. See DECISIONS.md's T-F35 entry.
    private const int ParallelPipelineFileCountThreshold = 64;

    private readonly GroupPolicyOptions _policy;

    /// <summary>
    /// Creates the service. T-F51: policy is optional so every existing
    /// <c>new ZipArchiveService()</c> call site keeps compiling — a null policy means "everything
    /// allowed", matching today's shipped behavior exactly.
    /// </summary>
    public ZipArchiveService(GroupPolicyOptions? policy = null)
    {
        _policy = policy ?? new GroupPolicyOptions();
    }

    // T-F234: the OEM/ANSI pages entry names without the UTF-8 flag are decoded with. Tests pin
    // them so expectations hold on a machine with other pages (the en-US CI runner).
    internal ZipNameCodePages NameCodePages { get; init; } = ZipNameCodePages.System;

    /// <inheritdoc/>
    public async Task<ArchiveResult> ArchiveAsync(
        ArchiveOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // T-F153: a source path ending in a directory separator (e.g. "src/" or "src\" — common
        // from shell tab-completion or a manually-typed CLI argument) made every downstream
        // Path.GetFileName(sourcePath) call return "" instead of the real folder name, silently
        // producing entries rooted at the archive's own top level ("/binary.dat") instead of under
        // their real parent folder ("src/binary.dat"). Path.TrimEndingDirectorySeparator correctly
        // leaves a true drive root ("C:\") untouched — see ArchiveNaming's own drive-root handling
        // for why that distinction matters (T-F99). Normalized once here, at the top, so every
        // downstream Path.GetFileName call in this class already sees a clean path.
        options = options with { SourcePaths = [.. options.SourcePaths.Select(Path.TrimEndingDirectorySeparator)] };

        var errors = new List<ArchiveError>();
        var createdFiles = new List<string>();
        var skippedFiles = new List<SkippedFile>();
        var conflictResolver = new ConflictResolver(options.OnConflict, options.ResolveConflictAsync);

        // T-F193: resolved before either mode runs, never after the destination-conflict step —
        // that step can already have deleted an existing archive the user chose to overwrite, and
        // a cancelled prompt afterward would leave them with neither the old archive nor a new one.
        string? password = null;
        if (options.ResolvePasswordAsync is not null)
        {
            ArchiveError? passwordError;
            (password, passwordError) = await ResolveEncryptionPasswordAsync(options).ConfigureAwait(false);
            if (passwordError is not null)
                return new ArchiveResult { Success = false, CreatedFiles = [], Errors = [passwordError], SkippedFiles = [] };
        }
        var run = new ArchiveRunContext(conflictResolver, password);
        IEnumerable<SourceResult> sources;

        if (options.Mode == ArchiveMode.SingleArchive)
        {
            ArchiveResult? skipResult = await ArchiveSingleArchiveModeAsync(
                options, run, errors, createdFiles, skippedFiles, progress, cancellationToken).ConfigureAwait(false);
            // T-F87: an already-exists+Skip conflict returns its own complete result immediately
            // (every source reported skipped) — bypasses OpenDestinationFolder below entirely,
            // matching this method's original behavior.
            if (skipResult is not null)
                return skipResult;

            // T-F260: one archive for every source — each is Completed only when the whole call
            // was clean (per-source attribution of an issue inside one folder is not attempted).
            bool clean = errors.Count == 0 && skippedFiles.Count == 0;
            sources = options.SourcePaths.Select(p => SourceOutcomeRules.Classify(p, createdFiles.Count > 0, clean));
        }
        else // SeparateArchives
        {
            sources = await ArchiveSeparateArchivesModeAsync(
                options, run, errors, createdFiles, skippedFiles, progress, cancellationToken).ConfigureAwait(false);
        }

        var result = new ArchiveResult
        {
            Success = errors.Count == 0,
            CreatedFiles = createdFiles,
            Errors = errors,
            SkippedFiles = skippedFiles,
            Sources = SourceOutcomeRules.DowngradeSourcesContainingOutputs(sources, createdFiles),
        };

        if (result.Success && options.OpenDestinationFolder)
        {
            ExplorerLauncher.OpenFolder(options.DestinationFolder);
        }

        return result;
    }

    // The per-call state both archive modes share — bundled to keep S107's parameter count down.
    // Password is null for an ordinary unencrypted archive (T-F193).
    private sealed record ArchiveRunContext(ConflictResolver ConflictResolver, string? Password);

    // T-F193: one prompt per ArchiveAsync call, never retried — there is nothing to verify a new
    // password against, so a wrong attempt cannot exist (maxAttempts: 1). The App/CLI prompts ask
    // for a confirmation themselves; Core only refuses a cancelled or empty answer.
    private static async Task<(string? Password, ArchiveError? Error)> ResolveEncryptionPasswordAsync(ArchiveOptions options)
    {
        string archiveName = ArchiveNaming.ResolveSingleArchiveName(options.ArchiveName, options.SourcePaths)
            + ArchiveNaming.GetExtension(ArchiveContainerFormat.Zip);
        var resolver = new PasswordResolver(options.ResolvePasswordAsync, maxAttempts: 1);
        string? password = await resolver.ResolveAsync(archiveName, PasswordPurpose.Encrypt, verify: _ => true).ConfigureAwait(false);

        // User decision 2026-09-24 — see EncryptionPasswordRule for the 7-Zip sources.
        string? failure = password is null
            ? "Archive was not created: no password was entered."
            : EncryptionPasswordRule.Check(password) switch
            {
                EncryptionPasswordProblem.None => null,
                EncryptionPasswordProblem.Empty => "Archive was not created: the password is empty.",
                EncryptionPasswordProblem.UnsupportedCharacters =>
                    "Archive was not created: the password may contain only English letters, digits, spaces and " +
                    "ASCII punctuation; other ZIP tools such as 7-Zip cannot open an archive protected by any other characters.",
                EncryptionPasswordProblem.TooLong =>
                    $"Archive was not created: the password is longer than {EncryptionPasswordRule.MaxLength} characters, " +
                    "the most 7-Zip accepts for an AES-encrypted ZIP.",
                var other => throw new System.Diagnostics.UnreachableException($"Unhandled {other}"),
            };
        return failure is null
            ? (password, null)
            : (null, new ArchiveError { SourcePath = options.DestinationFolder, Message = failure });
    }

    // Returns non-null only for the already-exists+Skip conflict case, which the caller must
    // return immediately as ArchiveAsync's own result (see the comment at that call site) — every
    // other outcome (including all errors) is recorded into errors/createdFiles/skippedFiles and
    // this returns null so the caller proceeds to its normal result assembly.
    private static async Task<ArchiveResult?> ArchiveSingleArchiveModeAsync(
        ArchiveOptions options, ArchiveRunContext run,
        List<ArchiveError> errors, List<string> createdFiles, List<SkippedFile> skippedFiles,
        IProgress<ProgressReport>? progress, CancellationToken cancellationToken)
    {
        // T-F99: Path.GetFileNameWithoutExtension returns "" for a drive root (e.g. "Z:\"), now a
        // reachable single-source selection via the shell extension's Drive ItemType — falls back
        // to "archive" the same way BuildAddToArchiveTitle already does for the context-menu
        // title text, instead of silently naming the archive ".zip".
        string archiveName = ArchiveNaming.ResolveSingleArchiveName(options.ArchiveName, options.SourcePaths);
        string destPath = Path.Combine(options.DestinationFolder, archiveName + ArchiveNaming.GetExtension(ArchiveContainerFormat.Zip));

        Directory.CreateDirectory(options.DestinationFolder);

        // T-F158: shared with TarSandboxedService's equivalent conflict decision — see
        // DestinationConflictResolver and DECISIONS.md's T-F158 entry.
        var (outcome, resolvedDestPath) = await DestinationConflictResolver.ResolveAsync(
            destPath, onDiskConflict: File.Exists(destPath), sameRunConflict: false,
            run.ConflictResolver, renameCandidate: p => GetUniqueFilePath(p)).ConfigureAwait(false);

        if (outcome == DestinationConflictOutcome.Skip)
        {
            // T-F87: report every source as skipped (not just a bare empty result) so
            // MainViewModel's DeleteAfterOperation cleanup can tell these sources were
            // never archived and must not be deleted.
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
        if (outcome == DestinationConflictOutcome.ProceedAfterDeletingExisting)
            File.Delete(destPath);
        destPath = resolvedDestPath;

        string tempPath = destPath + ".tmp";

        // T-F35 profiling (2026-07-18) found ComputeTotalBytes and the gate's file count used to
        // walk the same directory tree in two separate passes (~193ms combined against a
        // 5,000-file fixture, ~20-25% of total archiving time) — merged into one combined walk.
        (long totalSourceBytes, int totalFileCount) = ComputeSingleArchiveTotals(options.SourcePaths);
        progress?.Report(new ProgressReport { Percent = 0, BytesTransferred = 0, TotalBytes = totalSourceBytes });

        // T-F31/T-F32: Sort source paths for deterministic archive entry order (ordinal, case-insensitive).
        // This ensures identical inputs always produce identical archives regardless of the order
        // in which the caller supplies paths or the OS enumerates them.
        var sortedSourcePaths = options.SourcePaths
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // T-F35: gate the parallel pipeline behind a file-count threshold — see the constant's
        // own comment. T-F193: encryption exists only in the hand-rolled writer (ZipArchive has no
        // encrypting API), so a password always takes that path regardless of file count.
        bool useParallelPipeline = run.Password is not null || totalFileCount > ParallelPipelineFileCountThreshold;

        try
        {
            if (useParallelPipeline)
            {
                var callbacks = new Zip.ParallelSingleArchiveWriter.ReportCallbacks(skippedFiles.Add, errors.Add);
                await Zip.ParallelSingleArchiveWriter.WriteAsync(
                    tempPath, sortedSourcePaths, new Zip.ParallelSingleArchiveWriter.CompressionSettings(options.CompressionLevel, run.Password),
                    totalSourceBytes, callbacks, progress, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var sink = new ArchiveWorkSink(errors, skippedFiles);
                await WriteSequentialSingleArchiveAsync(
                    tempPath, sortedSourcePaths, options.CompressionLevel, totalSourceBytes, sink, progress, cancellationToken)
                    .ConfigureAwait(false);
            }

            // T-F21: Commit the archive even when per-item errors occurred so that successfully
            // archived files are preserved. Fatal errors (IOException creating the archive
            // itself) are still caught below and delete the temp.
            // T-F60: Only commit if at least one entry was written. When every source path
            // failed (missing, locked, etc.) the temp is an empty ZIP — discard it so no
            // zero-entry archive and no leftover .tmp lands on disk.
            // T-F260: never commit after a cancel — several loops inside the writers end with a
            // `break` rather than a throw, which would otherwise leave a partial archive that
            // looks finished.
            cancellationToken.ThrowIfCancellationRequested();
            if (HasTempEntries(tempPath))
            {
                File.Move(tempPath, destPath, overwrite: true);
                createdFiles.Add(destPath);
            }
            else
            {
                TryDeleteBestEffort(tempPath);
            }
        }
        catch (OperationCanceledException)
        {
            TryDeleteBestEffort(tempPath);
            throw;
        }
        catch (IOException ex)
        {
            TryDeleteBestEffort(tempPath);
            errors.Add(new ArchiveError
            {
                SourcePath = destPath,
                Message = $"Cannot create archive: {ex.Message}",
                Exception = ex
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            TryDeleteBestEffort(tempPath);
            errors.Add(new ArchiveError
            {
                SourcePath = destPath,
                Message = $"Access denied creating archive: {ex.Message}",
                Exception = ex
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryDeleteBestEffort(tempPath);
            errors.Add(new ArchiveError
            {
                SourcePath = destPath,
                Message = $"Unexpected error: {ex.Message}",
                Exception = ex
            });
        }

        return null;
    }

    // The two List sinks WriteSequentialSingleArchiveAsync/AddDirectoryToArchiveAsync's callers
    // write into — bundled to cut S107's parameter count.
    private sealed record ArchiveWorkSink(List<ArchiveError> Errors, List<SkippedFile> SkippedFiles);

    private static async Task WriteSequentialSingleArchiveAsync(
        string tempPath, List<string> sortedSourcePaths, CompressionLevel compressionLevel, long totalSourceBytes,
        ArchiveWorkSink sink, IProgress<ProgressReport>? progress, CancellationToken cancellationToken)
    {
        await Task.Run(async () =>
        {
            using var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create);
            int total = sortedSourcePaths.Count;
            long byteOffset = 0;
            // T-F30: multiple top-level SourcePaths can share a basename (e.g. two selected
            // files both named "report.txt" from different folders) — track names already
            // claimed at the archive root and rename later occurrences, the same way
            // GetUniqueFilePath renames colliding output files on disk.
            var usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < total; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                byteOffset += await AddOneSourcePathToArchiveAsync(
                    archive, sortedSourcePaths[i], compressionLevel,
                    new EntryWriteProgress(totalSourceBytes, byteOffset, progress),
                    usedEntryNames, sink, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    // One source path of WriteSequentialSingleArchiveAsync's loop. Returns pathSize (best-effort
    // size of the source), which the caller adds to its running byteOffset regardless of outcome
    // — matches the original inline loop's unconditional trailing `byteOffset += pathSize`.
    private static async Task<long> AddOneSourcePathToArchiveAsync(
        ZipArchive archive, string sourcePath, CompressionLevel compressionLevel, EntryWriteProgress progressInfo,
        HashSet<string> usedEntryNames, ArchiveWorkSink sink, CancellationToken cancellationToken)
    {
        // Compute source size for offset tracking (best-effort)
        long pathSize = 0;
        if (File.Exists(sourcePath))
            try { pathSize = new FileInfo(sourcePath).Length; } catch { /* best-effort */ }
        else if (Directory.Exists(sourcePath))
            pathSize = ComputeDirectoryBytes(sourcePath);

        // T-F23: Skip top-level symlinks and NTFS junctions
        if (ArchiveEntrySecurity.IsReparsePoint(sourcePath))
        {
            sink.SkippedFiles.Add(new SkippedFile
            {
                Path = sourcePath,
                Reason = "Symbolic links and NTFS junctions are not archived."
            });
            return pathSize;
        }

        try
        {
            if (Directory.Exists(sourcePath))
            {
                string entryName = GetUniqueEntryName(usedEntryNames, Path.GetFileName(sourcePath));
                var context = new DirectoryArchiveContext(
                    sourcePath, entryName, compressionLevel, sink.SkippedFiles.Add, sink.Errors.Add, progressInfo.TotalBytes, progressInfo.Progress);
                await AddDirectoryToArchiveAsync(archive, sourcePath, context, progressInfo.StartOffset, cancellationToken).ConfigureAwait(false);
            }
            else if (File.Exists(sourcePath))
            {
                string entryName = GetUniqueEntryName(usedEntryNames, Path.GetFileName(sourcePath));
                await AddEntryFromFileAsync(archive, sourcePath, entryName,
                    compressionLevel, progressInfo, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                sink.Errors.Add(new ArchiveError
                {
                    SourcePath = sourcePath,
                    Message = $"Source path does not exist: {sourcePath}"
                });
            }
        }
        catch (IOException ex)
        {
            sink.Errors.Add(new ArchiveError
            {
                SourcePath = sourcePath,
                Message = $"Cannot access file: {ex.Message}",
                Exception = ex
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            sink.Errors.Add(new ArchiveError
            {
                SourcePath = sourcePath,
                Message = $"Access denied: {ex.Message}",
                Exception = ex
            });
        }

        return pathSize;
    }

    private static async Task<IReadOnlyList<SourceResult>> ArchiveSeparateArchivesModeAsync(
        ArchiveOptions options, ArchiveRunContext run,
        List<ArchiveError> errors, List<string> createdFiles, List<SkippedFile> skippedFiles,
        IProgress<ProgressReport>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.DestinationFolder);

        long totalSourceBytes = ComputeTotalBytes(options.SourcePaths);
        progress?.Report(new ProgressReport { Percent = 0, BytesTransferred = 0, TotalBytes = totalSourceBytes });

        // T-F260: an already-cancelled token throws, like a cancel mid-batch — the old graceful
        // empty result looked finished, so "Delete after operation" deleted every source.
        cancellationToken.ThrowIfCancellationRequested();

        // T-F31/T-F32: Sort source paths for deterministic archive entry order (ordinal, case-insensitive).
        var sortedSourcePaths = options.SourcePaths
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var plans = await ResolveSeparateArchivePlansAsync(
            sortedSourcePaths, options.DestinationFolder, run.ConflictResolver, skippedFiles).ConfigureAwait(false);

        var concurrentSink = new ArchiveResultSink([], [], [], []);
        var progressContext = new SeparateArchiveProgressContext(totalSourceBytes, [0], progress);

        // T-F193: an encrypted archive runs ParallelSingleArchiveWriter, which already compresses
        // with up to ComputeWindowCapacity() workers of its own — divide the outer parallelism so
        // the two levels together stay near the core count instead of multiplying.
        int degreeOfParallelism = run.Password is null
            ? Environment.ProcessorCount
            : Math.Max(1, Environment.ProcessorCount / Zip.ParallelSingleArchiveWriter.ComputeWindowCapacity());

        await Parallel.ForEachAsync(
            plans.Where(p => p.DestPath is not null),
            new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism, CancellationToken = cancellationToken },
            async (plan, token) => await ArchiveSingleSeparatePathAsync(
                plan.SourcePath, plan.DestPath!, new Zip.ParallelSingleArchiveWriter.CompressionSettings(options.CompressionLevel, run.Password),
                concurrentSink, progressContext, token).ConfigureAwait(false)
        ).ConfigureAwait(false);

        foreach (var e in concurrentSink.Errors) errors.Add(e);
        foreach (var c in concurrentSink.CreatedFiles) createdFiles.Add(c);
        foreach (var s in concurrentSink.SkippedFiles) skippedFiles.Add(s);

        // Concurrent workers report progress off a shared-but-approximate byte baseline (see
        // ArchiveSingleSeparatePathAsync) — force one final, exact 100% report here so callers
        // always observe a deterministic completion value regardless of how the parallel
        // workers' individual reports interleaved.
        progress?.Report(new ProgressReport { Percent = 100, BytesTransferred = totalSourceBytes, TotalBytes = totalSourceBytes });

        // Plans skipped before the parallel pass (reparse point, conflict Skip) record no
        // SourceResult, so they stay not deletable.
        return [.. concurrentSink.Sources];
    }

    // T-F12: each SourcePath produces a fully independent .zip, so the whole batch can run in
    // parallel. But conflict/collision resolution (OnConflict, and two different SourcePaths
    // sharing a basename) must stay a SEQUENTIAL pre-pass: the original sequential loop relied on
    // File.Exists(destPath) reflecting every prior iteration's completed write, which parallel
    // execution can no longer guarantee (two workers could both observe "doesn't exist yet" and
    // race to write the same .tmp path). Resolving every path's final destination up front —
    // using an in-memory claimedDestPaths set alongside the on-disk check — reproduces the same
    // outcome deterministically before any parallel work starts. See DECISIONS.md's T-F12 entry
    // for the one behavior change this introduces (Overwrite + same-run collision).
    private static async Task<List<(string SourcePath, string? DestPath)>> ResolveSeparateArchivePlansAsync(
        List<string> sortedSourcePaths, string destinationFolder, ConflictResolver conflictResolver, List<SkippedFile> skippedFiles)
    {
        var claimedDestPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plans = new List<(string SourcePath, string? DestPath)>();

        foreach (string sourcePath in sortedSourcePaths)
        {
            // T-F23: Skip top-level symlinks and NTFS junctions
            if (ArchiveEntrySecurity.IsReparsePoint(sourcePath))
            {
                skippedFiles.Add(new SkippedFile
                {
                    Path = sourcePath,
                    Reason = "Symbolic links and NTFS junctions are not archived."
                });
                plans.Add((sourcePath, null));
                continue;
            }

            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            string destPath = Path.Combine(destinationFolder, baseName + ArchiveNaming.GetExtension(ArchiveContainerFormat.Zip));
            bool onDiskConflict = File.Exists(destPath);
            bool sameRunConflict = claimedDestPaths.Contains(destPath);

            // T-F158: shared with ZipArchiveService.ArchiveSingleArchiveModeAsync and
            // TarSandboxedService's equivalent conflict decision — see DestinationConflictResolver
            // and DECISIONS.md's T-F158 entry. Overwrite's same-run-collision-renames-instead
            // behavior (two SourcePaths in this batch sharing a basename would otherwise race
            // under parallel execution) now lives in the shared resolver.
            var (outcome, resolvedDestPath) = await DestinationConflictResolver.ResolveAsync(
                destPath, onDiskConflict, sameRunConflict,
                conflictResolver, renameCandidate: p => GetUniqueFilePath(p, claimedDestPaths)).ConfigureAwait(false);

            if (outcome == DestinationConflictOutcome.Skip)
            {
                // T-F87: record the skip so DeleteAfterOperation cleanup (keyed off
                // SkippedFiles) doesn't delete a source that was never archived.
                skippedFiles.Add(new SkippedFile
                {
                    Path = sourcePath,
                    Reason = $"Archive '{Path.GetFileName(destPath)}' already exists at the destination and was skipped."
                });
                plans.Add((sourcePath, null));
                continue;
            }
            if (outcome == DestinationConflictOutcome.ProceedAfterDeletingExisting)
                File.Delete(destPath);
            destPath = resolvedDestPath;

            claimedDestPaths.Add(destPath);
            plans.Add((sourcePath, destPath));
        }

        return plans;
    }

    // T-F12: archives a single SourcePath (already assigned its final, collision-free destPath
    // by ArchiveAsync's sequential planning pass) into its own independent ZIP. Safe to run
    // concurrently with other calls to this method — each has its own ZipArchive instance and
    // its own destPath/tempPath, so there is no shared archive-writer state.
    //
    // Progress: completedBytesBox[0] is a shared byte counter (Interlocked-updated) across all
    // concurrent workers. Each worker reads a snapshot baseline before it starts and adds its
    // own path's bytes to the shared counter once it finishes — this gives a reasonable,
    // thread-safe approximation of overall progress without any concurrent worker needing to
    // touch another worker's per-entry state. It is not byte-exact when multiple workers are
    // mid-flight at once (their in-progress bytes briefly overlap in the reported total), which
    // is acceptable for a progress bar; ArchiveAsync reports an explicit final 100% after all
    // workers complete so callers always see a deterministic completion value.
    // The three ConcurrentBag sinks ArchiveSingleSeparatePathAsync's parallel workers all write
    // into — bundled to cut S107's parameter count; each worker still just calls .Add on the
    // field it needs, same as before.
    private sealed record ArchiveResultSink(
        ConcurrentBag<ArchiveError> Errors,
        ConcurrentBag<string> CreatedFiles,
        ConcurrentBag<SkippedFile> SkippedFiles,
        ConcurrentBag<SourceResult> Sources);

    // The progress-tracking trio ArchiveSingleSeparatePathAsync's parallel workers all share —
    // bundled to cut S107's parameter count. CompletedBytesBox stays a raw long[] (not e.g. a
    // single long field) since every worker mutates it via Interlocked on the same shared instance.
    private sealed record SeparateArchiveProgressContext(
        long TotalSourceBytes, long[] CompletedBytesBox, IProgress<ProgressReport>? Progress);

    private static async Task ArchiveSingleSeparatePathAsync(
        string sourcePath,
        string destPath,
        Zip.ParallelSingleArchiveWriter.CompressionSettings settings,
        ArchiveResultSink sink,
        SeparateArchiveProgressContext progressContext,
        CancellationToken cancellationToken)
    {
        long totalSourceBytes = progressContext.TotalSourceBytes;
        long[] completedBytesBox = progressContext.CompletedBytesBox;
        IProgress<ProgressReport>? progress = progressContext.Progress;

        long pathSize = 0;
        if (File.Exists(sourcePath))
            try { pathSize = new FileInfo(sourcePath).Length; } catch { /* best-effort */ }
        else if (Directory.Exists(sourcePath))
            pathSize = ComputeDirectoryBytes(sourcePath);

        long baseOffset = Interlocked.Read(ref completedBytesBox[0]);
        string separateTempPath = destPath + ".tmp";
        CompressionLevel compressionLevel = settings.Level;
        // T-F260: this worker's own issue count — the shared bags are written by every worker at
        // once, so a before/after delta on them would not attribute issues to this source.
        int issues = 0;
        bool committed = false;
        void AddSkipped(SkippedFile f) { Interlocked.Increment(ref issues); sink.SkippedFiles.Add(f); }
        void AddError(ArchiveError e) { Interlocked.Increment(ref issues); sink.Errors.Add(e); }
        try
        {
            if (settings.Password is not null && (Directory.Exists(sourcePath) || File.Exists(sourcePath)))
            {
                // T-F193: the only writer that can encrypt. A one-source "single archive" of its
                // own; WorkItemEnumerator names the entries exactly as the branches below do.
                var callbacks = new Zip.ParallelSingleArchiveWriter.ReportCallbacks(AddSkipped, AddError);
                var offsetProgress = progress is null ? null : new OffsetProgress(progress, baseOffset, totalSourceBytes);
                await Zip.ParallelSingleArchiveWriter.WriteAsync(
                    separateTempPath, [sourcePath], settings, pathSize, callbacks, offsetProgress, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (Directory.Exists(sourcePath))
            {
                using var archive = ZipFile.Open(separateTempPath, ZipArchiveMode.Create);
                var context = new DirectoryArchiveContext(
                    sourcePath, Path.GetFileName(sourcePath), compressionLevel, AddSkipped, AddError, totalSourceBytes, progress);
                await AddDirectoryToArchiveAsync(archive, sourcePath, context, baseOffset, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (File.Exists(sourcePath))
            {
                using var archive = ZipFile.Open(separateTempPath, ZipArchiveMode.Create);
                await AddEntryFromFileAsync(archive, sourcePath, Path.GetFileName(sourcePath),
                    compressionLevel, new EntryWriteProgress(totalSourceBytes, baseOffset, progress), cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                AddError(new ArchiveError
                {
                    SourcePath = sourcePath,
                    Message = $"Source path does not exist: {sourcePath}"
                });
                Interlocked.Add(ref completedBytesBox[0], pathSize);
                return;
            }

            // T-F60: Only commit if at least one entry was written (e.g. a directory
            // where all contained files failed would otherwise leave an empty archive).
            // T-F260: never commit after a cancel (see ArchiveSingleArchiveModeAsync's same gate).
            cancellationToken.ThrowIfCancellationRequested();
            if (HasTempEntries(separateTempPath))
            {
                File.Move(separateTempPath, destPath, overwrite: true);
                sink.CreatedFiles.Add(destPath);
                committed = true;
            }
            else
            {
                TryDeleteBestEffort(separateTempPath);
            }
        }
        catch (OperationCanceledException)
        {
            TryDeleteBestEffort(separateTempPath);
            Interlocked.Add(ref completedBytesBox[0], pathSize);
            throw;
        }
        catch (IOException ex)
        {
            TryDeleteBestEffort(separateTempPath);
            AddError(new ArchiveError
            {
                SourcePath = sourcePath,
                Message = $"Cannot access file: {ex.Message}",
                Exception = ex
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            TryDeleteBestEffort(separateTempPath);
            AddError(new ArchiveError
            {
                SourcePath = sourcePath,
                Message = $"Access denied: {ex.Message}",
                Exception = ex
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryDeleteBestEffort(separateTempPath);
            AddError(new ArchiveError
            {
                SourcePath = sourcePath,
                Message = $"Unexpected error: {ex.Message}",
                Exception = ex
            });
        }

        Interlocked.Add(ref completedBytesBox[0], pathSize);
        sink.Sources.Add(SourceOutcomeRules.Classify(sourcePath, committed, issues == 0));
    }

    // T-F193: maps one encrypted SeparateArchives worker's own 0..totalBytes progress onto the
    // whole operation's scale, from the same approximate shared baseline the unencrypted workers
    // use, capped at 99 so no single archive's own finish (including the inner writer's terminal
    // 100%) reads as the whole operation's: ArchiveSeparateArchivesModeAsync alone reports the
    // real 100% once every archive is done.
    private sealed class OffsetProgress(IProgress<ProgressReport> inner, long baseOffset, long totalBytes) : IProgress<ProgressReport>
    {
        public void Report(ProgressReport value)
        {
            if (totalBytes <= 0)
                return;
            long transferred = Math.Min(baseOffset + value.BytesTransferred, totalBytes);
            inner.Report(new ProgressReport
            {
                Percent = (int)Math.Min(99, transferred * 100L / totalBytes),
                BytesTransferred = transferred,
                TotalBytes = totalBytes,
                CurrentFile = value.CurrentFile,
            });
        }
    }

    private static void TryDeleteBestEffort(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }

    /// <inheritdoc/>
    public async Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // T-F06: one instance for the whole call, constructed before the loop below, so an
        // "apply to all" decision on one archive's conflict survives across every subsequent
        // archive in this same ArchivePaths batch, not just the current archive's entries.
        var conflictResolver = new ConflictResolver(options.OnConflict, options.ResolveConflictAsync);
        // T-F189: 3 attempts — wrong-password retries make sense for reading (unlike ArchiveAsync's
        // Encrypt direction, which passes 1; see PasswordResolver's own doc comment).
        var passwordResolver = new PasswordResolver(options.ResolvePasswordAsync, maxAttempts: 3);

        bool destinationExisted = Directory.Exists(options.DestinationFolder);
        Directory.CreateDirectory(options.DestinationFolder);
        ArchiveResult? result = null;
        try
        {
            result = await ExtractArchivesAsync(options, conflictResolver, passwordResolver, progress, cancellationToken)
                .ConfigureAwait(false);
            return result;
        }
        finally
        {
            // T-F230: a run that produced nothing must not leave behind the (empty) folder it
            // created — e.g. Shell's fresh "Extract to <name>\" folder. Never one CreatedFiles names.
            if (!destinationExisted && (result is null || result.CreatedFiles.Count == 0))
                TryDeleteEmptyDirectory(options.DestinationFolder);
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path);
        }
        catch { /* best-effort */ }
    }

    private async Task<ArchiveResult> ExtractArchivesAsync(
        ExtractOptions options, ConflictResolver conflictResolver, PasswordResolver passwordResolver,
        IProgress<ProgressReport>? progress, CancellationToken cancellationToken)
    {
        var errors = new List<ArchiveError>();
        var createdFiles = new List<string>();
        var skippedFiles = new List<SkippedFile>();
        int total = options.ArchivePaths.Count;
        bool singleArchive = total == 1;
        var sources = new List<SourceResult>();
        // Entries skipped because they already exist at the destination — not reported in
        // SkippedFiles (the summary dialog stays as it was), but they make the archive Partial.
        var conflictSkipped = new List<string>();

        for (int i = 0; i < total; i++)
        {
            // T-F260: a cancel between archives throws like a cancel inside one — never a result
            // that looks finished (T-F245).
            cancellationToken.ThrowIfCancellationRequested();

            string archivePath = options.ArchivePaths[i];
            int errorsBefore = errors.Count, skippedBefore = skippedFiles.Count, createdBefore = createdFiles.Count,
                conflictSkippedBefore = conflictSkipped.Count;

            var (rejected, password) = await TryRejectUnsupportedOrEncryptedZipAsync(
                archivePath, errors, skippedFiles, passwordResolver).ConfigureAwait(false);
            if (!rejected)
            {
                string destDir = options.Mode == ExtractMode.SeparateFolders
                    ? Path.Combine(options.DestinationFolder,
                        options.SeparateFolderName ?? ArchiveNaming.GetBaseName(archivePath))
                    : options.DestinationFolder;

                IProgress<ProgressReport>? archiveProgress = singleArchive ? progress : null;
                var sink = new ZipExtractResultSink(errors, createdFiles, skippedFiles, conflictSkipped);
                await ExtractOneZipWithErrorMappingAsync(
                    archivePath, destDir, options, conflictResolver, password, sink, archiveProgress, cancellationToken).ConfigureAwait(false);
            }

            // T-F265: a subset extraction never makes the whole archive deletable.
            sources.Add(SourceOutcomeRules.Classify(archivePath,
                produced: createdFiles.Count > createdBefore,
                clean: errors.Count == errorsBefore && skippedFiles.Count == skippedBefore
                    && conflictSkipped.Count == conflictSkippedBefore && options.SelectedEntryPaths is null));

            if (!singleArchive) progress?.Report(new ProgressReport { Percent = (i + 1) * 100 / total, BytesTransferred = 0, TotalBytes = 0 });
        }

        var result = new ArchiveResult
        {
            Success = errors.Count == 0,
            CreatedFiles = createdFiles,
            Errors = errors,
            SkippedFiles = skippedFiles,
            Sources = sources,
        };

        if (result.Success && options.OpenDestinationFolder)
        {
            ExplorerLauncher.OpenFolder(options.DestinationFolder);
        }

        return result;
    }

    // T-F117: a known-but-unsupported format (RAR/7z/GZip/etc.) is a benign skip, but bytes
    // matching no known archive signature at all (empty file, garbage, a truncated-past-
    // recognition download) is not — it must surface as a real error, not a silent no-op, per
    // this project's "loud error always" convention. Returns (Rejected: true) when the archive
    // was rejected (recorded into errors/skippedFiles already) and extraction should not run at
    // all; otherwise (Rejected: false, Password) — Password is non-null only when the archive
    // contains at least one encrypted entry AND passwordResolver successfully resolved it (T-F189).
    private async Task<(bool Rejected, ResolvedZipPassword? Password)> TryRejectUnsupportedOrEncryptedZipAsync(
        string archivePath, List<ArchiveError> errors, List<SkippedFile> skippedFiles,
        PasswordResolver passwordResolver)
    {
        if (!IsZipFile(archivePath))
        {
            string? reason = GetKnownArchiveReason(archivePath);
            if (reason is not null)
                skippedFiles.Add(new SkippedFile { Path = archivePath, Reason = reason });
            else
                errors.Add(new ArchiveError
                {
                    SourcePath = archivePath,
                    Message = "File is not a recognized archive format and cannot be extracted."
                });
            return (true, null);
        }

        if (IsEncryptedZip(archivePath))
        {
            ResolvedZipPassword? password = await ResolveArchivePasswordAsync(archivePath, passwordResolver, NameCodePages).ConfigureAwait(false);
            if (password is not null)
                return (false, password);

            errors.Add(new ArchiveError
            {
                SourcePath = archivePath,
                Message = "This archive is password-protected and cannot be extracted."
            });
            return (true, null);
        }

        return (false, null);
    }

    // T-F189: password is requested once per archive, here — before temp-dir creation or
    // destination planning, never lazily inside the per-entry extraction loop. Verifies the
    // candidate password against the FIRST encrypted entry only (a cheap, small read regardless
    // of that entry's real size) — good enough to decide "prompt again" vs "proceed"; a rare
    // per-entry-only failure later (a different password on a different entry, or corruption)
    // surfaces as a normal per-entry ArchiveError in the extraction loop instead.
    // T-F194: internal (not private) — AntivirusScanService resolves an archive's password through
    // this exact same verify-against-first-encrypted-entry path rather than a second copy.
    internal static async Task<ResolvedZipPassword?> ResolveArchivePasswordAsync(
        string archivePath, PasswordResolver passwordResolver, ZipNameCodePages codePages)
    {
        // T-F189 (advisor-caught): wraps the WHOLE method body, not just LocateAll — the verify
        // callback below also calls EncryptedZipEntryReader.TryOpen, which can throw
        // InvalidDataException for a structurally-odd entry (its own Zip64-sentinel guard,
        // corrupted extra field, etc.). Without this, that throw would escape ExtractAsync/
        // TestAsync entirely uncaught, violating "Archiver.Core services never throw to callers."
        // Falls back to today's unchanged "password-protected" rejection at the call site either way.
        try
        {
            using var fs = File.OpenRead(archivePath);
            var located = RawZipEntryLocator.LocateAll(fs);
            // T-F243: the smallest encrypted entry, so the full check below stays cheap.
            var probe = located.Where(e => e.GeneralPurposeEncryptedBit).MinBy(e => e.CompressedSize);
            if (probe is null)
                return null; // IsEncryptedZip said yes but nothing actually has the bit set — defensive, shouldn't happen

            string? text = await passwordResolver.ResolveAsync(
                Path.GetFileName(archivePath),
                PasswordPurpose.Decrypt,
                candidate => ChoosePasswordEncoding(fs, probe, candidate, codePages.Ansi) is not null).ConfigureAwait(false);
            if (text is null)
                return null;

            // A sticky "apply to remaining" answer skips verify; if it fits no encoding here, the
            // entries fail one by one, as they did before T-F244.
            Encoding encoding = ChoosePasswordEncoding(fs, probe, text, codePages.Ansi)
                ?? EncryptedZipEntryReader.PasswordEncodings(text, probe.CompressionMethod == 99, codePages.Ansi)[0];
            return new ResolvedZipPassword(text, encoding);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
        {
            // IsEncryptedZip's cheap first-local-header fast path can say "encrypted" for a file
            // that has no real central directory/EOCD at all (e.g. a truncated or hand-crafted
            // minimal ZIP) — LocateAll needs a real EOCD to run.
            return null;
        }
    }

    /// <summary>T-F244: a resolved password plus the one encoding its bytes are taken in for every
    /// entry of the archive.</summary>
    internal sealed record ResolvedZipPassword(string Text, Encoding Encoding);

    // T-F243 item 3 / T-F244 item 3: ZipCrypto's one-byte check accepts ~1 in 256 wrong passwords
    // — and ~1 in 256 wrong ENCODINGS of the right one. The encoding is therefore chosen once per
    // archive, here, by a full check of a small ZipCrypto probe entry (decrypted, CRC-32 checked),
    // never per entry; a colliding wrong password is rejected while the user can still be asked
    // again. A probe above the limit (compressed or declared size — a small entry can declare a
    // huge one) keeps the cheap check only; its later CRC failure stays a per-entry error.
    internal const long PasswordProbeFullCheckLimitBytes = 4L * 1024 * 1024;

    private static Encoding? ChoosePasswordEncoding(Stream zipStream, LocatedZipEntry probe, string password, Encoding ansi)
    {
        foreach (Encoding encoding in EncryptedZipEntryReader.PasswordEncodings(password, probe.CompressionMethod == 99, ansi))
        {
            if (EncryptedZipEntryReader.VerifyPassword(zipStream, probe, password, encoding) == EncryptedZipReadResult.Success
                && ProbeDecryptsIntact(zipStream, probe, password, encoding))
                return encoding;
        }
        return null;
    }

    private static bool ProbeDecryptsIntact(Stream zipStream, LocatedZipEntry probe, string password, Encoding encoding)
    {
        // AES: the 2-byte verifier decides (1 in 65,536); an HMAC failure after it means tampering,
        // which must stay "corrupted", never turn into "wrong password".
        if (probe.CompressionMethod == 99
            || probe.CompressedSize > PasswordProbeFullCheckLimitBytes || probe.UncompressedSize > PasswordProbeFullCheckLimitBytes)
            return true;

        var (result, content) = EncryptedZipEntryReader.TryOpen(zipStream, probe, password, encoding);
        if (result != EncryptedZipReadResult.Success)
            return result == EncryptedZipReadResult.UnsupportedCompressionMethod;
        try
        {
            using (content)
                content!.CopyTo(Stream.Null);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    // The three List sinks ExtractOneZipWithErrorMappingAsync writes into — bundled to cut S107's
    // parameter count. Named distinctly from ArchiveSingleSeparatePathAsync's own ArchiveResultSink
    // (ConcurrentBag-typed, for its parallel workers) since this one is plain List-typed.
    private sealed record ZipExtractResultSink(
        List<ArchiveError> Errors,
        List<string> CreatedFiles,
        List<SkippedFile> SkippedFiles,
        List<string> ConflictSkippedEntries);

    private async Task ExtractOneZipWithErrorMappingAsync(
        string archivePath, string destDir, ExtractOptions options, ConflictResolver conflictResolver,
        ResolvedZipPassword? password, ZipExtractResultSink sink, IProgress<ProgressReport>? archiveProgress, CancellationToken cancellationToken)
    {
        try
        {
            bool alreadyIsolated = options.Mode == ExtractMode.SeparateFolders;
            var context = new ZipExtractionContext(
                conflictResolver, sink.SkippedFiles, options.ConfirmCompressionBombExtraction, _policy.MotwMode, archiveProgress, sink.Errors,
                sink.ConflictSkippedEntries, NameCodePages, password, options.EliminateDuplicateRootFolder);
            var (actualDest, anyExtracted) = await Task.Run(async () =>
                await ExtractWithSmartFolderingAsync(archivePath, destDir, alreadyIsolated,
                    options.DestinationFolder, options.SelectedEntryPaths, context, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            // T-F87: an archive whose entries were all individually skipped (e.g. every entry
            // already exists at the destination with OnConflict=Skip) must not be reported as
            // CreatedFiles — MainViewModel uses this list to decide whether DeleteAfterOperation
            // may delete the source archive.
            if (anyExtracted)
                sink.CreatedFiles.Add(actualDest);
        }
        catch (IOException ex)
        {
            sink.Errors.Add(new ArchiveError
            {
                SourcePath = archivePath,
                Message = $"Cannot extract archive: {ex.Message}",
                Exception = ex
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            sink.Errors.Add(new ArchiveError
            {
                SourcePath = archivePath,
                Message = $"Access denied extracting archive: {ex.Message}",
                Exception = ex
            });
        }
        catch (InvalidDataException ex)
        {
            sink.Errors.Add(new ArchiveError
            {
                SourcePath = archivePath,
                Message = "File has ZIP signature but appears corrupted or incomplete.",
                Exception = ex
            });
        }
    }

    /// <inheritdoc/>
    public async Task<ArchiveResult> TestAsync(
        IReadOnlyList<string> archivePaths,
        IProgress<ProgressReport>? progress = null,
        Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ArchiveError>();
        var skippedFiles = new List<SkippedFile>();
        var passwordResolver = new PasswordResolver(resolvePasswordAsync, maxAttempts: 3);

        int total = archivePaths.Count;
        for (int i = 0; i < total; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            string archivePath = archivePaths[i];

            if (!IsZipFile(archivePath))
            {
                // T-F117: see ExtractAsync's identical branch — unrecognized bytes are a real
                // error, not a silent no-op, distinct from a known-but-unsupported format skip.
                string? reason = GetKnownArchiveReason(archivePath);
                if (reason is not null)
                    skippedFiles.Add(new SkippedFile { Path = archivePath, Reason = reason });
                else
                    errors.Add(new ArchiveError
                    {
                        SourcePath = archivePath,
                        Message = "File is not a recognized archive format and cannot be tested."
                    });
                progress?.Report(new ProgressReport { Percent = (i + 1) * 100 / total, BytesTransferred = 0, TotalBytes = 0 });
                continue;
            }

            ResolvedZipPassword? password = null;
            if (IsEncryptedZip(archivePath))
            {
                password = await ResolveArchivePasswordAsync(archivePath, passwordResolver, NameCodePages).ConfigureAwait(false);
                if (password is null)
                {
                    errors.Add(new ArchiveError
                    {
                        SourcePath = archivePath,
                        Message = "This archive is password-protected and cannot be tested."
                    });
                    progress?.Report(new ProgressReport { Percent = (i + 1) * 100 / total, BytesTransferred = 0, TotalBytes = 0 });
                    continue;
                }
            }

            try
            {
                await Task.Run(() => TestArchiveEntries(archivePath, password, NameCodePages, errors, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                errors.Add(new ArchiveError
                {
                    SourcePath = archivePath,
                    Message = $"Cannot read archive: {ex.Message}",
                    Exception = ex
                });
            }
            catch (InvalidDataException ex)
            {
                errors.Add(new ArchiveError
                {
                    SourcePath = archivePath,
                    Message = "File has ZIP signature but appears corrupted or incomplete.",
                    Exception = ex
                });
            }

            progress?.Report(new ProgressReport { Percent = (i + 1) * 100 / total, BytesTransferred = 0, TotalBytes = 0 });
        }

        return new ArchiveResult
        {
            Success = errors.Count == 0,
            Errors = errors,
            SkippedFiles = skippedFiles,
        };
    }

    /// <inheritdoc/>
    public async Task<ArchiveListResult> ListEntriesAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entries = await Task.Run(() =>
            {
                using var reader = ZipArchiveReader.Open(archivePath, NameCodePages);
                var archive = reader.Archive;

                // T-F189: only paid for an archive that actually has an encrypted entry (the
                // Archive Browser calls ListEntriesAsync on every navigation — see docs/DECISIONS.md's
                // T-F189 entry for why this stays gated behind the cheap IsEncryptedZip check
                // rather than always parsing the central directory a second time).
                Dictionary<ZipArchiveEntry, LocatedZipEntry>? encryptedEntryMap = null;
                if (IsEncryptedZip(archivePath))
                {
                    try
                    {
                        using var rawArchiveStream = File.OpenRead(archivePath);
                        var located = RawZipEntryLocator.LocateAll(rawArchiveStream);
                        if (located.Count == archive.Entries.Count)
                        {
                            encryptedEntryMap = new Dictionary<ZipArchiveEntry, LocatedZipEntry>(archive.Entries.Count);
                            for (int i = 0; i < archive.Entries.Count; i++)
                                encryptedEntryMap[archive.Entries[i]] = located[i];
                        }
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
                    {
                        // A Zip64-sized entry (or any structurally-odd one) can make LocateAll
                        // throw — see docs/DECISIONS.md's T-F189 entry. Listing the archive is
                        // still possible without the AE-2 Crc32=null refinement; failing this
                        // whole ListEntriesAsync call over it would be a real regression (Archive
                        // Browser could list this exact archive before T-F189).
                        encryptedEntryMap = null;
                    }
                }

                return reader.Entries.Select(named =>
                {
                    var e = named.Entry;
                    // AE-2 zeroes the header CRC-32 by design (HMAC is the sole authority) — report
                    // null rather than a misleading 0, consistent with ArchiveEntryInfo.Crc32's
                    // existing nullable convention (0 is itself a legitimate CRC-32 for other entries).
                    bool isAe2WithZeroedCrc = encryptedEntryMap is { } map
                        && map.TryGetValue(e, out var located)
                        && located.CompressionMethod == 99
                        && located.AeVersion == 2;

                    bool isDirectory = named.FullName.EndsWith('/');
                    return new ArchiveEntryInfo
                    {
                        Path = named.FullName.TrimEnd('/'),
                        Size = e.Length,
                        CompressedSize = e.CompressedLength,
                        Modified = e.LastWriteTime.DateTime,
                        IsDirectory = isDirectory,
                        Crc32 = isDirectory || isAe2WithZeroedCrc ? null : e.Crc32,
                    };
                }).ToList();
            }, cancellationToken).ConfigureAwait(false);

            return new ArchiveListResult { Success = true, Entries = entries };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new ArchiveListResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    // Reads every entry's decompressed bytes and compares a freshly computed CRC-32 against
    // the value declared in the entry's header — System.IO.Compression never validates this
    // itself on read, so a bit-flipped-but-structurally-valid entry would otherwise extract
    // "successfully" with silently wrong content. T-F189: password is non-null only when this
    // archive contains at least one encrypted entry and a password was already resolved once,
    // upfront, in TestAsync — same "once per archive" rule as ExtractAsync.
    private static void TestArchiveEntries(
        string archivePath, ResolvedZipPassword? password, ZipNameCodePages codePages, List<ArchiveError> errors,
        CancellationToken cancellationToken)
    {
        using var reader = ZipArchiveReader.Open(archivePath, codePages);
        var archive = reader.Archive;

        Dictionary<ZipArchiveEntry, LocatedZipEntry>? encryptedEntryMap = null;
        using var rawArchiveStream = password is not null ? File.OpenRead(archivePath) : null;
        if (password is not null)
        {
            var located = RawZipEntryLocator.LocateAll(rawArchiveStream!);
            if (located.Count == archive.Entries.Count)
            {
                encryptedEntryMap = new Dictionary<ZipArchiveEntry, LocatedZipEntry>(archive.Entries.Count);
                for (int i = 0; i < archive.Entries.Count; i++)
                    encryptedEntryMap[archive.Entries[i]] = located[i];
            }
        }

        foreach (var named in reader.Entries)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var entry = named.Entry;
            if (named.FullName.EndsWith('/'))
                continue; // directory entry — no data to verify

            if (named.CollidesAfterDecoding)
            {
                errors.Add(new ArchiveError { SourcePath = archivePath, Message = CollisionMessage(named.FullName) });
                continue;
            }

            if (encryptedEntryMap is { } map && map.TryGetValue(entry, out var located2) && located2.GeneralPurposeEncryptedBit)
            {
                TestEncryptedEntry(archivePath, named.FullName, located2, rawArchiveStream!, password!, errors);
                continue;
            }

            // T-F246/T-F231: the same wrapper extraction uses, so Test and Extract agree (a stored
            // entry longer than declared fails both).
            try
            {
                using var verified = new VerifyingReadStream(entry.Open(), entry.Length, entry.Crc32);
                verified.CopyTo(Stream.Null);
            }
            catch (InvalidDataException ex)
            {
                errors.Add(new ArchiveError
                {
                    SourcePath = archivePath,
                    Message = $"Entry '{named.FullName}': {ex.Message}",
                    Exception = ex
                });
            }
        }
    }

    // T-F189: reuses the exact same EncryptedZipEntryReader/VerifyingReadStream machinery as
    // extraction — draining the stream to Stream.Null triggers the lazy CRC-32 check for
    // ZipCrypto/AE-1 and the declared-size cap for all (throws InvalidDataException, caught here so one bad entry
    // doesn't abort testing the rest of the archive). AE-2 has no header CRC to check (zeroed by
    // design) — HMAC, already verified inside TryOpen before this method is even reached, is the
    // whole story there; draining just runs the decompression harmlessly.
    private static void TestEncryptedEntry(
        string archivePath, string entryName, LocatedZipEntry located, Stream rawArchiveStream,
        ResolvedZipPassword password, List<ArchiveError> errors)
    {
        var (result, stream) = EncryptedZipEntryReader.TryOpen(rawArchiveStream, located, password.Text, password.Encoding);
        switch (result)
        {
            case EncryptedZipReadResult.WrongPassword:
                errors.Add(new ArchiveError
                {
                    SourcePath = archivePath,
                    Message = $"Entry '{entryName}' could not be decrypted: wrong password."
                });
                return;
            case EncryptedZipReadResult.Corrupted:
                errors.Add(new ArchiveError
                {
                    SourcePath = archivePath,
                    Message = $"Entry '{entryName}' could not be decrypted: authentication failed (corrupted or tampered)."
                });
                return;
            case EncryptedZipReadResult.UnsupportedCompressionMethod:
                errors.Add(new ArchiveError
                {
                    SourcePath = archivePath,
                    Message = $"Entry '{entryName}' uses an unsupported compression method under encryption."
                });
                return;
        }

        try
        {
            using (stream)
                stream!.CopyTo(Stream.Null);
        }
        catch (InvalidDataException ex)
        {
            errors.Add(new ArchiveError
            {
                SourcePath = archivePath,
                Message = $"Entry '{entryName}': {ex.Message}",
                Exception = ex
            });
        }
    }

    // T-F189: thin wrapper around ExtractWithSmartFolderingCoreAsync — owns the raw FileStream
    // used to positionally pair archive.Entries with RawZipEntryLocator.LocateAll's output, and
    // guarantees it's disposed even if the core method throws.
    private static async Task<(string ActualDest, bool AnyExtracted)> ExtractWithSmartFolderingAsync(
        string archivePath,
        string destDir,
        bool alreadyIsolated,
        string unisolatedDestDir,
        IReadOnlyList<string>? selectedEntryPaths,
        ZipExtractionContext context,
        CancellationToken cancellationToken)
    {
        using var reader = ZipArchiveReader.Open(archivePath, context.NameCodePages);
        var archive = reader.Archive;

        // T-F189: only paid for an archive that actually has a resolved password (i.e. contains
        // at least one encrypted entry) — a plain archive incurs zero extra parsing here. Built
        // positionally (see RawZipEntryLocator.LocateAll's own doc comment for why), paired 1:1
        // with archive.Entries — both walk the same central directory in the same order.
        Dictionary<ZipArchiveEntry, LocatedZipEntry>? encryptedEntryMap = null;
        Stream? rawArchiveStream = null;
        if (context.Password is not null)
        {
            rawArchiveStream = File.OpenRead(archivePath);
            var located = RawZipEntryLocator.LocateAll(rawArchiveStream);
            if (located.Count != archive.Entries.Count)
                throw new InvalidDataException(
                    "ZIP central directory entry count mismatch while resolving encrypted entries.");

            encryptedEntryMap = new Dictionary<ZipArchiveEntry, LocatedZipEntry>(archive.Entries.Count);
            for (int i = 0; i < archive.Entries.Count; i++)
                encryptedEntryMap[archive.Entries[i]] = located[i];
        }

        try
        {
            return await ExtractWithSmartFolderingCoreAsync(
                reader.Entries, archivePath, destDir, alreadyIsolated, unisolatedDestDir, selectedEntryPaths,
                context, encryptedEntryMap, rawArchiveStream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (rawArchiveStream is not null)
                await rawArchiveStream.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Already split via ZipExtractionContext/ExtractionPlan/TryExtractSingleEntryAsync (T-F147); // NOSONAR: prose, not commented-out code (S125 false positive)
    // the residual complexity below is the smart-foldering decision + whole-archive compression-
    // bomb gate, both order-sensitive and security-relevant (T-F94/T-F105 history) — further
    // splitting risks separating checks whose safety currently reads directly off this one method
    // body. Kept paired 1:1 with TarSandboxedService.ExtractSingleArchiveAsync (T-F118).
    private static async Task<(string ActualDest, bool AnyExtracted)> ExtractWithSmartFolderingCoreAsync( // NOSONAR: S3776 — see comment above
        IReadOnlyList<NamedZipEntry> archiveEntries,
        string archivePath,
        string destDir,
        bool alreadyIsolated,
        string unisolatedDestDir,
        IReadOnlyList<string>? selectedEntryPaths,
        ZipExtractionContext context,
        Dictionary<ZipArchiveEntry, LocatedZipEntry>? encryptedEntryMap,
        Stream? rawArchiveStream,
        CancellationToken cancellationToken)
    {
        List<SkippedFile> skippedFiles = context.SkippedFiles;
        Func<CompressionBombWarning, Task<bool>>? confirmCompressionBombExtraction = context.ConfirmCompressionBombExtraction;

        var allFileEntries = archiveEntries
            .Where(e => !e.FullName.EndsWith('/'))
            .ToList();
        // T-F197: folder entries ("name/") are extracted too — an empty folder has nothing else
        // that would bring it back.
        var allEntries = archiveEntries.ToList();

        // T-F05: restrict to just the selected entries (plus anything nested under a selected
        // folder path) before any of the smart-foldering logic below runs. O(entries × selected)
        // is fine here — selections are UI-driven (a user checking boxes), realistically dozens of
        // rows, not thousands, even against a 65,000-entry archive. The compression-bomb check
        // below deliberately still evaluates allFileEntries (the whole archive), not this subset —
        // see DECISIONS.md's T-F05 entry for why (conservative: may over-warn, never under-warns).
        bool isSelectedSubset = selectedEntryPaths is { Count: > 0 };
        var entries = allEntries;
        if (isSelectedSubset)
        {
            var selectedSet = new HashSet<string>(selectedEntryPaths!, StringComparer.Ordinal);
            entries = allEntries
                .Where(e => selectedSet.Contains(e.FullName)
                         || selectedSet.Any(s => e.FullName.StartsWith(s + "/", StringComparison.Ordinal)))
                .ToList();
        }

        if (entries.Count == 0)
        {
            Directory.CreateDirectory(destDir);
            return (destDir, true);
        }

        // A selected subset has no single meaningful "root" to collapse — it may span multiple
        // top-level folders/files depending on what the user checked. Skip the whole-archive
        // smart-foldering decision entirely and always extract straight into destDir.
        // T-F197: folder entries count as roots too ("a.txt" + "empty/" is two roots).
        bool isSingleRootFolder = !isSelectedSubset
            && entries.All(e => e.FullName.Contains('/'))
            && entries
                .Select(e => e.FullName[..e.FullName.IndexOf('/')])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 1;

        bool isSingleRootFile = !isSelectedSubset
            && entries.Count == 1 && !entries[0].FullName.Contains('/');

        // T-F157: the actualDest/StripRootPrefix decision (T-F154's single-file unwrap, T-F156's
        // multi-root-in-SingleFolder-mode fix, and the pre-existing isSingleRootFolder strip) now
        // lives in ExtractionDestinationPlanner, shared with TarSandboxedService.
        // ExtractSingleArchiveAsync instead of hand-kept-in-sync per T-F118's own comment — see
        // DECISIONS.md's T-F157 entry.
        var rootShape = ExtractionDestinationPlanner.Classify(isSelectedSubset, isSingleRootFolder, isSingleRootFile);
        bool rootDuplicatesArchiveName = context.EliminateDuplicateRootFolder && isSingleRootFolder
            && ExtractionDestinationPlanner.RootDuplicatesArchiveName(
                entries[0].FullName[..entries[0].FullName.IndexOf('/')], archivePath);
        var (actualDest, stripRootPrefix) = ExtractionDestinationPlanner.Resolve(
            alreadyIsolated, rootShape, destDir, unisolatedDestDir, rootDuplicatesArchiveName);

        // T-F94: whole-archive compression-ratio check, run BEFORE tempDest is created so a
        // declined/blocked bomb leaves nothing to clean up. Deliberately whole-archive rather
        // than the old per-entry model (see DECISIONS.md's T-F94 entry) — matches
        // TarProcessService's model and allows exactly one confirmation per archive. T-F05: uses
        // allFileEntries (not the filtered subset) even when extracting only a selection — see
        // DECISIONS.md's T-F05 entry for why this stays conservative rather than narrowed.
        long declaredUncompressedSize = allFileEntries.Where(e => e.Entry.Length > 0).Sum(e => e.Entry.Length);
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
                Reason = $"Suspicious compression ratio ({ratio}:1, {declaredUncompressedSize:N0} bytes declared). " +
                         "Extraction was declined as a precaution against ZIP bombs."
            });
            return (destDir, false);
        }

        // T-F227: a fresh, uniquely named staging folder owned by this run — never a fixed name
        // that could be a user's folder. Rooted in the caller's destination folder: it always
        // exists and shares the volume with actualDest, so the commit is a rename.
        using var staging = ExtractionStaging.Create(unisolatedDestDir);
        string tempDest = staging.Path;
        string fullTempDest = staging.FullPathWithSeparator;

        long totalUncompressedBytes = declaredUncompressedSize;
        long bytesRead = 0;
        int extractedCount = 0;

        // T-F30: ZIP format allows two entries with the identical name, and
        // System.IO.Compression does not reject them on read. The existing conflict check
        // below only looks at whether finalFilePath already exists in the FINAL destination —
        // it never sees an earlier duplicate entry from THIS SAME run, since nothing is
        // committed to the final destination until after the whole loop finishes. Tracking
        // claimed paths in memory closes that gap; without it, a duplicate entry would
        // silently overwrite the first one's file in tempDest.
        var claimedFinalPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var plan = new ExtractionPlan(tempDest, fullTempDest, actualDest, stripRootPrefix, totalUncompressedBytes, claimedFinalPaths,
            encryptedEntryMap, rawArchiveStream);

        // T-F161: `staging` is disposed on ANY exit, including a failure or cancellation partway
        // through — a leftover staging folder never stays on a real destination.
        foreach (var entry in entries)
        {
            // T-F260: throw — a `break` here committed the entries extracted so far as if the
            // archive were finished.
            cancellationToken.ThrowIfCancellationRequested();

            var (extracted, bytesConsumed) = await TryExtractSingleEntryAsync(
                entry, archivePath, bytesRead, plan, context, cancellationToken)
                .ConfigureAwait(false);

            if (extracted)
                extractedCount++;
            bytesRead += bytesConsumed;
        }

        foreach (string relativePath in staging.CommitInto(actualDest))
        {
            context.Errors.Add(new ArchiveError
            {
                SourcePath = archivePath,
                Message = $"Cannot write '{relativePath}': destination file is locked by another process."
            });
        }

        // T-F87: every entry was individually skipped (conflict/ADS/reserved name/reparse point/
        // zip bomb) — nothing was actually extracted, so the caller must not count this archive
        // as CreatedFiles (that list gates whether DeleteAfterOperation may delete the source).
        if (extractedCount == 0)
        {
            skippedFiles.Add(new SkippedFile
            {
                Path = archivePath,
                Reason = "No entries were extracted from this archive — every entry was skipped."
            });
            return (actualDest, false);
        }

        return (actualDest, true);
    }

    // The plumbing every entry of an ExtractWithSmartFolderingAsync call shares, cut from that
    // method's own parameter list to fix S107 — mirrors TarSandboxedService's identically-shaped
    // context for ExtractSingleArchiveAsync (T-F118: the two methods are deliberately kept
    // algorithmically identical).
    internal sealed record ZipExtractionContext(
        ConflictResolver ConflictResolver,
        List<SkippedFile> SkippedFiles,
        Func<CompressionBombWarning, Task<bool>>? ConfirmCompressionBombExtraction,
        MotwMode MotwMode,
        IProgress<ProgressReport>? Progress,
        // T-F170: a per-item failure to commit a file into actualDest (destination locked by
        // another process) — distinct from SkippedFiles (a deliberate, non-lossy decision like
        // Skip/ADS/reparse-point) since data the user asked for genuinely failed to arrive.
        List<ArchiveError> Errors,
        // T-F260: entries skipped because they already exist at the destination (see ExtractAsync).
        List<string> ConflictSkippedEntries,
        // T-F234: see ZipArchiveService.NameCodePages.
        ZipNameCodePages NameCodePages,
        // T-F189: non-null only when this archive contains at least one encrypted entry and a
        // password was already resolved once, upfront, in TryRejectUnsupportedOrEncryptedZipAsync.
        ResolvedZipPassword? Password = null,
        // T-F205: ExtractOptions.EliminateDuplicateRootFolder.
        bool EliminateDuplicateRootFolder = false);

    // The per-call setup ExtractWithSmartFolderingAsync computes once and every entry of its loop
    // reads unchanged — cut into its own type alongside ZipExtractionContext so
    // TryExtractSingleEntryAsync's own parameter count stays under S107's threshold.
    private sealed record ExtractionPlan(
        string TempDest,
        string FullTempDest,
        string ActualDest,
        bool StripRootPrefix,
        long TotalUncompressedBytes,
        HashSet<string> ClaimedFinalPaths,
        // T-F189: both null unless context.Password is set — see ExtractWithSmartFolderingAsync.
        Dictionary<ZipArchiveEntry, LocatedZipEntry>? EncryptedEntryMap = null,
        Stream? RawArchiveStream = null);

    // One entry of ExtractWithSmartFolderingAsync's loop — every one of the 6 skip-gates below is
    // already commented with its own T-Fxx tag and is self-contained. Returns whether the entry
    // was actually written, and how many bytes to advance the caller's running bytesRead by
    // (always entry.Length, on every path, matching the original inline loop's per-branch
    // `bytesRead += entry.Length`) — kept as an explicit return rather than a ref/out parameter so
    // the caller's accumulation stays visibly in its own hands.
    private static async Task<(bool Extracted, long BytesConsumed)> TryExtractSingleEntryAsync(
        NamedZipEntry named, string archivePath, long bytesReadSoFar, ExtractionPlan plan,
        ZipExtractionContext context, CancellationToken cancellationToken)
    {
        // T-F234: two different raw names that decode to one name would overwrite each other.
        if (named.CollidesAfterDecoding)
        {
            context.Errors.Add(new ArchiveError { SourcePath = archivePath, Message = CollisionMessage(named.FullName) });
            return (false, named.Entry.Length);
        }

        // T-F228: before the name checks — "C:/x" must read as unsafe, not as an ADS name.
        if (ArchiveEntrySecurity.HasUnsafePath(named.FullName))
        {
            context.Errors.Add(new ArchiveError
            {
                SourcePath = archivePath,
                Message = $"Entry '{named.FullName}' has an unsafe path and was not extracted."
            });
            return (false, named.Entry.Length);
        }

        string relativePath = named.FullName.Replace('/', Path.DirectorySeparatorChar);

        bool isFolder = named.FullName.EndsWith('/');

        if (plan.StripRootPrefix)
        {
            var sep = relativePath.IndexOf(Path.DirectorySeparatorChar);
            relativePath = relativePath[(sep + 1)..];
            // The stripped root folder itself: actualDest stands in for it.
            if (string.IsNullOrEmpty(relativePath))
                return (isFolder && !Directory.Exists(plan.ActualDest), named.Entry.Length);
        }

        // T-F38/T-F39: Reject ADS-marked, reserved-name, or control-character entry names
        // (a folder entry's trailing '/' would hide its last segment from the reserved-name check)
        string? nameRejectionReason = GetEntryNameRejectionReason(named.FullName.TrimEnd('/'));
        if (nameRejectionReason != null)
        {
            context.SkippedFiles.Add(new SkippedFile { Path = named.FullName, Reason = nameRejectionReason });
            return (false, named.Entry.Length);
        }

        try
        {
            if (isFolder)
                return (TryCreateFolderEntry(named, relativePath, plan, context), named.Entry.Length);
            return await WriteEntryAsync(named, relativePath, archivePath, bytesReadSoFar, plan, context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidDataException
                                   || (ex is IOException io && !IsDiskFull(io)))
        {
            // T-F230: an entry Windows cannot write (a name legal elsewhere, e.g. "What?.txt", or a
            // locked/denied path) fails only itself — as does corrupt content (T-F246: CRC-32,
            // T-F231: longer than declared). The message names the destination, never the
            // internal staging folder.
            string message = ex.Message.Replace(
                Path.TrimEndingDirectorySeparator(plan.FullTempDest),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.ActualDest)),
                StringComparison.OrdinalIgnoreCase);
            context.Errors.Add(new ArchiveError
            {
                SourcePath = archivePath,
                Message = $"Cannot extract '{named.FullName}': {message}",
                Exception = ex,
            });
            return (false, named.Entry.Length);
        }
    }

    // T-F197: a folder entry gets the same containment and reparse-point checks as a file entry,
    // then is created in staging; the commit carries it across even when it stays empty. Counts
    // as extracted only when the folder is new at the destination — otherwise re-extracting with
    // Skip would no longer report "nothing extracted" (T-F87).
    private static bool TryCreateFolderEntry(
        NamedZipEntry named, string relativePath, ExtractionPlan plan, ZipExtractionContext context)
    {
        string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(plan.TempDest, relativePath)));

        if (!folder.StartsWith(plan.FullTempDest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"ZIP entry '{named.FullName}' would extract outside destination directory.");

        if (ArchiveEntrySecurity.PathContainsReparsePoint(folder, plan.FullTempDest))
        {
            context.SkippedFiles.Add(new SkippedFile
            {
                Path = named.FullName,
                Reason = "Entry path traverses a reparse point (symlink or junction) and was skipped."
            });
            return false;
        }

        Directory.CreateDirectory(folder);
        return !Directory.Exists(Path.Combine(plan.ActualDest, relativePath));
    }

    // The write half of one entry: staging path, reparse-point check, conflict resolution, copy.
    // Every I/O failure in here is per entry (see the caller's catch).
    private static async Task<(bool Extracted, long BytesConsumed)> WriteEntryAsync(
        NamedZipEntry named, string relativePath, string archivePath, long bytesReadSoFar, ExtractionPlan plan,
        ZipExtractionContext context, CancellationToken cancellationToken)
    {
        string fullTempDest = plan.FullTempDest;
        string actualDest = plan.ActualDest;
        HashSet<string> claimedFinalPaths = plan.ClaimedFinalPaths;

        string destFilePath = Path.GetFullPath(Path.Combine(plan.TempDest, relativePath));

        if (!destFilePath.StartsWith(fullTempDest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"ZIP entry '{named.FullName}' would extract outside destination directory.");

        Directory.CreateDirectory(Path.GetDirectoryName(destFilePath)!);

        // T-F37: Reject entries whose path traverses a reparse point (symlink/junction)
        if (ArchiveEntrySecurity.PathContainsReparsePoint(destFilePath, fullTempDest))
        {
            context.SkippedFiles.Add(new SkippedFile
            {
                Path = named.FullName,
                Reason = "Entry path traverses a reparse point (symlink or junction) and was skipped."
            });
            return (false, named.Entry.Length);
        }

        // T-F94: per-entry compression-ratio check removed — superseded by the whole-archive
        // check before tempDest was created.

        // Conflict check against the final destination, not the temp dir — plus T-F30: against
        // every finalFilePath already claimed earlier in this same run, which catches a
        // duplicate entry name inside this archive that File.Exists alone can't see (see
        // claimedFinalPaths' own comment at its declaration).
        // T-F228: from the path as it actually resolved inside staging, so the conflict check
        // and the commit always talk about the same file.
        string finalFilePath = Path.GetFullPath(Path.Combine(actualDest, Path.GetRelativePath(fullTempDest, destFilePath)));
        if (File.Exists(finalFilePath) || claimedFinalPaths.Contains(finalFilePath))
        {
            ConflictBehavior resolvedConflict = await context.ConflictResolver.ResolveAsync(finalFilePath).ConfigureAwait(false);
            if (resolvedConflict == ConflictBehavior.Skip)
            {
                context.ConflictSkippedEntries.Add(relativePath);
                return (false, named.Entry.Length);
            }
            if (resolvedConflict == ConflictBehavior.Rename)
            {
                string uniqueFinal = GetUniqueFilePath(finalFilePath, claimedFinalPaths);
                destFilePath = Path.Combine(Path.GetDirectoryName(destFilePath)!, Path.GetFileName(uniqueFinal));
                finalFilePath = uniqueFinal;
            }
        }
        claimedFinalPaths.Add(finalFilePath);

        // T-F189: entry names are never encrypted by the ZIP format itself, so every check above
        // (ADS/reserved-name, traversal, reparse-point, conflict) already ran unmodified before
        // this point — decryption plugs in only here, exactly where entry.Open() used to be
        // called directly. See the ZIP Password Support design's "no second extraction path"
        // invariant in docs/TASKS.md's T-F189 entry.
        var (opened, entryStream, decryptErrorMessage) = OpenEntryContentStream(named, plan, context);
        if (!opened)
        {
            context.Errors.Add(new ArchiveError { SourcePath = archivePath, Message = decryptErrorMessage! });
            return (false, named.Entry.Length);
        }

        await CopyEntryToDestinationAsync(named, entryStream!, destFilePath, archivePath, plan.TotalUncompressedBytes,
            bytesReadSoFar, context, cancellationToken).ConfigureAwait(false);

        return (true, named.Entry.Length);
    }

    // T-F189: the encrypted-ZIP plug-in point — everything else about extraction (destination
    // planning, conflict resolution, security checks, temp-dir/atomic-commit) stays exactly as it
    // was, unaware this entry was ever encrypted. Returns entry.Open() itself unchanged for the
    // overwhelming majority of entries (plan.EncryptedEntryMap is null whenever the archive has no
    // encrypted entries at all).
    private static (bool Success, Stream? Stream, string? ErrorMessage) OpenEntryContentStream(
        NamedZipEntry named, ExtractionPlan plan, ZipExtractionContext context)
    {
        if (plan.EncryptedEntryMap is { } map
            && map.TryGetValue(named.Entry, out var located)
            && located.GeneralPurposeEncryptedBit)
        {
            var (result, stream) = EncryptedZipEntryReader.TryOpen(plan.RawArchiveStream!, located, context.Password!.Text, context.Password.Encoding);
            return result switch
            {
                EncryptedZipReadResult.Success => (true, stream, null),
                EncryptedZipReadResult.WrongPassword =>
                    (false, null, $"Entry '{named.FullName}' could not be decrypted: wrong password."),
                EncryptedZipReadResult.UnsupportedCompressionMethod =>
                    (false, null, $"Entry '{named.FullName}' uses an unsupported compression method under encryption."),
                _ => (false, null,
                    $"Entry '{named.FullName}' could not be decrypted: authentication failed (corrupted or tampered)."),
            };
        }

        // T-F246: .NET does not check CRC-32 on read (and silently truncates a deflated entry to
        // its declared size) — without this a corrupted entry is written and reported as success.
        return (true, new VerifyingReadStream(named.Entry.Open(), named.Entry.Length, named.Entry.Crc32), null);
    }

    private static string CollisionMessage(string entryName) =>
        $"Entry '{entryName}' has the same name as another entry once decoded; it is not extracted, since one would overwrite the other.";

    // T-F230: a full disk fails every remaining entry the same way — one archive-level error, not
    // one per entry.
    internal static bool IsDiskFull(IOException ex) =>
        ex.HResult is unchecked((int)0x80070070) or unchecked((int)0x80070027); // ERROR_DISK_FULL, ERROR_HANDLE_DISK_FULL

    private static string? GetEntryNameRejectionReason(string entryFullName)
    {
        if (ArchiveEntrySecurity.HasAlternateDataStreamMarker(entryFullName))
            return "Alternate Data Stream entry rejected for security.";
        if (ArchiveEntrySecurity.HasReservedName(entryFullName))
            return "Entry name matches a reserved Windows device name and was skipped.";
        if (ArchiveEntrySecurity.HasControlCharacters(entryFullName))
            return "Entry name contains control characters and was skipped.";
        return null;
    }

    // T-F189: entryStream is already open — either entry.Open() (the overwhelming majority) or a
    // decrypting stream from OpenEntryContentStream — so ProgressStream/CopyToAsync wrap it
    // identically either way. This is what gives an encrypted entry real byte-accurate T-F16
    // progress with no special-casing (see docs/DECISIONS.md's T-F189 streaming design point).
    private static async Task CopyEntryToDestinationAsync(
        NamedZipEntry named, Stream entryStream, string destFilePath, string archivePath, long totalUncompressedBytes,
        long bytesReadSoFar, ZipExtractionContext context, CancellationToken cancellationToken)
    {
        try
        {
            await CopyEntryStreamAsync(named, entryStream, destFilePath, totalUncompressedBytes, bytesReadSoFar, context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // T-F230: a half-written file must never be committed.
            TryDeleteBestEffort(destFilePath);
            throw;
        }

        // T-F45: Propagate Zone.Identifier ADS from archive to extracted file
        ArchiveEntrySecurity.TryPropagateMotw(archivePath, destFilePath, context.MotwMode);
    }

    private static async Task CopyEntryStreamAsync(
        NamedZipEntry named, Stream entryStream, string destFilePath, long totalUncompressedBytes,
        long bytesReadSoFar, ZipExtractionContext context, CancellationToken cancellationToken)
    {
        if (context.Progress != null && totalUncompressedBytes > 0)
        {
            await using var ps = new ProgressStream(entryStream, totalUncompressedBytes, bytesReadSoFar, context.Progress, Path.GetFileName(named.FullName));
            using var fileStream = new FileStream(
                destFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: CopyBufferSize,
                useAsync: true);
            await ps.CopyToAsync(fileStream, CopyBufferSize, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            using (entryStream)
            {
                using var fileStream = new FileStream(
                    destFilePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: CopyBufferSize,
                    useAsync: true);
                await entryStream.CopyToAsync(fileStream, CopyBufferSize, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // The progress-tracking trio AddEntryFromFileAsync's 3 call sites all pass explicitly —
    // bundled to cut S107's parameter count.
    private sealed record EntryWriteProgress(long TotalBytes, long StartOffset, IProgress<ProgressReport>? Progress);

    private static async Task AddEntryFromFileAsync(
        ZipArchive archive,
        string sourcePath,
        string entryName,
        CompressionLevel compressionLevel,
        EntryWriteProgress progressInfo,
        CancellationToken cancellationToken)
    {
        // T-F21: Open the source file BEFORE creating the archive entry.
        // If the file has been deleted or locked since it was discovered by
        // Directory.EnumerateFiles, the IOException propagates to the caller
        // without leaving an orphaned 0-byte entry in the archive.
        using var fileStream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: FileStreamBufferSize,
            useAsync: false);
        var entry = archive.CreateEntry(entryName, compressionLevel);
        // T-F31: Pin LastWriteTime to the source file's actual timestamp so that two
        // archive runs over identical inputs produce byte-identical ZIPs.
        // Without this, ZipArchiveEntry defaults to DateTimeOffset.UtcNow (creation time),
        // which makes the archive non-deterministic.
        try { entry.LastWriteTime = File.GetLastWriteTime(sourcePath); } catch { /* best-effort */ }

        if (progressInfo.Progress != null && progressInfo.TotalBytes > 0)
        {
            var entryStream = entry.Open();
            await using var ps = new ProgressStream(entryStream, progressInfo.TotalBytes, progressInfo.StartOffset, progressInfo.Progress, entryName);
            await fileStream.CopyToAsync(ps, CopyBufferSize, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            using var entryStream = entry.Open();
            await fileStream.CopyToAsync(entryStream, CopyBufferSize, cancellationToken).ConfigureAwait(false);
        }
    }

    // T-F23: Manual recursive traversal so we can inspect FileAttributes before entering each
    // directory. Returns the updated startOffset for progress tracking.
    // T-F21: errors list receives per-file ArchiveErrors so that a single inaccessible file
    // does not abort the rest of the directory — operation continues for all remaining files.
    // T-F75: rootDir is the original top-level directory being archived and stays FIXED across
    // every recursion level — relative paths (and therefore ZIP entry names) are always computed
    // against it. Before this fix, relative paths were computed against each recursion level's
    // own immediate parent, so every level below the first lost its accumulated prefix (e.g.
    // "notes/sub/file.txt" became just "sub/file.txt" — silently wrong, and deep enough nesting
    // could collide two distinct source files into the same entry name). See DECISIONS.md.
    // T-F75's fixed rootDir/entryPrefix plus the report-sink/progress plumbing every recursive
    // call passes down unchanged — bundled so the recursive self-call (and both top-level call
    // sites) don't repeat an 11-parameter list. sourceDir/startOffset are the only things that
    // vary per recursion level, so they stay separate params on AddDirectoryToArchiveAsync itself.
    private sealed record DirectoryArchiveContext(
        string RootDir,
        string EntryPrefix,
        CompressionLevel CompressionLevel,
        Action<SkippedFile> ReportSkipped,
        Action<ArchiveError> ReportError,
        long TotalBytes,
        IProgress<ProgressReport>? Progress);

    // T-F23: Manual recursive traversal so we can inspect FileAttributes before entering each
    // directory. Returns the updated startOffset for progress tracking.
    // T-F21: errors list receives per-file ArchiveErrors so that a single inaccessible file
    // does not abort the rest of the directory — operation continues for all remaining files.
    // T-F75: rootDir is the original top-level directory being archived and stays FIXED across
    // every recursion level — relative paths (and therefore ZIP entry names) are always computed
    // against it. Before this fix, relative paths were computed against each recursion level's
    // own immediate parent, so every level below the first lost its accumulated prefix (e.g.
    // "notes/sub/file.txt" became just "sub/file.txt" — silently wrong, and deep enough nesting
    // could collide two distinct source files into the same entry name). See DECISIONS.md.
    private static async Task<long> AddDirectoryToArchiveAsync(
        ZipArchive archive,
        string sourceDir,
        DirectoryArchiveContext context,
        long startOffset = 0,
        CancellationToken cancellationToken = default)
    {
        // T-F66: A directory with no files and no subdirectories writes no entry at all
        // otherwise — for a top-level empty folder, that leaves HasTempEntries() false and
        // the whole archive gets silently discarded (ArchiveAsync's "no entries" cleanup),
        // so archiving an empty folder produced no output file. Writing an explicit
        // directory entry preserves the folder and keeps the archive from being discarded.
        if (!Directory.EnumerateFileSystemEntries(sourceDir).Any())
        {
            string relativeDir = Path.GetRelativePath(context.RootDir, sourceDir);
            string emptyEntryName = relativeDir == "."
                ? context.EntryPrefix + "/"
                : context.EntryPrefix + "/" + relativeDir.Replace('\\', '/') + "/";
            if (ZipEntryWriter.NameFitsHeader(emptyEntryName))
                archive.CreateEntry(emptyEntryName);
            else
                context.ReportError(new ArchiveError { SourcePath = sourceDir, Message = ZipEntryWriter.NameTooLongMessage(emptyEntryName) });
            return startOffset;
        }

        // T-F32: Sort files and subdirectories for deterministic traversal order.
        // Directory.EnumerateFiles/EnumerateDirectories return items in filesystem order,
        // which is non-deterministic across runs and filesystems.
        startOffset = await AddFilesInDirectoryAsync(archive, sourceDir, context, startOffset, cancellationToken)
            .ConfigureAwait(false);

        // Recurse into subdirectories, skipping junctions and directory symlinks
        startOffset = await AddSubdirectoriesAsync(archive, sourceDir, context, startOffset, cancellationToken)
            .ConfigureAwait(false);

        return startOffset;
    }

    private static async Task<long> AddFilesInDirectoryAsync(
        ZipArchive archive, string sourceDir, DirectoryArchiveContext context, long startOffset, CancellationToken cancellationToken)
    {
        foreach (string filePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // T-F23: Skip file-level symlinks (reparse points)
            if (ArchiveEntrySecurity.IsReparsePoint(filePath))
            {
                context.ReportSkipped(new SkippedFile
                {
                    Path = filePath,
                    Reason = "Symbolic links and reparse points are not archived."
                });
                continue;
            }

            long fileSize = 0;
            try { fileSize = new FileInfo(filePath).Length; } catch { /* best-effort */ }

            string relativePath = Path.GetRelativePath(context.RootDir, filePath).Replace('\\', '/');
            string entryName = context.EntryPrefix + "/" + relativePath;

            // T-F243 item 6: ZipArchive.CreateEntry throws on a name over 65,535 UTF-8 bytes.
            if (!ZipEntryWriter.NameFitsHeader(entryName))
            {
                context.ReportError(new ArchiveError { SourcePath = filePath, Message = ZipEntryWriter.NameTooLongMessage(entryName) });
                startOffset += fileSize;
                continue;
            }

            // T-F21: Catch per-file IO failures. A file may be deleted or locked between
            // Directory.EnumerateFiles discovery and the FileStream.Open inside
            // AddEntryFromFileAsync — both FileNotFoundException and sharing-violation
            // IOException are subclasses of IOException and handled here.
            try
            {
                await AddEntryFromFileAsync(archive, filePath, entryName, context.CompressionLevel,
                    new EntryWriteProgress(context.TotalBytes, startOffset, context.Progress), cancellationToken);
            }
            catch (IOException ex)
            {
                context.ReportError(new ArchiveError
                {
                    SourcePath = filePath,
                    Message = $"Cannot access file: {ex.Message}",
                    Exception = ex
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                context.ReportError(new ArchiveError
                {
                    SourcePath = filePath,
                    Message = $"Access denied: {ex.Message}",
                    Exception = ex
                });
            }

            startOffset += fileSize;
        }

        return startOffset;
    }

    private static async Task<long> AddSubdirectoriesAsync(
        ZipArchive archive, string sourceDir, DirectoryArchiveContext context, long startOffset, CancellationToken cancellationToken)
    {
        foreach (string subDir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // T-F23: Skip NTFS junctions and directory symlinks — prevents infinite loops
            if (ArchiveEntrySecurity.IsReparsePoint(subDir))
            {
                context.ReportSkipped(new SkippedFile
                {
                    Path = subDir,
                    Reason = "NTFS junctions and directory symbolic links are not followed during archiving."
                });
                continue;
            }

            startOffset = await AddDirectoryToArchiveAsync(archive, subDir, context, startOffset, cancellationToken)
                .ConfigureAwait(false);
        }

        return startOffset;
    }

    private static long ComputeTotalBytes(IReadOnlyList<string> paths)
    {
        long total = 0;
        foreach (var p in paths)
        {
            try
            {
                if (ArchiveEntrySecurity.IsReparsePoint(p)) continue;
                if (File.Exists(p)) total += new FileInfo(p).Length;
                else if (Directory.Exists(p)) total += ComputeDirectoryBytes(p);
            }
            catch { /* best-effort */ }
        }
        return total;
    }

    // T-F23: Safe recursive byte count that skips reparse points — prevents infinite loops
    // on circular directory symlinks and NTFS junctions.
    private static long ComputeDirectoryBytes(string dir)
    {
        long total = 0;
        try
        {
            foreach (string filePath in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
                .Where(f => !ArchiveEntrySecurity.IsReparsePoint(f)))
            {
                try { total += new FileInfo(filePath).Length; } catch { /* best-effort */ }
            }
            foreach (string subDir in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly)
                .Where(d => !ArchiveEntrySecurity.IsReparsePoint(d)))
            {
                total += ComputeDirectoryBytes(subDir);
            }
        }
        catch { /* best-effort */ }
        return total;
    }

    // T-F35: combined byte-total + file-count walk for ArchiveAsync's SingleArchive branch —
    // used for both the progress-report total and the parallel-pipeline gate decision in one
    // pass (profiling found the previous two-separate-walks approach cost ~193ms combined
    // against a 5,000-file fixture). Also applies the same stat-call reduction as
    // WorkItemEnumerator: DirectoryInfo.EnumerateFiles()/EnumerateDirectories() populate
    // Length/Attributes from the same FindNextFile data the enumeration itself already read,
    // instead of separate File.GetAttributes/FileInfo.Length calls per entry.
    private static (long TotalBytes, int FileCount) ComputeSingleArchiveTotals(IReadOnlyList<string> paths)
    {
        long totalBytes = 0;
        int fileCount = 0;
        foreach (var p in paths)
        {
            try
            {
                if (ArchiveEntrySecurity.IsReparsePoint(p)) continue;
                if (File.Exists(p))
                {
                    totalBytes += new FileInfo(p).Length;
                    fileCount++;
                }
                else if (Directory.Exists(p))
                {
                    var (bytes, count) = ComputeDirectoryTotals(p);
                    totalBytes += bytes;
                    fileCount += count;
                }
            }
            catch { /* best-effort */ }
        }
        return (totalBytes, fileCount);
    }

    private static (long TotalBytes, int FileCount) ComputeDirectoryTotals(string dir)
    {
        long totalBytes = 0;
        int fileCount = 0;
        try
        {
            foreach (var fileInfo in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                try
                {
                    totalBytes += fileInfo.Length;
                    fileCount++;
                }
                catch { /* best-effort */ }
            }
            foreach (var dirInfo in new DirectoryInfo(dir).EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
            {
                if (dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                var (bytes, count) = ComputeDirectoryTotals(dirInfo.FullName);
                totalBytes += bytes;
                fileCount += count;
            }
        }
        catch { /* best-effort */ }
        return (totalBytes, fileCount);
    }

    private static bool IsZipFile(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[4];
            using var fs = File.OpenRead(path);
            fs.ReadExactly(header);
            return header[0] == 0x50 && header[1] == 0x4B
                && header[2] == 0x03 && header[3] == 0x04;
        }
        catch
        {
            return false;
        }
    }

    // T-F189: the original first-local-header-only check stays as a fast path — it's also the
    // ONLY thing that can detect encryption on a file whose local header is valid but has no real
    // end-of-central-directory record at all (RawZipEntryLocator.HasAnyEncryptedEntry requires a
    // real EOCD to run at all; ExtractAsync_PasswordProtectedZip_ReturnsArchiveErrorWithClearMessage's
    // fixture is exactly this — a bare 20-byte local header, nothing else). Falling through to the
    // full central-directory scan only when the cheap check says "no" additionally catches this
    // project's own mixed_encrypted_and_plain.zip fixture, whose plain entry comes first — a case
    // the cheap check alone would silently miss. See docs/DECISIONS.md's T-F189 entry.
    internal static bool IsEncryptedZip(string path) =>
        IsFirstLocalHeaderEncrypted(path) || HasAnyEncryptedEntryViaCentralDirectory(path);

    private static bool IsFirstLocalHeaderEncrypted(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[8];
            using var fs = File.OpenRead(path);
            int read = fs.Read(header);
            if (read < 8) return false;
            // flags are at offset 6 (little-endian); bit 0 = encryption flag
            return (header[6] & 0x01) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasAnyEncryptedEntryViaCentralDirectory(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return RawZipEntryLocator.HasAnyEncryptedEntry(fs);
        }
        catch
        {
            return false;
        }
    }

    private static string? GetKnownArchiveReason(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[6];
            using var fs = File.OpenRead(path);
            int read = fs.Read(header);

            // GZIP: 1F 8B
            if (read >= 2 && header[0] == 0x1F && header[1] == 0x8B)
                return "GZip format is not supported. Only ZIP-based formats are supported.";

            // BZip2: 42 5A 68
            if (read >= 3 && header[0] == 0x42 && header[1] == 0x5A && header[2] == 0x68)
                return "BZip2 format is not supported. Only ZIP-based formats are supported.";

            // RAR: 52 61 72 21
            if (read >= 4 && header[0] == 0x52 && header[1] == 0x61 && header[2] == 0x72 && header[3] == 0x21)
                return "RAR format is not supported. Only ZIP-based formats are supported.";

            // LZ4: 04 22 4D 18
            if (read >= 4 && header[0] == 0x04 && header[1] == 0x22 && header[2] == 0x4D && header[3] == 0x18)
                return "LZ4 format is not supported. Only ZIP-based formats are supported.";

            // 7-Zip: 37 7A BC AF 27 1C
            if (read >= 6 && header[0] == 0x37 && header[1] == 0x7A && header[2] == 0xBC
                && header[3] == 0xAF && header[4] == 0x27 && header[5] == 0x1C)
                return "7-Zip format is not supported. Only ZIP-based formats are supported.";

            // XZ: FD 37 7A 58 5A 00
            if (read >= 6 && header[0] == 0xFD && header[1] == 0x37 && header[2] == 0x7A
                && header[3] == 0x58 && header[4] == 0x5A && header[5] == 0x00)
                return "XZ format is not supported. Only ZIP-based formats are supported.";

            return null;
        }
        catch
        {
            return null;
        }
    }

    // T-F38/T-F39/T-F23/T-F37/T-F45 checks moved to ArchiveEntrySecurity (T-F49) — shared with
    // TarProcessService so validation cannot drift between extractors.

    // T-F60: Returns true when the temp ZIP at path contains at least one entry.
    // Used to decide whether to commit or discard after an all-failures archive run.
    private static bool HasTempEntries(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.Entries.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    // T-F30: claimedPaths lets a caller also exclude candidates already reserved in-memory this
    // run (e.g. a rename target chosen for an earlier duplicate entry that hasn't been written
    // to the real destination yet) — File.Exists alone can't see those.
    private static string GetUniqueFilePath(string path, HashSet<string>? claimedPaths = null)
    {
        string dir = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        int i = 1;
        string candidate;
        do { candidate = Path.Combine(dir, $"{name} ({i++}){ext}"); }
        while (File.Exists(candidate) || (claimedPaths?.Contains(candidate) ?? false));
        return candidate;
    }

    // T-F30: same "name (1)", "name (2)", ... renaming convention as GetUniqueFilePath, but
    // against an in-memory set of ZIP entry names already claimed at the archive root rather
    // than the filesystem — two top-level SourcePaths sharing a basename would otherwise become
    // two ZIP entries with the identical name (CreateEntry does not reject duplicates).
    // Internal (not private) so Zip.WorkItemEnumerator (T-F35's parallel SingleArchive path) can
    // reuse the exact same T-F30 collision-renaming rule instead of duplicating it.
    internal static string GetUniqueEntryName(HashSet<string> usedNames, string proposedName)
    {
        if (usedNames.Add(proposedName))
            return proposedName;

        string name = Path.GetFileNameWithoutExtension(proposedName);
        string ext = Path.GetExtension(proposedName);
        int i = 1;
        string candidate;
        do { candidate = $"{name} ({i++}){ext}"; }
        while (!usedNames.Add(candidate));
        return candidate;
    }

}
