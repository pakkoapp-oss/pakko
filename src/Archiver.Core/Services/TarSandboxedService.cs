using System.Diagnostics;
using System.Globalization;
using Archiver.Core.Interfaces;
using Archiver.Core.IO;
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

    // The App's first window waits for DetectCapabilitiesAsync (App.xaml.cs starts it on the
    // thread pool, T-F347) — a hung tar.exe --version must not hang app launch indefinitely.
    private static readonly TimeSpan DetectionTimeout = TimeSpan.FromSeconds(5);

    private readonly GroupPolicyOptions _policy;
    private readonly Func<Task<TarCapabilities>> _probe;

    /// <summary>Creates the service under the given Group Policy (T-F261: required, never defaulted).</summary>
    public TarSandboxedService(GroupPolicyOptions policy)
        : this(policy, () => DetectCapabilitiesAsync(TarExecutablePath))
    {
    }

    // T-F261: test seam for the unsandboxed tar.exe --version probe, so a unit test can prove it
    // never runs under DisableTarExtraction without starting tar.exe.
    internal TarSandboxedService(GroupPolicyOptions policy, Func<Task<TarCapabilities>> probe)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
        _probe = probe;
    }

    // T-F261: POLICIES.md promises DisableTarExtraction means tar.exe is never started — enforced
    // here, where tar.exe is launched, not only by the callers that route to this engine.
    private static CoreText TarDisabled => CoreMessages.Text(MessageCode.TarExtractionDisabled);

    /// <inheritdoc/>
    /// <remarks>Under DisableTarExtraction the probe never runs and all-false defaults are returned.</remarks>
    public Task<TarCapabilities> DetectCapabilitiesAsync() =>
        _policy.DisableTarExtraction ? Task.FromResult(new TarCapabilities()) : _probe();

    private static ArchiveResult RefuseAll(IReadOnlyList<string> paths, CoreText text) => new()
    {
        Errors = [.. paths.Select(path => CoreMessages.Error(path, text))],
        Sources = [.. paths.Select(path => SourceOutcomeRules.Classify(path, produced: false, clean: false))],
    };

    // T-F182 (test-coverage audit): internal test-only overload — the const TarExecutablePath
    // stays hardcoded for every real caller (CLAUDE.md's PATH-hijack hard constraint), but this
    // lets Archiver.Core.Tests point the real detection logic at a path that doesn't exist, to
    // prove "tar.exe physically absent" hits the same documented all-false-defaults path as every
    // other failure mode here, without adding a public seam to the const itself.
    internal static async Task<TarCapabilities> DetectCapabilitiesAsync(string tarExecutablePath)
    {
        try
        {
            // Deliberately unsandboxed (no AppContainer/Job Object) — this is a one-shot,
            // eagerly-resolved startup probe, not an untrusted-archive operation. Still gated on
            // the signature check: a tampered tar.exe should fail closed here via the same
            // all-false-defaults path used for "tar.exe absent", not silently run --version.
            if (!TarSignatureVerifier.Verify(tarExecutablePath))
                return new TarCapabilities();

            using var timeoutCts = new CancellationTokenSource(DetectionTimeout);
            (_, string? output, _) = await SandboxedProcessLauncher.RunAsync(
                tarExecutablePath, ["--version"], new ProcessLaunchOptions(), timeoutCts.Token).ConfigureAwait(false);
            return TarVersionParser.Parse(output);
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
        if (_policy.DisableTarExtraction)
            return RefuseAll(options.ArchivePaths, TarDisabled);

        var errors = new List<ArchiveError>();
        var createdFiles = new List<string>();
        var skippedFiles = new List<SkippedFile>();
        // T-F06: one instance for the whole call — see ZipArchiveService.ExtractAsync's identical
        // comment. Independent of ZipArchiveService's own resolver instance: Zip and tar-family
        // archives are handled by two separate ExtractionRouter calls, so "apply to all" does not
        // cross a mixed zip+tar-family selection (an accepted, documented scope cut).
        var conflictResolver = new ConflictResolver(options.OnConflict, options.ResolveConflictAsync);

        bool destinationExisted = Directory.Exists(options.DestinationFolder);
        Directory.CreateDirectory(options.DestinationFolder);
        try
        {
            return await ExtractArchivesAsync(options, conflictResolver, new ArchiveResultSink(errors, createdFiles, skippedFiles), progress, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            // T-F309: as ZIP's T-F230 — a run that produced nothing (refused, cancelled) must not
            // leave behind the empty folder it created. Never one CreatedFiles names.
            if (!destinationExisted && createdFiles.Count == 0)
                ZipArchiveService.TryDeleteEmptyDirectory(options.DestinationFolder);
        }
    }

    private async Task<ArchiveResult> ExtractArchivesAsync(
        ExtractOptions options, ConflictResolver conflictResolver, ArchiveResultSink sink,
        IProgress<ProgressReport>? progress, CancellationToken cancellationToken)
    {
        (List<ArchiveError> errors, List<string> createdFiles, List<SkippedFile> skippedFiles) = sink;
        int total = options.ArchivePaths.Count;
        var sources = new List<SourceResult>();

        for (int i = 0; i < total; i++)
        {
            // T-F260/T-F245: a cancel between or inside archives throws — it used to end the loop
            // and return a result that looked finished, so "Delete after operation" deleted
            // archives that were never extracted.
            cancellationToken.ThrowIfCancellationRequested();

            int errorsBefore = errors.Count, skippedBefore = skippedFiles.Count, createdBefore = createdFiles.Count,
                userSkipsBefore = conflictResolver.UserSkipCount;
            await ExtractArchiveAtIndexAsync(
                options, i, total, conflictResolver, sink, progress, cancellationToken).ConfigureAwait(false);

            // T-F265: a subset extraction never makes the whole archive deletable.
            // T-F216: nor does an entry the user chose to skip, though it is not listed.
            sources.Add(SourceOutcomeRules.Classify(options.ArchivePaths[i],
                produced: createdFiles.Count > createdBefore,
                clean: errors.Count == errorsBefore && skippedFiles.Count == skippedBefore
                    && conflictResolver.UserSkipCount == userSkipsBefore && options.SelectedEntryPaths is null));
        }

        var result = new ArchiveResult
        {
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

    private static bool IsKnownEncryptedRar(string archivePath) =>
        ArchiveFormatDetector.Detect(archivePath) == ArchiveFormat.Rar
            && ArchiveFormatDetector.IsEncryptedRar(archivePath);

    // One iteration of ExtractAsync's per-archive loop — moved out so the loop itself reads as
    // "check cancellation, extract one" at a glance. Cancellation propagates as
    // OperationCanceledException (T-F260).
    private async Task ExtractArchiveAtIndexAsync(
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
            sink.Errors.Add(CoreMessages.Error(archivePath, MessageCode.PasswordProtectedFormatNotSupported));
        }
        else
        {
            IProgress<ProgressReport>? archiveProgress = singleArchive ? progress : null;
            await ExtractOneArchiveAsync(
                archivePath, destDir, options, conflictResolver, sink, archiveProgress, cancellationToken).ConfigureAwait(false);
        }

        if (!singleArchive)
            progress?.Report(new ProgressReport { Percent = (i + 1) * 100 / total, BytesTransferred = 0, TotalBytes = 0 });
    }

    // The three List sinks ExtractOneArchiveAsync/ProcessSeparateArchivesAsync write into —
    // bundled to cut S107's parameter count, same reasoning as ZipArchiveService's own
    // (ConcurrentBag-typed, for its parallel workers) ArchiveResultSink; this one stays List-typed
    // since neither of this file's two call sites is itself parallelized across archives.
    private sealed record ArchiveResultSink(
        List<ArchiveError> Errors,
        List<string> CreatedFiles,
        List<SkippedFile> SkippedFiles);

    // The try/catch error-mapping body of ExtractAsync's per-archive loop, pulled out so the
    // loop itself reads as "known-encrypted-RAR short-circuit, else extract-and-map-errors" at a
    // glance. Every outcome except cancellation is recorded into errors/createdFiles; a cancel
    // propagates as OperationCanceledException (T-F260 — it used to be swallowed here, T-F245).
    private async Task ExtractOneArchiveAsync(
        string archivePath, string destDir, ExtractOptions options, ConflictResolver conflictResolver,
        ArchiveResultSink sink, IProgress<ProgressReport>? archiveProgress, CancellationToken cancellationToken)
    {
        try
        {
            bool alreadyIsolated = options.Mode == ExtractMode.SeparateFolders;
            var context = new TarExtractionContext(
                conflictResolver, sink.SkippedFiles, options.ConfirmCompressionBombExtraction, _policy.EffectiveMotwMode(options.ApplyDownloadMark), archiveProgress, sink.Errors,
                options.EliminateDuplicateRootFolder);
            (string? actualDest, bool anyExtracted) = await ExtractSingleArchiveAsync(
                archivePath, destDir, alreadyIsolated, options.DestinationFolder, options.SelectedEntryPaths, context, cancellationToken)
                .ConfigureAwait(false);

            // T-F87: an archive whose entries were all individually skipped (e.g. every entry
            // already exists at the destination with OnConflict=Skip) must not be reported as
            // CreatedFiles — MainViewModel uses this list to decide whether DeleteAfterOperation
            // may delete the source archive.
            if (anyExtracted)
                sink.CreatedFiles.Add(actualDest);
        }
        catch (TarArchiveRejectedException ex)
        {
            sink.Errors.Add(CoreMessages.Error(archivePath, ex.Text));
        }
        catch (TarSignatureVerificationException ex)
        {
            sink.Errors.Add(CoreMessages.Error(archivePath, ex.Text));
        }
        catch (SandboxSetupException ex)
        {
            sink.Errors.Add(CoreMessages.Error(archivePath, ex.Text, ex));
        }
        catch (IOException ex)
        {
            // T-F113: covers 7z (both encryption modes) and RAR's header-encrypted case — the
            // proactive check above only catches RAR's more common data-only case before staging
            // even begins.
            sink.Errors.Add(CoreMessages.Error(archivePath,
                IsLikelyEncryptionFailure(ex.Message)
                    ? CoreMessages.Text(MessageCode.PasswordProtectedFormatNotSupported)
                    : CoreMessages.Wrap(MessageCode.CannotExtractArchive, ex),
                ex));
        }
        catch (UnauthorizedAccessException ex)
        {
            sink.Errors.Add(CoreMessages.Error(archivePath, CoreMessages.Text(MessageCode.AccessDeniedExtractingArchive, CoreMessages.Detail(ex)), ex));
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
    // T-F52: the scope (profile + ACLs + the open archive + Job Object + signature check) is
    // created FIRST, before the compression-bomb decision — unlike the pre-sandbox design, the
    // pre-scan itself must now run inside the sandbox too, reading the archive the scope holds
    // open (T-F233). This means a declined/blocked bomb no longer leaves
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
        string unisolatedDestDir,
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
        // T-F307: each listing pass decompresses the whole archive, so it gets its own phase.
        progress?.Report(new ProgressReport { Percent = 0, Phase = ProgressPhase.CheckingArchive });
        (long declaredUncompressedSize, string[]? allNames, Dictionary<string, long>? sizeByName, List<(string Name, long Size)> fileEntries) = await ScanForUnsafeEntriesAsync(scope, cancellationToken)
            .ConfigureAwait(false);

        // T-F118: mirrors ZipArchiveService.ExtractWithSmartFolderingAsync's identical algorithm
        // exactly — allNames already carries tar's own trailing '/' convention for directory
        // entries (see ScanForUnsafeEntriesAsync's comment), so "file entries" can be derived the
        // same way ZIP derives them from ZipArchiveEntry.FullName, with no second tar.exe call.
        // A selected subset (T-F05/T-F98 drill-down) has no single meaningful "root" to collapse,
        // same reasoning as ZIP's isSelectedSubset — always extract straight into destDir.
        bool isSelectedSubset = selectedEntryPaths is { Count: > 0 };
        // T-F196: `tar -C dir .` (the most common way to make a tar) prefixes every member with
        // "./". tar.exe itself resolves that away on disk (out\a.txt, not out\.\a.txt), so the
        // root-shape decision must see the same names — otherwise "." reads as one shared root
        // folder, and the move phase strips a real path segment (or silently drops a root file).
        var fileNames = allNames.Where(n => !n.EndsWith('/')).Select(StripLeadingDotSlash).ToList();
        // T-F197: folder entries count as roots too ("a.txt" + "empty/" is two roots). A bare
        // "./" member names the archive's own top level, not a root.
        var rootNames = allNames.Select(StripLeadingDotSlash).Where(n => n.Length > 0).ToList();

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
        // Ended here, not by the byte poll: the poll does not run for an archive with no file bytes.
        progress?.Report(new ProgressReport { Percent = 0, BytesTransferred = 0, TotalBytes = progressTotalBytes });

        bool isSingleRootFolder = !isSelectedSubset
            && rootNames.Count > 0
            && rootNames.All(n => n.Contains('/'))
            && rootNames
                .Select(n => n[..n.IndexOf('/')])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 1;

        bool isSingleRootFile = !isSelectedSubset && rootNames.Count == 1 && !rootNames[0].Contains('/');

        // T-F157: mirrors ZipArchiveService.ExtractWithSmartFolderingAsync exactly — the
        // actualDest/StripRootPrefix decision (T-F154's single-file unwrap, T-F156's multi-root-in-
        // SingleFolder-mode fix, and the pre-existing isSingleRootFolder strip) now lives in the
        // shared ExtractionDestinationPlanner instead of two hand-kept-in-sync copies (T-F118's own
        // comment called that sync a documentation-enforced promise) — see DECISIONS.md's T-F157
        // entry.
        RootShape rootShape = ExtractionDestinationPlanner.Classify(isSelectedSubset, isSingleRootFolder, isSingleRootFile);
        bool rootDuplicatesArchiveName = context.EliminateDuplicateRootFolder && isSingleRootFolder
            && ExtractionDestinationPlanner.RootDuplicatesArchiveName(rootNames[0][..rootNames[0].IndexOf('/')], archivePath);
        (string? actualDest, bool stripRootPrefix) = ExtractionDestinationPlanner.Resolve(
            alreadyIsolated, rootShape, destDir, unisolatedDestDir, rootDuplicatesArchiveName);

        // T-F94: whole-archive compression-ratio decision. compressedFileSize reads archivePath —
        // the scope holds it open and unwritable, so this is the size tar.exe reads.
        long compressedFileSize = new FileInfo(archivePath).Length;
        CompressionBombOutcome bombOutcome = await ArchiveEntrySecurity.EvaluateCompressionBombAsync(
            archivePath, declaredUncompressedSize, compressedFileSize,
            ArchiveEntrySecurity.GetAvailableFreeSpace(destDir),
            confirmCompressionBombExtraction).ConfigureAwait(false);

        if (bombOutcome == CompressionBombOutcome.InsufficientDiskSpace)
        {
            skippedFiles.Add(CoreMessages.Skip(archivePath, MessageCode.InsufficientDiskSpace,
                declaredUncompressedSize.ToString("N0", CultureInfo.CurrentCulture),
                ArchiveEntrySecurity.GetAvailableFreeSpace(destDir).ToString("N0", CultureInfo.CurrentCulture)));
            return (destDir, false);
        }

        if (bombOutcome == CompressionBombOutcome.UserDeclined)
        {
            long ratio = compressedFileSize > 0 ? declaredUncompressedSize / compressedFileSize : 0;
            skippedFiles.Add(CoreMessages.Skip(archivePath, MessageCode.TarBombDeclined,
                ratio.ToString(CultureInfo.CurrentCulture), declaredUncompressedSize.ToString("N0", CultureInfo.CurrentCulture)));
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
        // T-F298: only the directories tar.exe would have to create implicitly — tar.exe does not
        // set the time of a directory that already exists, so pre-creating one with its own entry
        // lost that entry's time.
        // With a selection, only the selected names are extracted: their folders' entries may not be.
        IEnumerable<string> extractedNames = (IEnumerable<string>?)expandedSelection ?? allNames;
        foreach (string relativeDir in DirectoriesToPreCreate(extractedNames))
            Directory.CreateDirectory(Path.Combine(scope.OutputDirectory!, relativeDir));


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
        Task<(int ExitCode, string StdOut, string StdErr)> extractionTask = scope.ExtractAsync(expandedSelection, cancellationToken);

        if (progress != null && progressTotalBytes > 0)
        {
            await PollExtractionProgressAsync(
                scope.OutputDirectory!, progressTotalBytes, progress, extractionTask, cancellationToken)
                .ConfigureAwait(false);
        }

        (int exitCode, _, string? stdErr) = await extractionTask.ConfigureAwait(false);

        if (exitCode != 0)
            throw new CoreTextIOException(CoreMessages.Text(MessageCode.TarExtractionFailed, DescribeFailure(stdErr)));

        (string? firstCopyDir, HashSet<string> firstCopies) = await ExtractFirstCopiesAsync(
            scope, FindDuplicateGroups(fileEntries, allNames, expandedSelection), skippedFiles, cancellationToken)
            .ConfigureAwait(false);

        // T-F263: the same staging + commit as ZIP extraction (ExtractionStaging) — files move from
        // the quarantine into a staging folder on the destination's volume, and only a finished
        // archive is committed, so a cancel or failure partway through leaves no partial files.
        using var staging = ExtractionStaging.Create(unisolatedDestDir);
        var plan = new TarCommitPlan(staging, actualDest, stripRootPrefix,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        RecordFolderTimes((IEnumerable<string>?)expandedSelection ?? allNames, stripRootPrefix, scope.OutputDirectory!, staging);

        int totalFiles = 0;
        int extractedCount = 0;
        // T-F216: see ZipArchiveService — to tell "the user skipped everything" apart.
        int userSkipsBefore = context.ConflictResolver.UserSkipCount;
        int skippedBefore = skippedFiles.Count, errorsBefore = context.Errors.Count;
        // The move phase (quarantine "out\" -> the real destination) is not free — a cross-volume
        // move is a real copy, not a rename — so it gets its own slice of the percentage (95-99)
        // rather than leaving the dialog sitting at whatever the extraction-phase poll last saw.
        // Uses the same expandedSelection-vs-whole-archive distinction as progressTotalBytes above
        // — a subset selection only ever moves its own subset of files, not fileNames.Count.
        int totalFileEntries = (expandedSelection != null
            ? expandedSelection.Count(n => !n.EndsWith('/'))
            : fileNames.Count) + firstCopies.Count;
        var moveReportStopwatch = System.Diagnostics.Stopwatch.StartNew();
        long lastMoveReportMs = -MoveReportThrottleMs;

        // T-F171: first copies move first, so the first copy takes the name and the last one meets
        // the conflict rule, as a duplicate ZIP entry does (T-F30).
        IEnumerable<(string File, string Root)> quarantined = firstCopyDir is null
            ? []
            : EnumerateFilesGuarded(firstCopyDir).Where(firstCopies.Contains).Select(f => (f, firstCopyDir));
        quarantined = quarantined.Concat(EnumerateFilesGuarded(scope.OutputDirectory!).Select(f => (f, scope.OutputDirectory!)));

        foreach ((string file, string root) in quarantined)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalFiles++;

            (bool extracted, string? relativePath) = await TryMoveSingleEntryAsync(
                file, root, plan, archivePath, context).ConfigureAwait(false);
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

        CreateFolderEntries((IEnumerable<string>?)expandedSelection ?? allNames, stripRootPrefix, staging.Path);

        cancellationToken.ThrowIfCancellationRequested();
        foreach (string relativePath in staging.CommitInto(actualDest))
        {
            context.Errors.Add(CoreMessages.Error(archivePath, MessageCode.DestinationFileLocked, relativePath));
        }

        // T-F87: every extracted file was individually skipped (already existed at the
        // destination) — nothing was actually written, so the caller must not count this
        // archive as CreatedFiles (that list gates whether DeleteAfterOperation may delete
        // the source archive).
        bool onlyUserSkips = context.ConflictResolver.UserSkipCount > userSkipsBefore
            && skippedFiles.Count == skippedBefore && context.Errors.Count == errorsBefore;
        if (totalFiles > 0 && extractedCount == 0)
        {
            if (!onlyUserSkips)
            {
                skippedFiles.Add(CoreMessages.Skip(archivePath, MessageCode.AllEntriesSkipped));
            }
            progress?.Report(new ProgressReport { Percent = 100, BytesTransferred = progressTotalBytes, TotalBytes = progressTotalBytes });
            return (actualDest, false);
        }

        progress?.Report(new ProgressReport { Percent = 100, BytesTransferred = progressTotalBytes, TotalBytes = progressTotalBytes });
        return (actualDest, true);
    }

    // T-F171: a name several file entries share. Case-insensitive, since NTFS makes "A.txt" and
    // "a.txt" one file; FirstName is the exact name of the first entry, which "-q" matches.
    internal sealed record DuplicateGroup(string FirstName, long FirstSize, int Count);

    // Groups among the entries being extracted (the selection, or the whole archive). A name that
    // is also a folder entry is left out: the two cannot both exist on disk.
    internal static List<DuplicateGroup> FindDuplicateGroups(
        IReadOnlyList<(string Name, long Size)> fileEntries, IReadOnlyList<string> allNames, IReadOnlyCollection<string>? selection)
    {
        HashSet<string>? selected = selection is null ? null : new HashSet<string>(selection, StringComparer.Ordinal);
        var folders = new HashSet<string>(
            allNames.Where(n => n.EndsWith('/')).Select(n => StripLeadingDotSlash(n).TrimEnd('/')),
            StringComparer.OrdinalIgnoreCase);
        var groups = new Dictionary<string, DuplicateGroup>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, long size) in fileEntries)
        {
            if (selected is not null && !selected.Contains(name))
                continue;
            string key = StripLeadingDotSlash(name);
            groups[key] = groups.TryGetValue(key, out DuplicateGroup? group)
                ? group with { Count = group.Count + 1 }
                : new DuplicateGroup(name, size, 1);
        }
        return groups.Where(g => g.Value.Count > 1 && !folders.Contains(g.Key)).Select(g => g.Value).ToList();
    }

    // T-F171: runs the first-copy pass and keeps only files at a group's exact path with the first
    // entry's listed size. A member is a pattern (T-F284), so "a[1].txt" can pull in "a1.txt"
    // instead; anything unexpected stays in the quarantine. A group whose first copy is not
    // recovered keeps only its last copy, and every copy not extracted is reported.
    private static async Task<(string? Directory, HashSet<string> Files)> ExtractFirstCopiesAsync(
        TarSandboxScope scope, List<DuplicateGroup> groups, List<SkippedFile> skippedFiles, CancellationToken cancellationToken)
    {
        var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (groups.Count == 0)
            return (null, accepted);

        string? firstDir;
        try
        {
            (firstDir, _, _) = await scope.ExtractFirstOccurrencesAsync(
                groups.Select(g => g.FirstName).ToList(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SandboxSetupException)
        {
            firstDir = null; // the main pass already succeeded: keep its last copies, report the rest
        }

        foreach (DuplicateGroup group in groups)
        {
            string displayName = StripLeadingDotSlash(group.FirstName);
            FileInfo? info = firstDir is null ? null : new FileInfo(Path.GetFullPath(Path.Combine(firstDir, displayName)));
            bool recovered = info is { Exists: true } && info.Length == group.FirstSize
                && (info.Attributes & FileAttributes.ReparsePoint) == 0;
            if (recovered)
                accepted.Add(info!.FullName);
            int notExtracted = group.Count - (recovered ? 2 : 1);
            if (notExtracted > 0)
                skippedFiles.Add(CoreMessages.Skip(displayName, MessageCode.TarDuplicateCopiesNotExtracted,
                    notExtracted.ToString(CultureInfo.CurrentCulture)));
        }
        return (firstDir, accepted);
    }

    // Where ExtractSingleArchiveAsync's move phase puts each file: into Staging under the path it
    // will have in ActualDest (T-F263). ClaimedFinalPaths is ZIP's T-F30 set — a name picked for a
    // renamed conflict must not collide with another entry of the same archive already staged.
    private sealed record TarCommitPlan(
        ExtractionStaging Staging, string ActualDest, bool StripRootPrefix, HashSet<string> ClaimedFinalPaths);

    // T-F197: the move phase walks files only, so folder entries are created here — from the
    // names the pre-scan already validated (no "..", rooted, ADS or reserved names), with the
    // same root strip the files get. Moving files first means a folder that also holds files
    // already exists; only the empty ones are new.
    private static void CreateFolderEntries(IEnumerable<string> names, bool stripRootPrefix, string actualDest)
    {
        foreach (string name in names)
        {
            if (!name.EndsWith('/'))
                continue;
            string relative = StripLeadingDotSlash(name).TrimEnd('/').Replace('/', Path.DirectorySeparatorChar);
            if (stripRootPrefix)
            {
                int sep = relative.IndexOf(Path.DirectorySeparatorChar);
                relative = sep < 0 ? string.Empty : relative[(sep + 1)..];
            }
            if (relative.Length > 0)
                Directory.CreateDirectory(Path.Combine(actualDest, relative));
        }
    }

    // T-F52/T-F298: the directories an entry needs before its own directory entry has been seen,
    // in archive order — tar.exe cannot create those itself inside the AppContainer. A directory
    // whose entry comes before everything inside it (what GNU tar, bsdtar and 7-Zip write) is
    // left to tar.exe, which then sets its time. Pre-creating a directory also creates its
    // parents, so a rare archive with an implicit subfolder under an explicit one loses that
    // parent's time; it still extracts.
    internal static List<string> DirectoriesToPreCreate(IEnumerable<string> namesInArchiveOrder)
    {
        var seenDirectoryEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var preCreate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in namesInArchiveOrder)
        {
            string path = StripLeadingDotSlash(name);
            bool isDirectory = path.EndsWith('/');
            string[] segments = path.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < segments.Length; i++)
            {
                string ancestor = string.Join('/', segments, 0, i);
                if (!seenDirectoryEntries.Contains(ancestor))
                    preCreate.Add(ancestor);
            }
            if (isDirectory && segments.Length > 0)
                seenDirectoryEntries.Add(string.Join('/', segments));
        }
        return [.. preCreate];
    }

    // T-F298: a folder entry takes the time tar.exe gave it in the quarantine — read before the
    // move phase, since moving a folder's files out changes that time. Same names and root strip
    // as CreateFolderEntries; "" is the stripped root, which actualDest stands in for.
    private static void RecordFolderTimes(IEnumerable<string> names, bool stripRootPrefix, string quarantineDir, ExtractionStaging staging)
    {
        foreach (string name in names)
        {
            if (!name.EndsWith('/'))
                continue;
            string entryPath = StripLeadingDotSlash(name).TrimEnd('/').Replace('/', Path.DirectorySeparatorChar);
            string quarantined = Path.Combine(quarantineDir, entryPath);
            if (entryPath.Length == 0 || !Directory.Exists(quarantined))
                continue;
            string relative = entryPath;
            if (stripRootPrefix)
            {
                int sep = relative.IndexOf(Path.DirectorySeparatorChar);
                relative = sep < 0 ? string.Empty : relative[(sep + 1)..];
            }
            try
            {
                staging.RecordFolderTime(relative, Directory.GetLastWriteTimeUtc(quarantined));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // best-effort: the folder keeps the time the commit gives it
            }
        }
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
        IProgress<ProgressReport>? Progress,
        // T-F170: mirrors ZipExtractionContext's identical addition — a per-item failure to move
        // a file out of the quarantine (destination locked by another process), distinct from
        // SkippedFiles since data the user asked for genuinely failed to arrive.
        List<ArchiveError> Errors,
        // T-F205: ExtractOptions.EliminateDuplicateRootFolder.
        bool EliminateDuplicateRootFolder);

    // One file of ExtractSingleArchiveAsync's move-phase loop (quarantine "out\" -> staging, then
    // committed) — conflict-resolve, move, propagate MOTW. Returns whether the file was
    // actually moved (false for both the already-exists+Skip case and the defensive-only
    // isSingleRootFolder edge case, matching the original inline loop's two `continue` sites) and
    // the relative path actually used, for the caller's own progress-report CurrentFile.
    // Only feeds the conflict prompt's comparison, so an unreadable file just shows no details.
    private static (long? Size, DateTimeOffset? Modified) DescribeIncoming(string file)
    {
        try
        {
            var info = new FileInfo(file);
            return (info.Length, new DateTimeOffset(info.LastWriteTimeUtc));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    private static string StripLeadingDotSlash(string name)
    {
        while (name.StartsWith("./", StringComparison.Ordinal))
            name = name[2..];
        return name;
    }

    private static async Task<(bool Extracted, string? RelativePath)> TryMoveSingleEntryAsync(
        string file, string outputDirectory, TarCommitPlan plan, string archivePath, TarExtractionContext context)
    {
        string relativePath = Path.GetRelativePath(outputDirectory, file);
        bool stripRootPrefix = plan.StripRootPrefix;
        string actualDest = plan.ActualDest;

        // T-F118/T-F157: matches ZipArchiveService.ExtractWithSmartFolderingAsync's identical
        // strip (now via the shared ExtractionDestinationPlanner) — when the whole archive
        // collapses to one root folder, actualDest already stands in for that folder, so its own
        // name is dropped from the path being written.
        if (stripRootPrefix)
        {
            int sep = relativePath.IndexOf(Path.DirectorySeparatorChar);
            if (sep < 0)
            {
                // Defensive only — every file walked here came from a fileNames entry that was
                // confirmed to contain '/' for stripRootPrefix (RootShape.SingleFolder) to be
                // true at all.
                return (false, null);
            }
            relativePath = relativePath[(sep + 1)..];
        }

        string finalFilePath = Path.GetFullPath(Path.Combine(actualDest, relativePath));

        // Conflicts are decided against the destination plus every path already claimed in this
        // run (T-F30, as in ZIP's WriteEntryAsync).
        if (File.Exists(finalFilePath) || plan.ClaimedFinalPaths.Contains(finalFilePath))
        {
            (long? incomingSize, DateTimeOffset? incomingModified) = DescribeIncoming(file);
            int userSkipsSoFar = context.ConflictResolver.UserSkipCount;
            ConflictBehavior resolvedConflict = await context.ConflictResolver
                .ResolveAsync(finalFilePath, incomingSize, incomingModified).ConfigureAwait(false);
            if (resolvedConflict == ConflictBehavior.Skip)
            {
                // T-F216: the user's own Skip answer is not listed (as in ZIP); only an automatic one.
                if (context.ConflictResolver.UserSkipCount == userSkipsSoFar)
                    context.SkippedFiles.Add(CoreMessages.Skip(relativePath, MessageCode.FileExistsAtDestination));
                return (false, relativePath);
            }
            if (resolvedConflict == ConflictBehavior.Rename)
            {
                finalFilePath = ArchiveNaming.GetUniqueFilePath(finalFilePath, plan.ClaimedFinalPaths);
            }
        }
        plan.ClaimedFinalPaths.Add(finalFilePath);

        // T-F263: staged under the path it will have in actualDest; the commit moves it there.
        string stagedFilePath = Path.Combine(plan.Staging.Path, Path.GetRelativePath(actualDest, finalFilePath));
        Directory.CreateDirectory(Path.GetDirectoryName(stagedFilePath)!);

        // T-F170: a file that cannot be written fails only itself. A locked destination file is
        // reported by the commit (ExtractionStaging.CommitInto), the same way as for ZIP.
        try
        {
            File.Move(file, stagedFilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            context.Errors.Add(CoreMessages.Error(archivePath, CoreMessages.Text(MessageCode.CannotExtractEntry, relativePath, CoreMessages.Detail(ex)), ex));
            return (false, relativePath);
        }

        // T-F45: propagate Zone.Identifier ADS from the archive the user chose to the extracted
        // file — MOTW must reflect the real source. The stream moves with the file at commit.
        ArchiveEntrySecurity.TryPropagateMotw(archivePath, stagedFilePath, context.MotwMode);

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
    internal static async Task<(long TotalDeclaredSize, string[] Names, Dictionary<string, long> SizeByName, List<(string Name, long Size)> FileEntries)> ScanForUnsafeEntriesAsync(
        TarSandboxScope scope, CancellationToken cancellationToken)
    {
        (int nameExitCode, string? nameStdOut, string? nameStdErr) = await scope.ListAsync(verbose: false, cancellationToken).ConfigureAwait(false);
        if (nameExitCode != 0)
            throw new CoreTextIOException(CoreMessages.Text(MessageCode.CannotReadArchive, DescribeFailure(nameStdErr)));

        string[] names = SplitLines(nameStdOut);

        string? unsafeName = names.FirstOrDefault(IsDangerousEntryName);
        if (unsafeName != null)
            throw new TarArchiveRejectedException(CoreMessages.Text(MessageCode.TarUnsafeEntryPath, unsafeName));

        (int typeExitCode, string? typeStdOut, string? typeStdErr) = await scope.ListAsync(verbose: true, cancellationToken).ConfigureAwait(false);
        if (typeExitCode != 0)
            throw new CoreTextIOException(CoreMessages.Text(MessageCode.CannotReadArchive, DescribeFailure(typeStdErr)));

        string[] typeLines = SplitLines(typeStdOut);
        if (typeLines.Length != names.Length)
            throw new TarArchiveRejectedException(CoreMessages.Text(MessageCode.TarListingInconsistent));

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
        // T-F171: every file entry in archive order — sizeByName keeps one size per name, but
        // same-named entries each need their own.
        var fileEntries = new List<(string Name, long Size)>();

        for (int i = 0; i < typeLines.Length; i++)
        {
            string line = typeLines[i];
            char typeChar = line.Length > 0 ? line[0] : '?';
            if (typeChar != '-' && typeChar != 'd')
                throw new TarArchiveRejectedException(CoreMessages.Text(MessageCode.TarSpecialEntry));

            if (typeChar == '-')
            {
                long size = ParseTarListingSize(line);
                totalDeclaredSize += size;
                sizeByName[names[i]] = size;
                fileEntries.Add((names[i], size));
            }
        }

        // T-F05: the raw names (with tar's own trailing '/' on directory entries preserved) are
        // returned so ExtractSingleArchiveAsync's selected-subset extraction can build a "-xf"
        // member argument list without a second "-tf" invocation, and so the exact path form
        // tar.exe itself uses is what's ever passed back to it (see DECISIONS.md's T-F05 spike
        // entry — an unmatched/mismatched member name makes the whole "-xf" call fail non-zero).
        return (totalDeclaredSize, names, sizeByName, fileEntries);
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

        foreach (string requested in selectedEntryPaths)
        {
            string selected = ToArchiveMemberName(allNamesSet, requested);
            if (allNamesSet.Contains(selected))
                result.Add(selected);
            else if (allNamesSet.Contains(selected + "/"))
                result.Add(selected + "/");

            string descendantPrefix = selected + "/";
            result.AddRange(allNames.Where(name => name.StartsWith(descendantPrefix, StringComparison.Ordinal)));
        }

        return result.Distinct(StringComparer.Ordinal).ToList();
    }

    // T-F196: ListEntriesAsync strips tar's "./" member prefix (see BuildEntryList), so a path the
    // Archive Browser hands back may need it restored to name the archive's real member.
    private static string ToArchiveMemberName(HashSet<string> allNames, string requested)
    {
        if (allNames.Contains(requested) || allNames.Contains(requested + "/"))
            return requested;
        string dotted = "./" + requested;
        return allNames.Contains(dotted) || allNames.Contains(dotted + "/") ? dotted : requested;
    }

    /// <inheritdoc/>
    public async Task<ArchiveListResult> ListEntriesAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        if (_policy.DisableTarExtraction)
            return CoreMessages.ListFailure(TarDisabled);

        // T-F113: cheap proactive check for the header-encrypted case only (unlike ExtractAsync's
        // IsEncryptedRar check) — a data-only-encrypted RAR's filenames are still readable, so
        // listing should still succeed there, matching ZipArchiveService.ListEntriesAsync's and
        // 7z's own parity (only extraction refuses for data-only encryption, not browsing).
        if (IsHeaderEncryptedRar(archivePath))
        {
            return CoreMessages.ListFailure(CoreMessages.Text(MessageCode.PasswordProtectedFormatNotSupported));
        }

        try
        {
            using TarSandboxScope scope = await TarSandboxScope.CreateAsync(archivePath, needsOutputDir: false, cancellationToken)
                .ConfigureAwait(false);

            (string[]? names, ArchiveListResult? nameError) = await RunListingCommandAsync(scope, verbose: false, cancellationToken).ConfigureAwait(false);
            if (nameError is not null)
                return nameError;

            (string[]? typeLines, ArchiveListResult? typeError) = await RunListingCommandAsync(scope, verbose: true, cancellationToken).ConfigureAwait(false);
            if (typeError is not null)
                return typeError;

            if (typeLines.Length != names.Length)
                return CoreMessages.ListFailure(CoreMessages.Text(MessageCode.ListingInconsistent));

            return new ArchiveListResult { Success = true, Entries = BuildEntryList(names, typeLines) };
        }
        catch (TarSignatureVerificationException ex)
        {
            return CoreMessages.ListFailure(ex.Text);
        }
        catch (SandboxSetupException ex)
        {
            return CoreMessages.ListFailure(ex.Text);
        }
        catch (IOException ex)
        {
            return CoreMessages.ListFailure(CoreMessages.FromException(ex));
        }
    }

    private static bool IsHeaderEncryptedRar(string archivePath) =>
        ArchiveFormatDetector.Detect(archivePath) == ArchiveFormat.Rar
            && ArchiveFormatDetector.IsRarHeaderEncrypted(archivePath);

    // Runs one of ListEntriesAsync's two tar.exe listing calls ("-tf" for names, "-tvf" for
    // type/size lines) and maps a nonzero exit code to an ArchiveListResult the same way both
    // calls already did identically. Error is non-null exactly when the call failed.
    private static async Task<(string[] Lines, ArchiveListResult? Error)> RunListingCommandAsync(
        TarSandboxScope scope, bool verbose, CancellationToken cancellationToken)
    {
        (int exitCode, string? stdOut, string? stdErr) = await scope.ListAsync(verbose, cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            return ([], CoreMessages.ListFailure(IsLikelyEncryptionFailure(stdErr)
                ? CoreMessages.Text(MessageCode.PasswordProtectedFormatNotSupported)
                : DescribeFailure(stdErr)));
        }

        return (SplitLines(stdOut), null);
    }

    private static List<ArchiveEntryInfo> BuildEntryList(string[] names, string[] typeLines)
    {
        DateTime now = DateTime.Now;
        var entries = new List<ArchiveEntryInfo>(names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            // T-F196: `tar -C dir .` archives prefix every member "./" and start with a bare "./"
            // — shown verbatim, the Archive Browser's root was a lone folder named ".". List the
            // same paths the plain archive would; ExpandSelection maps them back for extraction.
            string path = StripLeadingDotSlash(names[i]).TrimEnd('/');
            if (path.Length == 0)
                continue;

            char typeChar = typeLines[i].Length > 0 ? typeLines[i][0] : '?';
            TarListedDate? modified = TarListingDate.Parse(typeLines[i], TarListingDate.UserMonthNames, now);
            entries.Add(new ArchiveEntryInfo
            {
                Path = path,
                Size = typeChar == '-' ? ParseTarListingSize(typeLines[i]) : 0,
                Modified = modified?.Value,
                ModifiedHasTime = modified?.HasTime ?? true,
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
        if (_policy.DisableTarExtraction)
            return new ArchiveResult
            {
                Errors = [CoreMessages.Error(ArchiveNaming.RefusedArchivePath(options), MessageCode.TarCreationDisabled)],
            };

        // T-F153: see ZipArchiveService.ArchiveAsync's identical normalization for the full
        // rationale — a trailing separator on a source path made Path.GetFileName(sourcePath)
        // return "" in AppendSourcesToNameList, which this class's own comment there already
        // treats as the drive-root case (tar.exe strips the drive letter itself) even for an
        // ordinary folder like "src\" — silently misrouting it through the wrong tar.exe argument
        // shape instead of the normal "-C <parent> <name>" one.
        options = options with { SourcePaths = [.. options.SourcePaths.Select(SourcePathNormalizer.Normalize)] };

        var errors = new List<ArchiveError>();
        var createdFiles = new List<string>();
        var skippedFiles = new List<SkippedFile>();
        var conflictResolver = new ConflictResolver(options.OnConflict, options.ResolveConflictAsync);

        // T-F193: tar.exe/libarchive has no encrypting writer. Refuse outright — before any
        // prompt — rather than silently writing an unencrypted archive the user asked to protect.
        if (options.ResolvePasswordAsync is not null)
        {
            errors.Add(CoreMessages.Error(ArchiveNaming.RefusedArchivePath(options), MessageCode.PasswordOnlyForZip));
            return new ArchiveResult { CreatedFiles = createdFiles, Errors = errors, SkippedFiles = skippedFiles };
        }

        if (!TarSignatureVerifier.Verify(TarExecutablePath))
        {
            errors.Add(CoreMessages.Error(ArchiveNaming.RefusedArchivePath(options), MessageCode.TarSignatureInvalid));
            return new ArchiveResult { CreatedFiles = createdFiles, Errors = errors, SkippedFiles = skippedFiles };
        }

        Directory.CreateDirectory(options.DestinationFolder);
        string extension = ArchiveNaming.GetExtension(options.Format);
        IEnumerable<SourceResult> sources;

        if (options.Mode == ArchiveMode.SingleArchive)
        {
            // T-F99: same drive-root/empty-name fallback ZipArchiveService.ArchiveAsync already
            // uses for a single-source drive-root selection (e.g. "Z:\" via the shell extension's
            // Drive ItemType) instead of silently naming the archive after the bare extension.
            string destPath = Path.Combine(options.DestinationFolder, ArchiveNaming.SingleArchiveFileName(options));

            // T-F158: shared with ZipArchiveService's equivalent conflict decision — see
            // DestinationConflictResolver and DECISIONS.md's T-F158 entry.
            (DestinationConflictOutcome outcome, string? resolvedDestPath) = await DestinationConflictResolver.ResolveAsync(
                destPath, onDiskConflict: File.Exists(destPath), sameRunConflict: false,
                conflictResolver, renameCandidate: p => ArchiveNaming.GetUniqueFilePath(p)).ConfigureAwait(false);
            if (outcome == DestinationConflictOutcome.Skip)
            {
                return new ArchiveResult
                {
                    CreatedFiles = [],
                    Errors = [],
                    SkippedFiles = [.. options.SourcePaths.Select(p => CoreMessages.Skip(p, MessageCode.ArchiveAlreadyExists, Path.GetFileName(destPath)))],
                };
            }

            await CompressToArchiveAsync(options, (resolvedDestPath, outcome == DestinationConflictOutcome.ProceedReplacingExisting), createdFiles, errors, skippedFiles, progress, cancellationToken)
                .ConfigureAwait(false);

            // T-F260: one archive for every source — see ZipArchiveService.ArchiveAsync's same rule.
            bool clean = errors.Count == 0 && skippedFiles.Count == 0;
            sources = options.SourcePaths.Select(p => SourceOutcomeRules.Classify(p, createdFiles.Count > 0, clean));
        }
        else // ArchiveMode.SeparateArchives — one archive per top-level source path
        {
            var sink = new ArchiveResultSink(errors, createdFiles, skippedFiles);
            sources = await ProcessSeparateArchivesAsync(options, extension, conflictResolver, sink, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        var result = new ArchiveResult
        {
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

    // Returns one SourceResult per source that reached CompressToArchiveAsync; a missing or
    // conflict-skipped source records none, so it stays not deletable (T-F260).
    private static async Task<List<SourceResult>> ProcessSeparateArchivesAsync(
        ArchiveOptions options, string extension, ConflictResolver conflictResolver,
        ArchiveResultSink sink, IProgress<ProgressReport>? progress, CancellationToken cancellationToken)
    {
        var sortedSourcePaths = options.SourcePaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        var sources = new List<SourceResult>();

        foreach (string sourcePath in sortedSourcePaths)
        {
            // T-F260/T-F245: throw, never end the loop with a result that looks finished.
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            {
                sink.Errors.Add(CoreMessages.Error(sourcePath, MessageCode.SourceNotFound, sourcePath));
                continue;
            }

            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            string destPath = Path.Combine(options.DestinationFolder, baseName + extension);

            (DestinationConflictOutcome outcome, string? resolvedDestPath) = await DestinationConflictResolver.ResolveAsync(
                destPath, onDiskConflict: File.Exists(destPath), sameRunConflict: false,
                conflictResolver, renameCandidate: p => ArchiveNaming.GetUniqueFilePath(p)).ConfigureAwait(false);
            if (outcome == DestinationConflictOutcome.Skip)
            {
                sink.SkippedFiles.Add(CoreMessages.Skip(sourcePath, MessageCode.ArchiveAlreadyExists, Path.GetFileName(destPath)));
                continue;
            }

            ArchiveOptions singleOptions = options with { SourcePaths = [sourcePath] };
            int errorsBefore = sink.Errors.Count, skippedBefore = sink.SkippedFiles.Count, createdBefore = sink.CreatedFiles.Count;
            await CompressToArchiveAsync(singleOptions, (resolvedDestPath, outcome == DestinationConflictOutcome.ProceedReplacingExisting), sink.CreatedFiles, sink.Errors, sink.SkippedFiles, progress, cancellationToken)
                .ConfigureAwait(false);
            sources.Add(SourceOutcomeRules.Classify(sourcePath,
                produced: sink.CreatedFiles.Count > createdBefore,
                clean: sink.Errors.Count == errorsBefore && sink.SkippedFiles.Count == skippedBefore));
        }

        return sources;
    }

    // Runs one tar.exe -cf invocation writing to an ArchiveTempFile path, then moves it to
    // destPath only if at least one entry was actually written — mirrors ZipArchiveService's
    // temp-then-commit pattern (no partial files on cancel or failure, CLAUDE.md hard
    // constraint). Reparse-point sources are skipped (T-F23 precedent); missing sources are
    // reported as ArchiveError, matching ZipArchiveService.ArchiveAsync's per-item handling.
    private static async Task CompressToArchiveAsync(
        ArchiveOptions options,
        (string Path, bool ReplacesExisting) destination,
        List<string> createdFiles,
        List<ArchiveError> errors,
        List<SkippedFile> skippedFiles,
        IProgress<ProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        // T-F321: from the conflict decision, not a second look at the disk - an archive that
        // appeared since then is another run's, and the commit must not replace it.
        (string destPath, bool replacesExisting) = destination;
        string tempPath = ArchiveTempFile.Create(destPath);
        // T-F171: decided here, created only on the first collision, so the finally below cleans it
        // up even when staging stops partway through.
        TarCollisionStaging.SweepStale();
        string collisionStagingDir = TarCollisionStaging.NewDirectoryPath(Path.GetTempPath());
        var stagedJunctions = new List<string>();

        try
        {
            var tarArgs = new List<string>();
            AppendCompressionFilterArgs(tarArgs, options.Format, options.CompressionLevel);
            // T-F316: tar.exe walks a source folder itself, and when the destination lies inside
            // one it would pack this run's own temporary file. The name is Pakko's own (ASCII, no
            // pattern characters), and tar.exe matches it at any depth.
            tarArgs.Add("--exclude");
            tarArgs.Add(Path.GetFileName(tempPath));
            tarArgs.Add("-v");
            tarArgs.Add("-cf");
            tarArgs.Add(tempPath);
            // T-F283/T-F273: the names go to tar.exe as a list on its stdin, never as arguments —
            // a selected name could otherwise be read as one of its options, and a long selection
            // would overflow the command line.
            tarArgs.Add("-T");
            tarArgs.Add("-");

            var sortedSourcePaths = options.SourcePaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            var nameList = new List<string>();
            (int entryCount, long totalEntriesForProgress, long totalBytesForProgress) =
                AppendSourcesToNameList(nameList, sortedSourcePaths, errors, skippedFiles, collisionStagingDir, stagedJunctions,
                    ownPaths: [tempPath, destPath]);

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
                ArchiveTempFile.RemoveOldArchiveInsideSources(destPath, options.SourcePaths);
                byte[] nameListBytes = TarCommandLineEncoding.EncodeLines(nameList);
                (int exitCode, _, string? stdErr) = await RunUnsandboxedTarAsync(tarArgs, nameListBytes, OnVerboseLine, cancellationToken).ConfigureAwait(false);

                if (exitCode != 0 || !File.Exists(tempPath))
                {
                    TryDeleteBestEffort(tempPath);
                    errors.Add(CoreMessages.Error(destPath, MessageCode.TarCreationFailed, DescribeCreationFailure(stdErr)));
                    return;
                }

                createdFiles.Add(await ArchiveTempFile.CommitAsync(tempPath, destPath, replacesExisting, cancellationToken).ConfigureAwait(false));
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
                errors.Add(CoreMessages.Error(destPath, CoreMessages.Wrap(MessageCode.CannotCreateArchive, ex), ex));
            }
            catch (UnauthorizedAccessException ex)
            {
                TryDeleteBestEffort(tempPath);
                errors.Add(CoreMessages.Error(destPath, CoreMessages.Text(MessageCode.AccessDeniedCreatingArchive, CoreMessages.Detail(ex)), ex));
            }
        }
        finally
        {
            if (Directory.Exists(collisionStagingDir))
                DeleteStagingDirectory(collisionStagingDir, stagedJunctions);
        }
    }

    // T-F171: each junction is removed on its own first (a non-recursive delete removes only the
    // link), and the staging folder is deleted recursively only once every junction is gone — a
    // recursive delete never reaches a folder that points at the user's own files.
    private static void DeleteStagingDirectory(string stagingDir, List<string> junctions)
    {
        foreach (string junction in junctions)
        {
            try
            {
                Directory.Delete(junction, recursive: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return; // the staging folder stays in %TEMP% rather than risk the junction's target
            }
        }
        try { Directory.Delete(stagingDir, recursive: true); } catch { /* best-effort */ }
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
    // T-F168: tar.exe has no --transform on this bundled bsdtar build (confirmed empirically —
    // "Option --transform is not supported"), so unlike ZipArchiveService (which writes entries
    // programmatically and can just pick a different in-archive name), the only way to give a
    // colliding FILE source a distinct entry name is to stage a real renamed copy and point tar.exe
    // at that instead. T-F171: a colliding FOLDER source is staged as a junction under the new name
    // (no copy); a folder on a network share cannot be, and is refused with a message.
    // T-F283: every name is one line of tar.exe's "-T -" list. In that list only an exact "-C" line
    // is special (the next line is the folder to change to); no other line is read as an option.
    private static (int EntryCount, long TotalEntriesForProgress, long TotalBytesForProgress) AppendSourcesToNameList(
        List<string> nameList, IReadOnlyList<string> sortedSourcePaths, List<ArchiveError> errors, List<SkippedFile> skippedFiles,
        string collisionStagingDir, List<string> stagedJunctions, string[] ownPaths)
    {
        int entryCount = 0;
        long totalEntriesForProgress = 0;
        long totalBytesForProgress = 0;
        var claims = new SourceNameClaims(collisionStagingDir, stagedJunctions);

        foreach (string sourcePath in sortedSourcePaths)
        {
            if (ArchiveEntrySecurity.IsReparsePoint(sourcePath))
            {
                skippedFiles.Add(CoreMessages.Skip(sourcePath, MessageCode.LinkNotArchived));
                continue;
            }

            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            {
                errors.Add(CoreMessages.Error(sourcePath, MessageCode.SourceNotFound, sourcePath));
                continue;
            }

            string fullSource = Path.GetFullPath(sourcePath);
            bool isDriveRoot = Path.GetDirectoryName(fullSource) is null;
            fullSource = SubstDrive.Resolve(fullSource);

            // T-F266/T-F204: tar.exe receives this path and walks the folder itself, so every
            // name it will touch must survive its ANSI command line / path conversion. Refused
            // before tar.exe runs (U+2713 crashed it, U+FF02 injected options).
            (long entries, long bytes, string? unrepresentable) = CountRecursiveEntriesAndBytes(fullSource);
            if (unrepresentable is not null)
            {
                errors.Add(CoreMessages.Error(sourcePath, TarArgumentEncodingException.Describe(unrepresentable, TarCommandLineEncoding.AnsiCodePage)));
                continue;
            }

            bool appended = isDriveRoot
                ? claims.TryAppendDriveRootLines(nameList, sourcePath, fullSource, ownPaths, new ArchiveResultLists(errors, skippedFiles))
                : claims.TryAppendLines(nameList, sourcePath, fullSource, errors);
            if (!appended)
                continue;

            entryCount++;
            totalEntriesForProgress += entries;
            totalBytesForProgress += bytes;
        }

        return (entryCount, totalEntriesForProgress, totalBytesForProgress);
    }

    private sealed record ArchiveResultLists(List<ArchiveError> Errors, List<SkippedFile> SkippedFiles);

    // T-F171: every source claims its name, file or folder. A clashing file is staged as a renamed
    // copy, a clashing folder as a junction under the new name; a clashing folder on a network share
    // cannot be, and is refused. Null means the source was refused and its error is recorded.
    private sealed class SourceNameClaims(string stagingDir, List<string> stagedJunctions)
    {
        private readonly HashSet<string> _claimed = new(StringComparer.OrdinalIgnoreCase);

        // False when the source was refused (its error is recorded).
        public bool TryAppendLines(List<string> nameList, string sourcePath, string fullSource, List<ArchiveError> errors)
        {
            if (Claim(sourcePath, fullSource, Path.GetDirectoryName(fullSource)!, Path.GetFileName(fullSource), errors) is not { } claimed)
                return false;

            AppendClaimed(nameList, claimed);
            return true;
        }

        // T-F285: tar.exe (bsdtar 3.8.8) cannot visit a drive root, as an argument or as "." under
        // "-C X:\" ("Couldn't visit directory"), on a real volume, a network drive and a subst drive
        // alike. What the root holds is listed name by name instead; the entries come out the same.
        // Each name is then a source of its own, so three kinds are left out: the OS's own entries
        // (Hidden and System, as in the App's browser, T-F324: tar.exe cannot read "System Volume
        // Information" and would fail the whole run), this run's archive and temporary file when
        // the destination is the root itself, and links, as for any selected source.
        public bool TryAppendDriveRootLines(List<string> nameList, string sourcePath, string root, string[] ownPaths, ArchiveResultLists result)
        {
            const FileAttributes OsOwned = FileAttributes.Hidden | FileAttributes.System;
            FileSystemInfo[] children;
            try
            {
                children = new DirectoryInfo(root).GetFileSystemInfos();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Errors.Add(CoreMessages.Error(sourcePath, CoreMessages.Wrap(MessageCode.CannotCreateArchive, ex), ex));
                return false;
            }

            HashSet<string> own = new(ownPaths.Select(path => SubstDrive.Resolve(Path.GetFullPath(path))), StringComparer.OrdinalIgnoreCase);
            children = [.. children.Where(child => (child.Attributes & OsOwned) != OsOwned && !own.Contains(child.FullName))];
            if (children.Length == 0)
            {
                result.SkippedFiles.Add(CoreMessages.Skip(sourcePath, MessageCode.NothingToArchive, sourcePath));
                return false;
            }

            bool any = false;
            foreach (FileSystemInfo child in children)
            {
                if (child.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    result.SkippedFiles.Add(CoreMessages.Skip(child.FullName, MessageCode.LinkNotArchived));
                    continue;
                }
                if (Claim(sourcePath, child.FullName, root, child.Name, result.Errors) is not { } claimed)
                    continue;
                AppendClaimed(nameList, claimed);
                any = true;
            }
            return any;
        }

        private static void AppendClaimed(List<string> nameList, (string Parent, string Name) claimed)
        {
            nameList.Add("-C");
            nameList.Add(claimed.Parent);
            nameList.Add(claimed.Name == "-C" ? "./-C" : claimed.Name);
        }

        private (string Parent, string Name)? Claim(string sourcePath, string fullSource, string parent, string name, List<ArchiveError> errors)
        {
            if (_claimed.Add(name))
                return (parent, name);

            bool isFolder = Directory.Exists(fullSource);
            if (isFolder && DirectoryJunction.IsNetworkPath(fullSource))
            {
                errors.Add(CoreMessages.Error(sourcePath, MessageCode.TarSourceNameCollisionNotAdded));
                return null;
            }
            string uniqueName = GetUniqueEntryName(name, _claimed);
            string stagedPath = Path.Combine(stagingDir, uniqueName);
            try
            {
                Directory.CreateDirectory(stagingDir);
                if (isFolder)
                {
                    DirectoryJunction.Create(stagedPath, fullSource);
                    stagedJunctions.Add(stagedPath);
                }
                else
                {
                    File.Copy(fullSource, stagedPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add(CoreMessages.Error(sourcePath, isFolder
                    ? CoreMessages.Text(MessageCode.TarSourceNameCollisionNotAdded)
                    : CoreMessages.Wrap(MessageCode.CannotCreateArchive, ex), ex));
                return null;
            }
            _claimed.Add(uniqueName);
            return (stagingDir, uniqueName);
        }

        // Same "name (1)", "name (2)", ... convention as ArchiveNaming.GetUniqueFilePath, but checked against
        // an in-memory set of already-claimed entry names rather than disk existence — the candidate
        // doesn't exist on disk yet (it's about to be staged into a fresh temp directory).
        private static string GetUniqueEntryName(string name, HashSet<string> claimedNames) =>
            ArchiveNaming.GetUniqueName(name, candidate => candidate == name || claimedNames.Contains(candidate));
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
    // T-F266: also returns the first path tar.exe would receive altered (null if none) — the
    // source's own full path, then every name beneath it, in the same single walk.
    internal static (long EntryCount, long TotalBytes, string? Unrepresentable) CountRecursiveEntriesAndBytes(string sourcePath)
    {
        string? unrepresentable = TarCommandLineEncoding.IsRepresentable(sourcePath) ? null : sourcePath;

        if (!Directory.Exists(sourcePath))
            return (1, FileLengthOrZero(sourcePath), unrepresentable); // plain file (or something that no longer exists by the time we get here)

        // T-F237: the shared iterative walker (the root's own Directory entry is its tar entry).
        // Every name tar.exe will meet is checked, reparse points and unreadable folders included:
        // tar.exe receives those names even though the walk does not enter them. Unreadable
        // folders only make the progress estimate low — the Math.Min(99, ...) clamp tolerates it.
        long count = 0;
        long totalBytes = 0;
        foreach (WalkEntry entry in DirectoryWalker.Walk(sourcePath))
        {
            bool isRoot = count == 0; // the walk always starts with the root itself
            count++;
            if (unrepresentable is null && !isRoot && !TarCommandLineEncoding.IsRepresentable(entry.Info.Name))
                unrepresentable = entry.Info.FullName;
            if (entry.Kind == WalkEntryKind.File)
                totalBytes += FileLengthOrZero((FileInfo)entry.Info);
        }

        return (count, totalBytes, unrepresentable);
    }

    // Anything unreadable or already gone counts 0 (best-effort estimate).
    private static long FileLengthOrZero(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.Directory) == 0 ? new FileInfo(path).Length : 0;
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static long FileLengthOrZero(FileInfo file)
    {
        try { return file.Length; }
        catch (IOException) { return 0; }
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
    // why this is safe to run outside the AppContainer) — no AppContainer, no Job Object, but the
    // same launcher as every sandboxed run, so the child inherits only its own pipes (T-F244
    // item 5: a Process.Start child inherited every inheritable handle in this process).
    // onStdErrLine is invoked once per non-empty stderr line as it streams in — tar.exe's "-v"
    // writes each added entry's "a <name>" line to STDERR during creation (confirmed
    // empirically; NOT stdout), so this is how per-entry progress is derived.
    private static Task<(int ExitCode, string StdOut, string StdErr)> RunUnsandboxedTarAsync(
        IReadOnlyList<string> arguments,
        byte[] stdInData,
        Action<string>? onStdErrLine,
        CancellationToken cancellationToken)
        => SandboxedProcessLauncher.RunAsync(
            TarExecutablePath, arguments,
            new ProcessLaunchOptions(OnStdErrLine: onStdErrLine, OutputEncoding: TarOutputEncoding.Current, StdInData: stdInData), cancellationToken);

    // T-F204: tar.exe's own words for a name it cannot show in the locale's code page (or an
    // empty one) — the archive is not damaged, this system just cannot name the file.
    private const string UnreadableNameMessage = "empty or unreadable filename";

    internal static CoreText DescribeFailure(string stdErr)
    {
        string text = stdErr.Trim();
        return text.Contains(UnreadableNameMessage, StringComparison.Ordinal)
            ? CoreMessages.Text(MessageCode.TarUnreadableNames, TarOutputEncoding.Current.CodePage.ToString(CultureInfo.InvariantCulture), text)
            : CoreText.Raw(text);
    }

    // T-F215: "-v" writes one "a <name>" progress line per entry to stderr, before any error —
    // the failure message keeps only the lines that say what went wrong.
    private static CoreText DescribeCreationFailure(string stdErr) =>
        CoreText.Raw(string.Join(Environment.NewLine, SplitLines(stdErr).Where(line => !line.StartsWith("a ", StringComparison.Ordinal))));

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
        // own behavior). Rooted paths (leading '/', UNC "\\server\share", or "C:/...") — tar.exe
        // strips the drive letter and keeps these contained (confirmed empirically), but reject
        // outright rather than trust that sanitization. T-F204 follow-up: both separators — this
        // used to split on '/' only, so "..\evil.txt" passed the pre-scan.
        if (ArchiveEntrySecurity.HasUnsafePath(entryName))
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

    // T-F146: internal (was private) — AntivirusScanService catches this the same way
    // ExtractSingleArchiveAsync's own callers do, to map a rejected archive to an Inconclusive
    // finding rather than an unhandled throw.
    internal sealed class TarArchiveRejectedException(CoreText text) : Exception(text.English), ICoreTextSource // NOSONAR: S3871 — deliberately internal, never escapes Archiver.Core's public surface (always caught and converted to ArchiveError/Inconclusive, per this project's "services never throw to callers" rule); public would be pure API-surface bloat
    {
        public CoreText Text { get; } = text;
    }
}
