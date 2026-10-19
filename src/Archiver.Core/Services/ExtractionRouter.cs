using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Recovery;

namespace Archiver.Core.Services;

/// <inheritdoc cref="IExtractionRouter"/>
public sealed class ExtractionRouter : IExtractionRouter
{
    private readonly IArchiveService _archiveService;
    private readonly ITarService _tarService;
    private readonly Func<Task<TarCapabilities>> _tarCapabilities;
    private readonly GroupPolicyOptions _policy;

    /// <summary>Creates the router over both engines, with the capabilities of tar.exe already known.</summary>
    public ExtractionRouter(
        IArchiveService archiveService,
        ITarService tarService,
        TarCapabilities tarCapabilities,
        GroupPolicyOptions groupPolicyOptions)
        : this(archiveService, tarService, () => Task.FromResult(tarCapabilities), groupPolicyOptions)
    {
    }

    // T-F350: the capabilities are asked for only when a tar-family archive is met.
    internal ExtractionRouter(
        IArchiveService archiveService,
        ITarService tarService,
        Func<Task<TarCapabilities>> tarCapabilities,
        GroupPolicyOptions groupPolicyOptions)
    {
        _archiveService = archiveService;
        _tarService = tarService;
        _tarCapabilities = tarCapabilities;
        _policy = groupPolicyOptions ?? throw new ArgumentNullException(nameof(groupPolicyOptions));
    }

    /// <inheritdoc/>
    public async Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // T-F146: classification (zip/tar/unsupported split + Group Policy gating) is now shared
        // with AntivirusScanService via ArchiveFormatPolicy, so a scan can never silently drift
        // from what real extraction would allow/refuse. Behavior here is unchanged.
        ArchiveFormatPolicy.Classification classification = await ArchiveFormatPolicy
            .ClassifyAsync(options.ArchivePaths, _tarCapabilities, _policy, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> zipPaths = classification.ZipPaths;
        IReadOnlyList<string> tarPaths = classification.TarPaths;
        IReadOnlyList<SkippedFile> unsupported = classification.Unsupported;

        // T-F306: with both kinds selected, ZIP runs first and tar second, each in its own slice of
        // one climb. T-F142 had given tar no progress at all here (a second 0->100 climb would have
        // dropped the bar), so the bar sat at 100% through the whole tar part.
        bool mixed = progress is not null && zipPaths.Count > 0 && tarPaths.Count > 0;
        int zipSliceEnd = mixed ? ZipSharePercent(zipPaths, tarPaths) : 100;
        SliceProgress? zipSlice = mixed ? new SliceProgress(progress!, 0, zipSliceEnd, bytesBefore: 0) : null;

        // T-F420: one bookkeeping of the paths written, so a tar file replacing a ZIP's leaves that
        // ZIP not deletable (and Explorer's "Extract here" over both kinds keeps twins apart).
        var names = options.SharedNames ?? new ExtractionNameRegistry();
        ArchiveResult zipResult = zipPaths.Count > 0
            ? await _archiveService.ExtractAsync(
                options with { ArchivePaths = zipPaths, OpenDestinationFolder = false, SharedNames = names },
                zipSlice ?? progress, cancellationToken).ConfigureAwait(false)
            : EmptyResult();

        IProgress<ProgressReport>? tarProgress = mixed
            ? new SliceProgress(progress!, zipSliceEnd, 100, bytesBefore: zipSlice!.LastTotalBytes)
            : progress;
        ArchiveResult tarResult = tarPaths.Count > 0
            ? await _tarService.ExtractAsync(
                options with { ArchivePaths = tarPaths, OpenDestinationFolder = false, SharedNames = names },
                tarProgress, cancellationToken).ConfigureAwait(false)
            : EmptyResult();

        var merged = new ArchiveResult
        {
            CreatedFiles = [.. zipResult.CreatedFiles, .. tarResult.CreatedFiles],
            Errors = [.. zipResult.Errors, .. tarResult.Errors],
            SkippedFiles = [.. zipResult.SkippedFiles, .. tarResult.SkippedFiles, .. unsupported],
            Warnings = [.. zipResult.Warnings, .. tarResult.Warnings],
            KeptExistingFiles = [.. zipResult.KeptExistingFiles, .. tarResult.KeptExistingFiles],
            UserSkippedEntryCount = zipResult.UserSkippedEntryCount + tarResult.UserSkippedEntryCount,
            // T-F260: unsupported/policy-blocked paths get no SourceResult, so they are never
            // deletable; a cancel in either engine throws before this merge is reached.
            Sources = SourceOutcomeRules.DowngradeNotWhollyOnDisk([.. zipResult.Sources, .. tarResult.Sources], names),
        };

        if (merged.Success && options.OpenDestinationFolder)
        {
            ExplorerLauncher.OpenFolder(options.DestinationFolder);
        }

        return merged;
    }

