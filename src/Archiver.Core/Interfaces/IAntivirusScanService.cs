using Archiver.Core.Models;

namespace Archiver.Core.Interfaces;

/// <summary>
/// T-F146. Scans an archive's expanded contents for threats via AMSI — deliberately not an
/// extension of IArchiveService/ITarService (see docs/DECISIONS.md's T-F146 entry): this never
/// writes anything to a real destination, has no conflict/MOTW dimension, and adding a method to
/// those interfaces would ripple through every hand-rolled test fake in the repo for a capability
/// that isn't a variant of extraction. Never throws — every failure becomes an Inconclusive
/// ThreatFinding.
/// </summary>
public interface IAntivirusScanService
{
    Task<ThreatScanResult> ScanAsync(
        AntivirusScanOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);
}
