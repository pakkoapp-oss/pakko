using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.Core.Recovery;

/// <summary>
/// The PAR2 part of <see cref="Interfaces.IExtractionRouter.TestAsync"/> (T-F275 step 3): finds
/// the set of each tested archive, or the archive of each <c>.par2</c> path, and checks it. Never
/// throws except <see cref="OperationCanceledException"/>.
/// </summary>
internal static class RecoveryTestStep
{
    /// <summary>A set to check against <see cref="ArchivePath"/>.</summary>
    internal sealed record Found(string ArchivePath, IReadOnlyList<string> SetFiles, Par2Match Match);

    /// <summary>What <see cref="Locate"/> found: the paths the engines test, the sets to check,
    /// and what is already known without checking any archive.</summary>
    internal sealed class Plan
    {
        public List<string> ArchivePaths { get; } = [];
        public List<Found> Sets { get; } = [];
        public List<ArchiveError> Errors { get; } = [];
        public List<ArchiveWarning> Warnings { get; } = [];
        public List<RecoveryCheck> Checks { get; } = [];
    }

    /// <summary>The one spelling of a path that tells two mentions of a file apart: a check's
    /// archive may come from a .par2 path, written unlike the archive path the user gave.</summary>
    internal static string Key(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path; // not a usable path; it only has to compare equal to itself
        }
    }

    internal static bool IsPar2Path(string path) => path.EndsWith(".par2", StringComparison.OrdinalIgnoreCase);

    internal static Plan Locate(IReadOnlyList<string> paths, GroupPolicyOptions policy, CancellationToken cancellationToken)
    {
        var plan = new Plan();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsPar2Path(path))
            {
                plan.ArchivePaths.Add(path);
                if (!policy.DisableRecoveryData && !seen.Contains(Key(path)))
                    LocateForArchive(path, plan, seen, cancellationToken);
            }
            else if (policy.DisableRecoveryData)
            {
                plan.Errors.Add(CoreMessages.Error(path, MessageCode.RecoveryDataDisabled));
            }
            else
            {
                LocateForPar2(path, plan, seen, cancellationToken);
            }
        }
        return plan;
    }

    // A set is optional here: a folder that cannot be listed means the archive is tested without one.
    private static void LocateForArchive(string archivePath, Plan plan, HashSet<string> seen, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> setFiles;
        try
        {
            setFiles = Par2SetLocator.SetFilesForTarget(archivePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return; // the test still runs; there is just no set to compare with
        }
        if (setFiles.Count == 0)
            return;

        Par2ReadResult read = Par2PacketReader.Read(setFiles, cancellationToken);
        if (read.Sets.Count == 0)
        {
            plan.Warnings.Add(CoreMessages.Warning(archivePath, CoreMessages.Text(MessageCode.RecoveryDataUnusable)));
            plan.Checks.Add(new RecoveryCheck { ArchivePath = archivePath, State = RecoveryState.Unusable, SetFiles = setFiles });
            return;
        }
        if (Par2SetLocator.Select(read.Sets, archivePath) is { } match)
        {
            seen.Add(Key(archivePath));
            plan.Sets.Add(new Found(archivePath, setFiles, match));
        }
        else
            plan.Warnings.Add(CoreMessages.Warning(archivePath, CoreMessages.Text(MessageCode.RecoveryDataForAnotherFile)));
    }

    // The user asked about this set, so every way it cannot be checked is an error.
    private static void LocateForPar2(string par2Path, Plan plan, HashSet<string> seen, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> setFiles;
        IReadOnlyList<string> candidates;
        try
        {
            if (!File.Exists(par2Path))
            {
                plan.Errors.Add(CoreMessages.Error(par2Path, MessageCode.SourceNotFound, par2Path));
                return;
            }
            setFiles = Par2SetLocator.SetFilesForPar2(par2Path);
            candidates = Par2SetLocator.TargetCandidates(par2Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            plan.Errors.Add(CoreMessages.Error(par2Path, CoreMessages.FromException(ex), ex));
            return;
        }

        Par2ReadResult read = Par2PacketReader.Read(setFiles, cancellationToken);
        if (read.Sets.Count == 0)
        {
            plan.Errors.Add(CoreMessages.Error(par2Path, MessageCode.RecoveryDataUnusable));
            return;
        }
        foreach (string candidate in candidates)
        {
            if (Par2SetLocator.Select(read.Sets, candidate) is { } match)
            {
                if (seen.Add(Key(candidate)))
                    plan.Sets.Add(new Found(candidate, setFiles, match));
                return;
            }
        }
        plan.Errors.Add(CoreMessages.Error(par2Path, MessageCode.RecoveryDataTargetNotFound));
    }

    /// <summary>Checks one archive against its set. <paramref name="testPassed"/>: the ZIP engine
    /// tested this archive and found nothing wrong, so a set that disagrees is taken to be one left
    /// from an earlier version of the archive (a rewrite without recovery data keeps the old set).</summary>
    internal static (RecoveryCheck? Check, ArchiveError? Error, ArchiveWarning? Warning) Check(
        Found found, bool testPassed, Action<double> progress, CancellationToken cancellationToken)
    {
        Par2Set set = found.Match.Set;
        Par2Verification verification;
        try
        {
            verification = Par2Verifier.Verify(found.ArchivePath, set, progress, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, CoreMessages.Error(found.ArchivePath, CoreMessages.FromException(ex), ex), null);
        }

        int blocks = set.Slices.Length;
        int damaged = verification.Status == Par2VerifyStatus.Intact ? 0 : Math.Max(1, verification.DamagedSlices.Length);
        int recovery = set.RecoveryBlocks.Count;
        RecoveryState state = verification.Status switch
        {
            Par2VerifyStatus.Intact => RecoveryState.Intact,
            _ when testPassed => RecoveryState.DoesNotMatch,
            Par2VerifyStatus.Repairable => RecoveryState.Repairable,
            Par2VerifyStatus.RepairTooLarge => RecoveryState.RepairTooLarge,
            _ => RecoveryState.NotRepairable,
        };
        var check = new RecoveryCheck
        {
            ArchivePath = found.ArchivePath,
            State = state,
            SetFiles = found.SetFiles,
            Blocks = blocks,
            DamagedBlocks = damaged,
            RecoveryBlocks = recovery,
            Text = state == RecoveryState.Intact ? CoreMessages.Text(MessageCode.RecoveryDataIntact, blocks, recovery) : null,
        };
        ArchiveError? error = state switch
        {
            RecoveryState.Repairable => CoreMessages.Error(found.ArchivePath, MessageCode.RecoveryDataDamagedRepairable, damaged, blocks, recovery),
            RecoveryState.NotRepairable => CoreMessages.Error(found.ArchivePath, MessageCode.RecoveryDataDamagedNotRepairable, damaged, blocks, recovery),
            RecoveryState.RepairTooLarge => CoreMessages.Error(found.ArchivePath, MessageCode.RecoveryDataRepairTooLarge, damaged, blocks),
            _ => null,
        };
        ArchiveWarning? warning = null;
        if (state == RecoveryState.DoesNotMatch)
            warning = CoreMessages.Warning(found.ArchivePath, CoreMessages.Text(MessageCode.RecoveryDataDoesNotMatch, damaged, blocks));
        else if (!found.Match.NameMatches)
            warning = CoreMessages.Warning(found.ArchivePath, CoreMessages.Text(MessageCode.RecoveryDataNameMismatch));
        return (check, error, warning);
    }
}