    /// <inheritdoc/>
    public async Task<ArchiveResult> TestAsync(
        IReadOnlyList<string> archivePaths,
        IProgress<ProgressReport>? progress = null,
        Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync = null,
        bool verifyRecoveryData = false,
        Func<string, bool, bool>? entryFilter = null,
        CancellationToken cancellationToken = default)
    {
        if (verifyRecoveryData)
            return await TestWithRecoveryDataAsync(archivePaths, progress, resolvePasswordAsync, entryFilter, cancellationToken).ConfigureAwait(false);
        ArchiveFormatPolicy.Classification classification = await ArchiveFormatPolicy
            .ClassifyAsync(archivePaths, _tarCapabilities, _policy, cancellationToken).ConfigureAwait(false);
        return await TestClassifiedAsync(classification, progress, resolvePasswordAsync, entryFilter, cancellationToken).ConfigureAwait(false);
    }

    // T-F261: same classifier as extraction. T-F397: a tar-family archive is tested by the tar
    // engine (tar.exe reads every entry and writes nothing), ZIP first and tar second in one
    // climb, as extraction (T-F306).
    private async Task<ArchiveResult> TestClassifiedAsync(
        ArchiveFormatPolicy.Classification classification,
        IProgress<ProgressReport>? progress,
        Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync,
        Func<string, bool, bool>? entryFilter,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> zipPaths = classification.ZipPaths;
        IReadOnlyList<string> tarPaths = classification.TarPaths;
        bool mixed = progress is not null && zipPaths.Count > 0 && tarPaths.Count > 0;
        int zipSliceEnd = mixed ? ZipSharePercent(zipPaths, tarPaths) : 100;

        IProgress<ProgressReport>? zipProgress = mixed ? new SliceProgress(progress!, 0, zipSliceEnd, bytesBefore: 0) : progress;
        IProgress<ProgressReport>? tarProgress = mixed ? new SliceProgress(progress!, zipSliceEnd, 100, bytesBefore: 0) : progress;

        ArchiveResult zipResult = zipPaths.Count > 0
            ? await _archiveService.TestAsync(zipPaths, zipProgress, resolvePasswordAsync, entryFilter, cancellationToken).ConfigureAwait(false)
            : EmptyResult();
        ArchiveResult tarResult = tarPaths.Count > 0
            ? await _tarService.TestAsync(tarPaths, tarProgress, entryFilter, cancellationToken).ConfigureAwait(false)
            : EmptyResult();

        return zipResult with
        {
            Errors = [.. zipResult.Errors, .. tarResult.Errors],
            Warnings = [.. zipResult.Warnings, .. tarResult.Warnings],
            SkippedFiles = [.. zipResult.SkippedFiles, .. tarResult.SkippedFiles, .. classification.Unsupported],
            Sources = [.. zipResult.Sources, .. tarResult.Sources],
        };
    }

    // T-F275 step 3: the engines test what they can, then each archive with a set is checked by
    // it - one climb, the test's share by the bytes it reads, the check's by the archives' sizes.
    private async Task<ArchiveResult> TestWithRecoveryDataAsync(
        IReadOnlyList<string> paths,
        IProgress<ProgressReport>? progress,
        Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync,
        Func<string, bool, bool>? entryFilter,
        CancellationToken cancellationToken)
    {
        RecoveryTestStep.Plan plan = await Task.Run(() => RecoveryTestStep.Locate(paths, _policy, cancellationToken), cancellationToken).ConfigureAwait(false);
        ArchiveFormatPolicy.Classification classification = await ArchiveFormatPolicy
            .ClassifyAsync(plan.ArchivePaths, _tarCapabilities, _policy, cancellationToken).ConfigureAwait(false);

        long[] checkLengths = [.. plan.Sets.Select(s => TotalLength([s.ArchivePath]))];
        double checkTotal = Math.Max(1, checkLengths.Sum());
        long testTotal = TotalLength(classification.ZipPaths) + TotalLength(classification.TarPaths);
        int testEnd = plan.Sets.Count == 0 ? 100 : (int)(100 * testTotal / (testTotal + checkTotal));
        RecoveryClimb? climb = progress is null || plan.Sets.Count == 0 ? null : new RecoveryClimb(progress, testEnd);
        IProgress<ProgressReport>? testProgress = climb is null ? progress : new SliceProgress(climb, 0, testEnd, bytesBefore: 0);

        ArchiveResult tested = await TestClassifiedAsync(classification, testProgress, resolvePasswordAsync, entryFilter, cancellationToken).ConfigureAwait(false);
        // Keyed by full path: a check's archive may come from a .par2 path, spelled unlike the archive path given.
        // T-F397: a test that checked only the structure did not pass the contents. Counting it
        // would turn real damage into "a newer archive beside an older set". T-F410: nor did a
        // test of only the entries a filter passed.
        var passed = new HashSet<string>(
            tested.Sources
                .Where(s => entryFilter is null && s.Outcome == SourceOutcome.Completed && !tested.Errors.Any(e => e.SourcePath == s.Path)
                    && !tested.Warnings.Any(w => IsStructureOnly(w) && w.SourcePath == s.Path))
                .Select(s => RecoveryTestStep.Key(s.Path)),
            StringComparer.OrdinalIgnoreCase);

        await Task.Run(() => CheckSets(plan, checkLengths, passed, climb, cancellationToken), cancellationToken).ConfigureAwait(false);
        climb?.Check(1);

        // An archive its set could check had every byte compared: "only the structure" is no longer so.
        var checkedBySet = new HashSet<string>(
            plan.Checks.Where(c => c.State != RecoveryState.Unusable).Select(c => RecoveryTestStep.Key(c.ArchivePath)), StringComparer.OrdinalIgnoreCase);
        return tested with
        {
            Errors = [.. tested.Errors, .. plan.Errors],
            Warnings = [.. tested.Warnings.Where(w => !IsStructureOnly(w) || !checkedBySet.Contains(RecoveryTestStep.Key(w.SourcePath))), .. plan.Warnings],
            RecoveryChecks = plan.Checks,
        };
    }

