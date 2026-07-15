using Archiver.Core.Interfaces;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <inheritdoc cref="IArchiveCreationRouter"/>
public sealed class ArchiveCreationRouter(IArchiveService archiveService, ITarService tarService) : IArchiveCreationRouter
{
    /// <inheritdoc/>
    public Task<ArchiveResult> ArchiveAsync(
        ArchiveOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default) =>
        options.Format == ArchiveContainerFormat.Zip
            ? archiveService.ArchiveAsync(options, progress, cancellationToken)
            : tarService.CompressAsync(options, progress, cancellationToken);
}
