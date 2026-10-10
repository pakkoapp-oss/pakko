using Archiver.Core.Models;

namespace Archiver.Core.Interfaces;

/// <summary>Repairs archives from the PAR2 recovery data next to them (T-F275 step 4).</summary>
public interface IRecoveryService
{
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