    private static bool IsStructureOnly(ArchiveWarning warning) => warning.Text?.Code == MessageCode.TestedStructureOnly;

    // Each set's share of the check's part of the climb is its archive's size; what the checks
    // find joins what Locate already knew in the plan.
    private static void CheckSets(
        RecoveryTestStep.Plan plan, long[] lengths, HashSet<string> passed, RecoveryClimb? climb, CancellationToken cancellationToken)
    {
        double total = Math.Max(1, lengths.Sum());
        double before = 0;
        for (int i = 0; i < plan.Sets.Count; i++)
        {
            RecoveryTestStep.Found found = plan.Sets[i];
            double offset = before;
            long weight = lengths[i];
            before += weight;
            (RecoveryCheck? check, ArchiveError? error, ArchiveWarning? warning) = RecoveryTestStep.Check(
                found, passed.Contains(RecoveryTestStep.Key(found.ArchivePath)), f => climb?.Check((offset + f * weight) / total), cancellationToken);
            if (check is not null)
                plan.Checks.Add(check);
            if (error is not null)
                plan.Errors.Add(error);
            if (warning is not null)
                plan.Warnings.Add(warning);
        }
    }

    private static ArchiveResult EmptyResult() => new();

    // The ZIP part's share of the climb, by the archives' sizes on disk — the only measure both
    // kinds have before either runs. By count when no size can be read.
    private static int ZipSharePercent(IReadOnlyList<string> zipPaths, IReadOnlyList<string> tarPaths)
    {
        long zipBytes = TotalLength(zipPaths);
        long tarBytes = TotalLength(tarPaths);
        return zipBytes > 0 && tarBytes > 0
            ? (int)(zipBytes * 100.0 / ((double)zipBytes + tarBytes))
            : zipPaths.Count * 100 / (zipPaths.Count + tarPaths.Count);
    }

    private static long TotalLength(IReadOnlyList<string> paths)
    {
        long total = 0;
        foreach (string path in paths)
        {
            try
            {
                total += new FileInfo(path).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // best-effort: an unreadable size only changes how the bar is divided
            }
        }
        return total;
    }

    // The test's reports pass through below testEnd and the check's fill the rest; never goes
    // back, and a check report is passed on only when the percent changes.
    private sealed class RecoveryClimb(IProgress<ProgressReport> inner, int testEnd) : IProgress<ProgressReport>
    {
        private readonly Lock _lock = new();
        private int _shown = -1;

        public void Report(ProgressReport value)
        {
            lock (_lock)
                _shown = Math.Max(_shown, value.Percent);
            inner.Report(value);
        }

        public void Check(double fraction)
        {
            int percent = testEnd + (int)((100 - testEnd) * Math.Clamp(fraction, 0, 1));
            lock (_lock)
            {
                if (percent <= _shown)
                    return;
                _shown = percent;
            }
            inner.Report(new ProgressReport { Percent = percent, Phase = ProgressPhase.VerifyingRecoveryData });
        }
    }

    // Maps one engine's 0-100 onto its slice. Bytes continue after the engine that ran before:
    // a byte count that went back down would be ignored by ProgressSpeedSampler. A report with
    // no byte total (several archives of one kind) stays without one.
    private sealed class SliceProgress(IProgress<ProgressReport> inner, int startPercent, int endPercent, long bytesBefore)
        : IProgress<ProgressReport>
    {
        public long LastTotalBytes { get; private set; }

        public void Report(ProgressReport value)
        {
            bool hasBytes = value.TotalBytes > 0;
            if (hasBytes)
                LastTotalBytes = value.TotalBytes;
            inner.Report(new ProgressReport
            {
                Percent = startPercent + Math.Clamp(value.Percent, 0, 100) * (endPercent - startPercent) / 100,
                BytesTransferred = hasBytes ? bytesBefore + value.BytesTransferred : 0,
                TotalBytes = hasBytes ? bytesBefore + value.TotalBytes : 0,
                CurrentFile = value.CurrentFile,
                Phase = value.Phase,
            });
        }
    }
}
