using Archiver.Core.Models;

namespace Archiver.Core.Interfaces;

public interface IArchiveService
{
    /// <summary>
    /// Creates one or more ZIP archives from the provided options.
    /// Never throws — errors are captured in ArchiveResult.Errors.
    /// </summary>
    Task<ArchiveResult> ArchiveAsync(
        ArchiveOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts one or more ZIP archives.
    /// Never throws — errors are captured in ArchiveResult.Errors.
    /// </summary>
    Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}
