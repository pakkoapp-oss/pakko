using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Recovery;

namespace Archiver.Core.Services;

/// <inheritdoc cref="IRecoveryService"/>
public sealed class RecoveryService : IRecoveryService
{
    private const string RepairedSuffix = ".repaired";
    // A name taken between choosing it and the rename gets the next number; another process would
    // have to take every one of these first.
    private const int MaxNameAttempts = 10;
    private const int FileExists = 0x50;
    private const int AlreadyExists = 0xB7;

    private readonly GroupPolicyOptions _policy;
    private readonly Func<Task<IExtractionRouter>> _router;

    /// <summary><paramref name="router"/> tests a ZIP the set calls damaged, as the test does.</summary>
    public RecoveryService(GroupPolicyOptions policy, Func<Task<IExtractionRouter>> router)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _router = router ?? throw new ArgumentNullException(nameof(router));
    }

    /// <inheritdoc/>
    public async Task<ArchiveResult> RepairAsync(
        RepairOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RecoveryTestStep.Plan plan = await Task.Run(() => RecoveryTestStep.Locate(options.Paths, _policy, cancellationToken), cancellationToken).ConfigureAwait(false);
        var run = new Run(plan);
        RefuseArchivesWithoutASet(plan, run);

        long[] lengths = [.. plan.Sets.Select(s => Math.Max(1, s.Match.Set.FileLength))];
        double total = lengths.Sum();
        double before = 0;
        var climb = new Climb(progress);
        for (int i = 0; i < plan.Sets.Count; i++)
        {
            double offset = before / total;
            double share = lengths[i] / total;
            before += lengths[i];
            await RepairOneAsync(plan.Sets[i], options, run, (phase, f) => climb.Report(phase, offset + f * share), cancellationToken).ConfigureAwait(false);
        }
        climb.Finish();

        return new ArchiveResult
        {
            CreatedFiles = run.Created,
            Errors = run.Errors,
            Warnings = run.Warnings,
            RecoveryChecks = run.Checks,
        };
    }

    // The test treats a missing or unusable set as a note beside its own verdict. Repair was asked
    // for, so the same finding is the reason nothing was done.
    private void RefuseArchivesWithoutASet(RecoveryTestStep.Plan plan, Run run)
    {
        var withSet = new HashSet<string>(plan.Sets.Select(s => RecoveryTestStep.Key(s.ArchivePath)), StringComparer.OrdinalIgnoreCase);
        var refused = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in plan.ArchivePaths)
        {
            if (withSet.Contains(RecoveryTestStep.Key(path)) || !refused.Add(RecoveryTestStep.Key(path)))
                continue;
            ArchiveWarning? found = plan.Warnings.FirstOrDefault(w => w.SourcePath == path);
            if (_policy.DisableRecoveryData)
                run.Errors.Add(CoreMessages.Error(path, MessageCode.RecoveryDataDisabled));
            else if (found?.Text is { } text)
                run.Errors.Add(CoreMessages.Error(path, text));
            else
                run.Errors.Add(CoreMessages.Error(path, MessageCode.RecoveryDataNotFound));
        }
        run.Warnings.RemoveAll(w => refused.Contains(RecoveryTestStep.Key(w.SourcePath)));
    }

    private async Task RepairOneAsync(
        RecoveryTestStep.Found found, RepairOptions options, Run run, Action<ProgressPhase, double> progress, CancellationToken cancellationToken)
    {
        const double verifyShare = 0.2;
        Par2Verification verification;
        try
        {
            verification = await Task.Run(
                () => Par2Verifier.Verify(found.ArchivePath, found.Match.Set, f => progress(ProgressPhase.VerifyingRecoveryData, f * verifyShare), cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            run.Errors.Add(CoreMessages.Error(found.ArchivePath, CoreMessages.FromException(ex), ex));
            return;
        }

        bool testPassed = await TestsAsIntactAsync(found.ArchivePath, verification, cancellationToken).ConfigureAwait(false);
        (RecoveryCheck check, ArchiveError? error, ArchiveWarning? warning) = RecoveryTestStep.Describe(found, verification, testPassed);
        if (warning is not null)
            run.Warnings.Add(warning);
        if (check.State != RecoveryState.Repairable)
        {
            run.Checks.Add(check);
            if (error is not null)
                run.Errors.Add(error);
            return;
        }

        (Par2RepairResult? result, string output, ArchiveError? failure) = await Task.Run(
            () => WriteRepairedCopy(found, verification, options, f => progress(ProgressPhase.RepairingArchive, verifyShare + f * (1 - verifyShare)), cancellationToken),
            cancellationToken).ConfigureAwait(false);
        if (failure is not null)
        {
            run.Checks.Add(check);
            run.Errors.Add(failure);
            return;
        }
        if (result!.Status != Par2RepairStatus.Repaired)
        {
            run.Checks.Add(check);
            run.Errors.Add(CoreMessages.Error(found.ArchivePath, MessageCode.RecoveryRepairCheckFailed));
            return;
        }

        CarryDownloadMark(found, output, options);
        run.Created.Add(output);
        run.Checks.Add(check with
        {
            State = RecoveryState.Repaired,
            RepairedPath = output,
            Text = CoreMessages.Text(MessageCode.RecoveryDataRepaired, result.RepairedSlices, check.Blocks, Path.GetFileName(output)),
        });
    }

    // The rule of the test: a ZIP that its own test passes while the set disagrees is a newer
    // archive beside an older set, not damage, and nothing is rebuilt from that set. The ZIP is
    // tested only when the set calls it damaged, so a good archive is read once. An archive that
    // could not be tested (encrypted, refused by policy) did not pass.
    private async Task<bool> TestsAsIntactAsync(string archivePath, Par2Verification verification, CancellationToken cancellationToken)
    {
        if (verification.Status == Par2VerifyStatus.Intact || verification.FileMissing
            || ArchiveFormatDetector.Detect(archivePath) != ArchiveFormat.Zip)
            return false;
        IExtractionRouter router = await _router().ConfigureAwait(false);
        ArchiveResult tested = await router.TestAsync([archivePath], cancellationToken: cancellationToken).ConfigureAwait(false);
        return tested.Errors.Count == 0 && tested.Sources.Any(s => s.Outcome == SourceOutcome.Completed);
    }

    private static (Par2RepairResult? Result, string Output, ArchiveError? Failure) WriteRepairedCopy(
        RecoveryTestStep.Found found, Par2Verification verification, RepairOptions options, Action<double> progress, CancellationToken cancellationToken)
    {
        string output = string.Empty;
        try
        {
            string folder = options.OutputDirectory ?? Path.GetDirectoryName(Path.GetFullPath(found.ArchivePath))!;
            Directory.CreateDirectory(folder);
            string preferred = Path.Combine(Path.GetFullPath(folder), RepairedName(found.ArchivePath));
            for (int attempt = 0; attempt < MaxNameAttempts; attempt++)
            {
                output = File.Exists(preferred) ? ArchiveNaming.GetUniqueFilePath(preferred) : preferred;
                try
                {
                    return (Par2Repairer.Repair(found.ArchivePath, found.Match.Set, verification, output, progress, cancellationToken), output, null);
                }
                catch (IOException ex) when ((ex.HResult & 0xFFFF) is FileExists or AlreadyExists)
                {
                    // the name was taken after it was chosen: the existing file stays, the next number is tried
                }
            }
            // The last try: a name taken yet again is reported, not retried.
            output = File.Exists(preferred) ? ArchiveNaming.GetUniqueFilePath(preferred) : preferred;
            return (Par2Repairer.Repair(found.ArchivePath, found.Match.Set, verification, output, progress, cancellationToken), output, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (null, output, CoreMessages.Error(found.ArchivePath, CoreMessages.Text(MessageCode.RecoveryRepairNotWritten, CoreMessages.FromException(ex)), ex));
        }
    }

    // "a.tar.gz" gives "a.repaired.tar.gz": the copy keeps an extension every tool recognizes.
    internal static string RepairedName(string archivePath)
    {
        string fileName = Path.GetFileName(archivePath);
        string baseName = ArchiveNaming.GetBaseName(archivePath);
        return baseName + RepairedSuffix + fileName[baseName.Length..];
    }

    // The copy is the archive again, so it is marked as the archive was. With the archive gone the
    // mark comes from the PAR2 files it was rebuilt from: never less marked than what it was made of.
    private void CarryDownloadMark(RecoveryTestStep.Found found, string output, RepairOptions options)
    {
        if (_policy.EffectiveMotwMode(options.ApplyDownloadMark) == MotwMode.Disabled)
            return;
        byte[]? mark = ArchiveEntrySecurity.ReadMotw(found.ArchivePath, MotwMode.AllFiles)
            ?? found.SetFiles.Select(f => ArchiveEntrySecurity.ReadMotw(f, MotwMode.AllFiles)).FirstOrDefault(m => m is not null);
        if (mark is not null)
            ArchiveEntrySecurity.TryWriteMotw(mark, output, MotwMode.AllFiles);
    }

    private sealed class Run(RecoveryTestStep.Plan plan)
    {
        public List<string> Created { get; } = [];
        public List<ArchiveError> Errors { get; } = [.. plan.Errors];
        public List<ArchiveWarning> Warnings { get; } = [.. plan.Warnings];
        public List<RecoveryCheck> Checks { get; } = [.. plan.Checks];
    }

    // One climb over all archives; the percent never goes back and 100 is sent once, at the end.
    private sealed class Climb(IProgress<ProgressReport>? progress)
    {
        private int _last = -1;

        public void Report(ProgressPhase phase, double fraction)
        {
            int percent = (int)Math.Clamp(fraction * 100, 0, 99);
            if (progress is null || percent <= _last)
                return;
            _last = percent;
            progress.Report(new ProgressReport { Percent = percent, Phase = phase });
        }

        public void Finish() => progress?.Report(new ProgressReport { Percent = 100, Phase = ProgressPhase.RepairingArchive });
    }
}
