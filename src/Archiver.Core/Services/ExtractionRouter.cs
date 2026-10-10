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

        ArchiveResult zipResult = zipPaths.Count > 0
            ? await _archiveService.ExtractAsync(
                options with { ArchivePaths = zipPaths, OpenDestinationFolder = false },
                zipSlice ?? progress, cancellationToken).ConfigureAwait(false)
            : EmptyResult();

        IProgress<ProgressReport>? tarProgress = mixed
            ? new SliceProgress(progress!, zipSliceEnd, 100, bytesBefore: zipSlice!.LastTotalBytes)
            : progress;
        ArchiveResult tarResult = tarPaths.Count > 0
            ? await _tarService.ExtractAsync(
                options with { ArchivePaths = tarPaths, OpenDestinationFolder = false },
                tarProgress, cancellationToken).ConfigureAwait(false)
            : EmptyResult();

        var merged = new ArchiveResult
        {
            CreatedFiles = [.. zipResult.CreatedFiles, .. tarResult.CreatedFiles],
            Errors = [.. zipResult.Errors, .. tarResult.Errors],
            SkippedFiles = [.. zipResult.SkippedFiles, .. tarResult.SkippedFiles, .. unsupported],
            Warnings = [.. zipResult.Warnings, .. tarResult.Warnings],
            KeptExistingFiles = [.. zipResult.KeptExistingFiles, .. tarResult.KeptExistingFiles],
            // T-F260: unsupported/policy-blocked paths get no SourceResult, so they are never
            // deletable; a cancel in either engine throws before this merge is reached.
            Sources = [.. zipResult.Sources, .. tarResult.Sources],
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
        CancellationToken cancellationToken = default)
    {
        if (verifyRecoveryData)
            return await TestWithRecoveryDataAsync(archivePaths, progress, resolvePasswordAsync, cancellationToken).ConfigureAwait(false);
        ArchiveFormatPolicy.Classification classification = await ArchiveFormatPolicy
            .ClassifyAsync(archivePaths, _tarCapabilities, _policy, cancellationToken).ConfigureAwait(false);
        return await TestClassifiedAsync(classification, progress, resolvePasswordAsync, cancellationToken).ConfigureAwait(false);
    }

    // T-F261: same classifier as extraction. tar.exe has no test mode, so a tar-family path
    // that policy and capabilities would allow is still only reported, never opened.
    private async Task<ArchiveResult> TestClassifiedAsync(
        ArchiveFormatPolicy.Classification classification,
        IProgress<ProgressReport>? progress,
        Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync,
        CancellationToken cancellationToken)
    {
        ArchiveResult zipResult = classification.ZipPaths.Count > 0
            ? await _archiveService.TestAsync(classification.ZipPaths, progress, resolvePasswordAsync, cancellationToken).ConfigureAwait(false)
            : EmptyResult();

        IEnumerable<SkippedFile> untestable = classification.TarPaths
            .Select(path => CoreMessages.Skip(path, MessageCode.NoTestCapability));
        return zipResult with { SkippedFiles = [.. zipResult.SkippedFiles, .. untestable, .. classification.Unsupported] };
    }

    // T-F275 step 3: the engines test what they can, then each archive with a set is checked by
    // it - one climb, the test's share by the bytes it reads, the check's by the archives' sizes.
    private async Task<ArchiveResult> TestWithRecoveryDataAsync(
        IReadOnlyList<string> paths,
        IProgress<ProgressReport>? progress,
        Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync,
        CancellationToken cancellationToken)
    {
        RecoveryTestStep.Plan plan = await Task.Run(() => RecoveryTestStep.Locate(paths, _policy, cancellationToken), cancellationToken).ConfigureAwait(false);
        ArchiveFormatPolicy.Classification classification = await ArchiveFormatPolicy
            .ClassifyAsync(plan.ArchivePaths, _tarCapabilities, _policy, cancellationToken).ConfigureAwait(false);

        long[] checkLengths = [.. plan.Sets.Select(s => TotalLength([s.ArchivePath]))];
        double checkTotal = Math.Max(1, checkLengths.Sum());
        long testTotal = TotalLength(classification.ZipPaths);
        int testEnd = plan.Sets.Count == 0 ? 100 : (int)(100 * testTotal / (testTotal + checkTotal));
        RecoveryClimb? climb = progress is null || plan.Sets.Count == 0 ? null : new RecoveryClimb(progress, testEnd);
        IProgress<ProgressReport>? testProgress = climb is null ? progress : new SliceProgress(climb, 0, testEnd, bytesBefore: 0);

        ArchiveResult tested = await TestClassifiedAsync(classification, testProgress, resolvePasswordAsync, cancellationToken).ConfigureAwait(false);
        // Keyed by full path: a check's archive may come from a .par2 path, spelled unlike the archive path given.
        var passed = new HashSet<string>(
            tested.Sources.Where(s => s.Outcome == SourceOutcome.Completed && !tested.Errors.Any(e => e.SourcePath == s.Path)).Select(s => RecoveryTestStep.Key(s.Path)),
            StringComparer.OrdinalIgnoreCase);

        var errors = new List<ArchiveError>(plan.Errors);
        var warnings = new List<ArchiveWarning>(plan.Warnings);
        var checks = new List<RecoveryCheck>(plan.Checks);
        await Task.Run(() =>
        {
            double before = 0;
            for (int i = 0; i < plan.Sets.Count; i++)
            {
                RecoveryTestStep.Found found = plan.Sets[i];
                double offset = before;
                long weight = checkLengths[i];
                before += weight;
                (RecoveryCheck? check, ArchiveError? error, ArchiveWarning? warning) = RecoveryTestStep.Check(
                    found, passed.Contains(RecoveryTestStep.Key(found.ArchivePath)), f => climb?.Check((offset + f * weight) / checkTotal), cancellationToken);
                if (check is not null)
                    checks.Add(check);
                if (error is not null)
                    errors.Add(error);
                if (warning is not null)
                    warnings.Add(warning);
            }
        }, cancellationToken).ConfigureAwait(false);
        climb?.Check(1);

        // A tar-family archive that its set could check was tested after all.
        var checkedBySet = new HashSet<string>(
            checks.Where(c => c.State != RecoveryState.Unusable).Select(c => RecoveryTestStep.Key(c.ArchivePath)), StringComparer.OrdinalIgnoreCase);
        return tested with
        {
            Errors = [.. tested.Errors, .. errors],
            Warnings = [.. tested.Warnings, .. warnings],
            SkippedFiles = [.. tested.SkippedFiles.Where(s => s.Text?.Code != MessageCode.NoTestCapability || !checkedBySet.Contains(RecoveryTestStep.Key(s.Path)))],
            RecoveryChecks = checks,
        };
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
