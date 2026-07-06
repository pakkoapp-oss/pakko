using Archiver.Core.Models;

namespace Archiver.Core.Interfaces;

public interface ITarService
{
    /// <summary>
    /// Detects which formats the system's tar.exe supports by probing its version output.
    /// Returns sensible all-false defaults if tar.exe is absent or the probe fails.
    /// </summary>
    Task<TarCapabilities> DetectCapabilitiesAsync();

    /// <summary>
    /// Extracts one or more tar-family archives (tar, tar.gz, tar.bz2, tar.xz, tar.zst, tar.lzma,
    /// 7z, rar) via tar.exe. Never throws — errors are captured in ArchiveResult.Errors.
    /// </summary>
    Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}
