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
        ArchiveFormat format = ArchiveFormatDetector.Detect(archivePath);

        return format switch
        {
            ArchiveFormat.Zip or ArchiveFormat.Unknown =>
                archiveService.ListEntriesAsync(archivePath, cancellationToken),
            _ when IsSupported(format, tarCapabilities) =>
                tarService.ListEntriesAsync(archivePath, cancellationToken),
            _ => Task.FromResult(new ArchiveListResult
            {
                Success = false,
                ErrorMessage = BuildUnsupportedReason(format, tarCapabilities),
            }),
        };
    }

    // Same dispatch rules as ExtractionRouter.IsSupported/BuildUnsupportedReason — kept as a
    // separate small copy rather than a shared helper; the duplication is a handful of lines and
    // extracting a shared static class would add indirection for two call sites.
    private static bool IsSupported(ArchiveFormat format, TarCapabilities caps) => format switch
    {
        ArchiveFormat.Tar or ArchiveFormat.GZip => true,
        ArchiveFormat.Bz2 => caps.SupportsBz2,
        ArchiveFormat.Xz => caps.SupportsXz,
        ArchiveFormat.Zstd => caps.SupportsZstd,
        ArchiveFormat.Lzma => caps.SupportsLzma,
        ArchiveFormat.Rar => caps.SupportsRar,
        ArchiveFormat.SevenZip => caps.Supports7z,
        _ => false,
    };

    private static string BuildUnsupportedReason(ArchiveFormat format, TarCapabilities caps) => format switch
    {
        ArchiveFormat.Rar => $"RAR requires tar.exe with libarchive >= 3.7.0 (Windows 11 23H2+); this system's tar.exe (version {caps.Version}) does not support it.",
        ArchiveFormat.SevenZip => $"7-Zip requires tar.exe with libarchive >= 3.7.0 (Windows 11 23H2+); this system's tar.exe (version {caps.Version}) does not support it.",
        ArchiveFormat.Zstd => $"Zstandard requires tar.exe with libarchive >= 3.7.0 (Windows 11 23H2+); this system's tar.exe (version {caps.Version}) does not support it.",
        ArchiveFormat.Xz => $"XZ is not supported by this system's tar.exe (version {caps.Version}).",
        ArchiveFormat.Lzma => $"LZMA is not supported by this system's tar.exe (version {caps.Version}).",
        _ => $"This archive format is not supported by this system's tar.exe (version {caps.Version}).",
    };
}
