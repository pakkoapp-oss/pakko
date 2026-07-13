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

    /// <summary>
    /// Lists a tar-family archive's entries as a flat list, without extracting. Never throws — a
    /// failure (corrupted archive, tar.exe error) is reported via
    /// ArchiveListResult.Success/ErrorMessage. Does not run the whole-archive safety pre-scan
    /// ScanForUnsafeEntriesAsync performs before extraction — listing must never be gated on a
    /// policy that only matters once bytes are about to be written to disk.
    /// </summary>
    Task<ArchiveListResult> ListEntriesAsync(
        string archivePath,
        CancellationToken cancellationToken = default);
}
