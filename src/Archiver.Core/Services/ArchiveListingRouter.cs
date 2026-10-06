using Archiver.Core.Interfaces;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <inheritdoc cref="IArchiveListingRouter"/>
public sealed class ArchiveListingRouter : IArchiveListingRouter
{
    private readonly IArchiveService _archiveService;
    private readonly ITarService _tarService;
    private readonly Func<Task<TarCapabilities>> _tarCapabilities;
    private readonly GroupPolicyOptions _policy;

    /// <summary>Creates the router over both engines, with the capabilities of tar.exe already known.</summary>
    public ArchiveListingRouter(
        IArchiveService archiveService,
        ITarService tarService,
        TarCapabilities tarCapabilities,
        GroupPolicyOptions groupPolicyOptions)
        : this(archiveService, tarService, () => Task.FromResult(tarCapabilities), groupPolicyOptions)
    {
    }

    // T-F350: the capabilities are asked for only when a tar-family archive is met.
    internal ArchiveListingRouter(
        IArchiveService archiveService,
        ITarService tarService,
        Func<Task<TarCapabilities>> tarCapabilities,
        GroupPolicyOptions groupPolicyOptions)
    {
        _archiveService = archiveService;
        _tarService = tarService;
        _tarCapabilities = tarCapabilities;
        _policy = groupPolicyOptions ?? throw new ArgumentNullException(nameof(groupPolicyOptions));
    }

    /// <inheritdoc/>
    public async Task<ArchiveListResult> ListEntriesAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        ArchiveFormatPolicy.Classification classification = await ArchiveFormatPolicy
            .ClassifyAsync([archivePath], _tarCapabilities, _policy, cancellationToken).ConfigureAwait(false);

        if (classification.Unsupported.Count > 0)
            return CoreMessages.ListFailure(classification.Unsupported[0].Text!);

        return classification.ZipPaths.Count > 0
            ? await _archiveService.ListEntriesAsync(archivePath, cancellationToken).ConfigureAwait(false)
            : await _tarService.ListEntriesAsync(archivePath, cancellationToken).ConfigureAwait(false);
    }
}
