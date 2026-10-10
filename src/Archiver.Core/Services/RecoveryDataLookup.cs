using Archiver.Core.Models;
using Archiver.Core.Recovery;

namespace Archiver.Core.Services;

/// <summary>
/// What a frontend asks about PAR2 recovery data before any test runs (T-F275 step 3c). The two
/// lookups that read the disk are reached through <see cref="Interfaces.IRecoveryService"/>
/// (step 4c), which holds the policy; only the name check is public here.
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
    internal static bool HasFilesFor(string archivePath, GroupPolicyOptions policy)
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
    internal static RecoveryTarget FindArchive(string par2Path, GroupPolicyOptions policy, CancellationToken cancellationToken = default)
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
