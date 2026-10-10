using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Recovery;

namespace Archiver.Core.Services;

/// <inheritdoc cref="IArchiveCreationRouter"/>
public sealed class ArchiveCreationRouter(
    IArchiveService archiveService,
    ITarService tarService,
    GroupPolicyOptions groupPolicyOptions) : IArchiveCreationRouter
{
    private readonly GroupPolicyOptions _policy = groupPolicyOptions ?? throw new ArgumentNullException(nameof(groupPolicyOptions));

    /// <inheritdoc/>
    public Task<ArchiveResult> ArchiveAsync(
        ArchiveOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // T-F51: AllowedFormats/BlockedFormats also govern which formats may be created, not
        // just extracted — no capability/whitelist check existed here before this.
        string registryName = ArchiveFormatRegistryNames.ToRegistryName(options.Format);
        if (!_policy.IsFormatAllowed(registryName))
        {
            return Task.FromResult(new ArchiveResult
            {
                Errors = [CoreMessages.Error(ArchiveNaming.RefusedArchivePath(options), MessageCode.CreationFormatBlocked, registryName)],
            });
        }

        // T-F51: DisableTarExtraction is documented (POLICIES.md) as stopping tar.exe from ever
        // being spawned at all, for either direction — not just extraction, despite its name.
        if (_policy.DisableTarExtraction && options.Format != ArchiveContainerFormat.Zip)
        {
            return Task.FromResult(new ArchiveResult
            {
                Errors = [CoreMessages.Error(ArchiveNaming.RefusedArchivePath(options), MessageCode.TarCreationDisabled)],
            });
        }

        // T-F275: refused before anything is written, like a blocked format.
        if (options.RecoveryPercent is < 0 or > 100)
        {
            return Task.FromResult(new ArchiveResult
            {
                Errors = [CoreMessages.Error(ArchiveNaming.RefusedArchivePath(options), MessageCode.RecoveryPercentInvalid, options.RecoveryPercent)],
            });
        }
        if (options.RecoveryPercent > 0 && _policy.DisableRecoveryData)
        {
            return Task.FromResult(new ArchiveResult
            {
                Errors = [CoreMessages.Error(ArchiveNaming.RefusedArchivePath(options), MessageCode.RecoveryDataDisabled)],
            });
        }

        return options.RecoveryPercent == 0
            ? Engine(options, progress, cancellationToken)
            : ArchiveWithRecoveryDataAsync(options, progress, cancellationToken);
    }

    private Task<ArchiveResult> Engine(ArchiveOptions options, IProgress<ProgressReport>? progress, CancellationToken cancellationToken) =>
        options.Format == ArchiveContainerFormat.Zip
            ? archiveService.ArchiveAsync(options, progress, cancellationToken)
            : tarService.CompressAsync(options, progress, cancellationToken);

    // T-F275: the set is written after the engine has finished every archive. The folder opens
    // only then, so the user never moves an archive whose set is still being written; the PAR2 work
    // runs on the thread pool, since an engine may finish synchronously on the caller's (UI) thread.
    private async Task<ArchiveResult> ArchiveWithRecoveryDataAsync(ArchiveOptions options, IProgress<ProgressReport>? progress, CancellationToken cancellationToken)
    {
        var split = new RecoveryProgressSplit(progress, options.RecoveryPercent);
        ArchiveResult archived = await Engine(options with { OpenDestinationFolder = false }, split.Archive, cancellationToken).ConfigureAwait(false);
        ArchiveResult result = await Task.Run(
            () => RecoveryDataWriter.AddTo(archived, options.RecoveryPercent, split.Recovery, cancellationToken), cancellationToken).ConfigureAwait(false);
        split.Complete();
        if (result.Success && options.OpenDestinationFolder)
            ExplorerLauncher.OpenFolder(options.DestinationFolder);
        return result;
    }
}
