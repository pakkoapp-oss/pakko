using Archiver.Core.Models;

namespace Archiver.Core.Interfaces;

/// <summary>PAR2 recovery data next to an archive: what is there, and repair from it (T-F275 step 4).
/// The checks themselves are <see cref="IExtractionRouter.TestAsync"/>.</summary>
public interface IRecoveryService
{
    /// <summary>
    /// Whether PAR2 files lie next to <paramref name="archivePath"/> under either name the test
    /// looks for (<c>a.zip.par2</c>, <c>a.par2</c>, their volumes). Says nothing about whether they
    /// are usable or meant for this archive. Reads a folder listing, so a UI calls it off its
    /// thread. False under <see cref="GroupPolicyOptions.DisableRecoveryData"/> and for a path or
    /// folder that cannot be read; never throws.
    /// </summary>
    bool HasFilesFor(string archivePath);

    /// <summary>
    /// The file the set of <paramref name="par2Path"/> protects, chosen as the test chooses it: by
    /// the hashes in the set, never by a name alone. A file that is gone is still named when the
    /// set names it. Reads the set, so a UI calls it off its thread. Never throws except
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    RecoveryTarget FindArchive(string par2Path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds each damaged archive into a new file, <c>&lt;name&gt;.repaired&lt;extension&gt;</c>, next
    /// to it or in <see cref="RepairOptions.OutputDirectory"/>. The archive and its PAR2 files are
    /// only read. An archive is rebuilt exactly when
    /// <see cref="IExtractionRouter.TestAsync"/> with <c>verifyRecoveryData</c> would call it
    /// damaged and repairable; a copy is kept only after it matches the set's hashes.
    /// <para><see cref="ArchiveResult.CreatedFiles"/> lists the repaired copies and
    /// <see cref="ArchiveResult.RecoveryChecks"/> what was found for each archive. An archive
    /// with no usable recovery data is an error here, since repair was asked for. Under
    /// <see cref="GroupPolicyOptions.DisableRecoveryData"/> every path is refused. Never throws
    /// except <see cref="OperationCanceledException"/>.</para>
    /// </summary>
    Task<ArchiveResult> RepairAsync(
        RepairOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);
}
