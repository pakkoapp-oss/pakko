using Archiver.Core.Models;

namespace Archiver.Core.Interfaces;

/// <summary>
/// Routes ExtractAsync calls to IArchiveService (ZIP) or ITarService (tar-family) per archive,
/// based on ArchiveFormatDetector, and merges the results. Never throws — all errors are
/// captured in ArchiveResult.Errors.
/// </summary>
public interface IExtractionRouter
{
    Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);
}
