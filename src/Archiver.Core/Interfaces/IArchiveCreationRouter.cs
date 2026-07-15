using Archiver.Core.Models;

namespace Archiver.Core.Interfaces;

/// <summary>
/// Routes archive creation to IArchiveService (ZIP) or ITarService (tar-family) based on
/// ArchiveOptions.Format. Unlike IExtractionRouter, this needs no per-path format detection —
/// creation format is a single explicit choice for the whole operation.
/// </summary>
public interface IArchiveCreationRouter
{
    Task<ArchiveResult> ArchiveAsync(
        ArchiveOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);
}
