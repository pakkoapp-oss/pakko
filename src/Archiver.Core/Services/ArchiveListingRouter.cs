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
    public Task<ArchiveListResult> ListEntriesAsync(
        string archivePath,
        CancellationToken cancellationToken = default) =>
        ListingCallback.CollectAsync(onEntry => ListEntriesAsync(archivePath, onEntry, cancellationToken));

    /// <inheritdoc/>
    public async Task<ArchiveListSummary> ListEntriesAsync(
        string archivePath,
        Action<ArchiveEntryInfo> onEntry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onEntry);
        ArchiveFormatPolicy.Classification classification = await ArchiveFormatPolicy
            .ClassifyAsync([archivePath], _tarCapabilities, _policy, cancellationToken).ConfigureAwait(false);

        if (classification.Unsupported.Count > 0)
            return CoreMessages.Summary(CoreMessages.ListFailure(classification.Unsupported[0].Text!));

        if (classification.ZipPaths.Count > 0)
            return await _archiveService.ListEntriesAsync(archivePath, onEntry, cancellationToken).ConfigureAwait(false);
        // T-F413: a gzip of one file, which tar.exe cannot read; its one entry is listed whole.
        if (BareGzip.IsBareGzip(archivePath))
        {
            return await ListingCallback.RunAsync(onEntry, async guarded =>
                ArchiveListSummary.Replay(await BareGzipEngine.ListAsync(archivePath, cancellationToken).ConfigureAwait(false), guarded),
                cancellationToken).ConfigureAwait(false);
        }
        return await _tarService.ListEntriesAsync(archivePath, onEntry, cancellationToken).ConfigureAwait(false);
    }
}
