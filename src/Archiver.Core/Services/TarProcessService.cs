using Archiver.Core.Interfaces;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// Extracts tar-family archives (tar, tar.gz, tar.bz2, tar.xz, tar.zst, tar.lzma, 7z, rar) via
/// the system's tar.exe. Capability detection and the extraction pipeline are implemented in
/// T-F48/T-F49; this is scaffolding only (T-F47).
/// </summary>
public sealed class TarProcessService : ITarService
{
    /// <inheritdoc/>
    public Task<TarCapabilities> DetectCapabilitiesAsync()
    {
        return Task.FromResult(new TarCapabilities());
    }

    /// <inheritdoc/>
    public Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Tar extraction pipeline lands in T-F49.");
    }
}
