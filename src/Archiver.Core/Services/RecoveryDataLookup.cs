using Archiver.Core.Models;
using Archiver.Core.Recovery;

namespace Archiver.Core.Services;

/// <summary>The archive a PAR2 file protects, or why it cannot be said (T-F275 step 3c). Exactly
/// one of the two is set.</summary>
public sealed record RecoveryTarget
{
    /// <summary>The protected file, found next to the PAR2 file and confirmed by the set's own hashes.</summary>
    public string? ArchivePath { get; init; }

    /// <summary>The set cannot be read, protects a file that is not there, or policy refuses it.</summary>
    public ArchiveError? Error { get; init; }
}

/// <summary>
/// What a frontend asks about PAR2 recovery data before any test runs (T-F275 step 3c). Both
/// lookups read the disk, so a UI calls them off its thread. The checks themselves stay in
/// <see cref="Interfaces.IExtractionRouter.TestAsync"/>.
/// </summary>
public static class RecoveryDataLookup
{
    /// <summary>Whether <paramref name="path"/> is named like a PAR2 file. Reads nothing.</summary>
    public static bool IsRecoveryFile(string path) => RecoveryTestStep.IsPar2Path(path);

    /// <summary>
    /// Whether PAR2 files lie next to <paramref name="archivePath"/> under either name the test
    /// looks for (<c>a.zip.par2</c>, <c>a.par2</c>, their volumes). Says nothing about whether they
    /// are usable or meant for this archive. False under
    /// <see cref="GroupPolicyOptions.DisableRecoveryData"/> and for a path or folder that cannot be
    /// read; never throws.
    /// </summary>
    public static bool HasFilesFor(string archivePath, GroupPolicyOptions policy)
    {
        if (policy.DisableRecoveryData)
            return false;
        try
        {
            return Par2SetLocator.AnySetFileForEitherBase(archivePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// The file the set of <paramref name="par2Path"/> protects, chosen as the test chooses it: by
    /// the hashes in the set, never by a name alone. Never throws except
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    public static RecoveryTarget FindArchive(string par2Path, GroupPolicyOptions policy, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsRecoveryFile(par2Path))
            return new RecoveryTarget { Error = CoreMessages.Error(par2Path, MessageCode.RecoveryDataUnusable) };
        try
        {
            RecoveryTestStep.Plan plan = RecoveryTestStep.Locate([par2Path], policy, cancellationToken);
            if (plan.Sets.Count > 0)
                return new RecoveryTarget { ArchivePath = plan.Sets[0].ArchivePath };
            return new RecoveryTarget { Error = plan.Errors.Count > 0 ? plan.Errors[0] : CoreMessages.Error(par2Path, MessageCode.RecoveryDataTargetNotFound) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new RecoveryTarget { Error = CoreMessages.Error(par2Path, CoreMessages.FromException(ex), ex) };
        }
    }
}
