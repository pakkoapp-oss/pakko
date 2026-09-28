using Archiver.Core.Interfaces;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <inheritdoc cref="IArchiveListingRouter"/>
public sealed class ArchiveListingRouter(
    IArchiveService archiveService,
    ITarService tarService,
    TarCapabilities tarCapabilities,
    GroupPolicyOptions groupPolicyOptions) : IArchiveListingRouter
{
    private readonly GroupPolicyOptions _policy = groupPolicyOptions ?? throw new ArgumentNullException(nameof(groupPolicyOptions));

    /// <inheritdoc/>
    public Task<ArchiveListResult> ListEntriesAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        ArchiveFormatPolicy.Classification classification =
            ArchiveFormatPolicy.Classify([archivePath], tarCapabilities, _policy);

        if (classification.Unsupported.Count > 0)
            return Task.FromResult(CoreMessages.ListFailure(classification.Unsupported[0].Text!));

        return classification.ZipPaths.Count > 0
            ? archiveService.ListEntriesAsync(archivePath, cancellationToken)
            : tarService.ListEntriesAsync(archivePath, cancellationToken);
    }
}
