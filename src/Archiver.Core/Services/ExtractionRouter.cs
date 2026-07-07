using Archiver.Core.Interfaces;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <inheritdoc cref="IExtractionRouter"/>
public sealed class ExtractionRouter(
    IArchiveService archiveService,
    ITarService tarService,
    TarCapabilities tarCapabilities) : IExtractionRouter
{
    /// <inheritdoc/>
    public async Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var zipPaths = new List<string>();
        var tarPaths = new List<string>();
        var unsupported = new List<SkippedFile>();

        foreach (string path in options.ArchivePaths)
        {
            ArchiveFormat format = ArchiveFormatDetector.Detect(path);
            switch (format)
            {
                case ArchiveFormat.Zip:
                    zipPaths.Add(path);
                    break;
                case ArchiveFormat.Unknown:
                    // Not a recognized archive format at all — let IArchiveService's own
                    // ZipArchiveService.GetKnownArchiveReason defensive path handle messaging
                    // for whatever this turns out to be (kept in the ZIP bucket so its existing
                    // behavior for unrecognized paths is unchanged).
                    zipPaths.Add(path);
                    break;
                default:
                    if (IsSupported(format, tarCapabilities))
                        tarPaths.Add(path);
                    else
                        unsupported.Add(new SkippedFile
                        {
                            Path = path,
                            Reason = BuildUnsupportedReason(format, tarCapabilities)
                        });
                    break;
            }
        }

        ArchiveResult zipResult = zipPaths.Count > 0
            ? await archiveService.ExtractAsync(
                options with { ArchivePaths = zipPaths, OpenDestinationFolder = false },
                progress, cancellationToken).ConfigureAwait(false)
            : EmptyResult();

        ArchiveResult tarResult = tarPaths.Count > 0
            ? await tarService.ExtractAsync(
                options with { ArchivePaths = tarPaths, OpenDestinationFolder = false },
                AdaptProgress(progress), cancellationToken).ConfigureAwait(false)
            : EmptyResult();

        var merged = new ArchiveResult
        {
            Success = zipResult.Success && tarResult.Success,
            CreatedFiles = [.. zipResult.CreatedFiles, .. tarResult.CreatedFiles],
            Errors = [.. zipResult.Errors, .. tarResult.Errors],
            SkippedFiles = [.. zipResult.SkippedFiles, .. tarResult.SkippedFiles, .. unsupported],
        };

        if (merged.Success && options.OpenDestinationFolder)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo("explorer.exe", options.DestinationFolder) { UseShellExecute = true });
            }
            catch { }
        }

        return merged;
    }

    private static ArchiveResult EmptyResult() => new() { Success = true };

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

    private static IProgress<int>? AdaptProgress(IProgress<ProgressReport>? progress) =>
        progress is null
            ? null
            : new Progress<int>(percent => progress.Report(new ProgressReport { Percent = percent, BytesTransferred = 0, TotalBytes = 0 }));
}
