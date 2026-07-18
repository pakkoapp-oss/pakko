using Archiver.Core.Interfaces;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <inheritdoc cref="IArchiveCreationRouter"/>
public sealed class ArchiveCreationRouter(
    IArchiveService archiveService,
    ITarService tarService,
    GroupPolicyOptions? groupPolicyOptions = null) : IArchiveCreationRouter
{
    private readonly GroupPolicyOptions _policy = groupPolicyOptions ?? new GroupPolicyOptions();

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
                Success = false,
                Errors = [new ArchiveError
                {
                    SourcePath = options.DestinationFolder,
                    Message = $"Creating a {registryName} archive is blocked by Group Policy.",
                }],
            });
        }

        // T-F51: DisableTarExtraction is documented (POLICIES.md) as stopping tar.exe from ever
        // being spawned at all, for either direction — not just extraction, despite its name.
        if (_policy.DisableTarExtraction && options.Format != ArchiveContainerFormat.Zip)
        {
            return Task.FromResult(new ArchiveResult
            {
                Success = false,
                Errors = [new ArchiveError
                {
                    SourcePath = options.DestinationFolder,
                    Message = "tar.exe-based archive creation is disabled by Group Policy.",
                }],
            });
        }

        return options.Format == ArchiveContainerFormat.Zip
            ? archiveService.ArchiveAsync(options, progress, cancellationToken)
            : tarService.CompressAsync(options, progress, cancellationToken);
    }
}
