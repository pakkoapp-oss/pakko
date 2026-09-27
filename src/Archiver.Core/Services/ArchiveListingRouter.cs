using Archiver.Core.Interfaces;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <inheritdoc cref="IArchiveListingRouter"/>
public sealed class ArchiveListingRouter(
    IArchiveService archiveService,
    ITarService tarService,
    TarCapabilities tarCapabilities) : IArchiveListingRouter
{
    /// <inheritdoc/>
    public Task<ArchiveListResult> ListEntriesAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        ArchiveFormatPolicy.Classification classification =
            ArchiveFormatPolicy.Classify([archivePath], tarCapabilities, new GroupPolicyOptions());

        if (classification.Unsupported.Count > 0)
            return Task.FromResult(new ArchiveListResult
            {
                Success = false,
                ErrorMessage = classification.Unsupported[0].Reason,
            });

        return classification.ZipPaths.Count > 0
            ? archiveService.ListEntriesAsync(archivePath, cancellationToken)
            : tarService.ListEntriesAsync(archivePath, cancellationToken);
    }
}
