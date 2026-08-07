using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// T-F146: extracted from ExtractionRouter's own per-path classify loop (previously inline, ~40
/// lines) so AntivirusScanService can apply the exact same Group Policy gating
/// (BlockedFormats/AllowedFormats, DisableTarExtraction) and tar-capability check without
/// duplicating it — a scan spawns tar.exe inside the same AppContainer a real extraction does, so
/// it must be refused identically wherever policy refuses extraction. Behavior-preserving: this is
/// a pure extraction of ExtractionRouter's existing logic, not a change to it.
/// </summary>
internal static class ArchiveFormatPolicy
{
    public sealed record Classification(
        IReadOnlyList<string> ZipPaths,
        IReadOnlyList<string> TarPaths,
        IReadOnlyList<SkippedFile> Unsupported);

    public static Classification Classify(
        IReadOnlyList<string> paths, TarCapabilities tarCapabilities, GroupPolicyOptions policy)
    {
        var zipPaths = new List<string>();
        var tarPaths = new List<string>();
        var unsupported = new List<SkippedFile>();

        foreach (string path in paths)
        {
            ArchiveFormat format = ArchiveFormatDetector.Detect(path);

            // AllowedFormats/BlockedFormats sit outside the format-specific switch below since
            // they apply uniformly to both Zip and every tar-family format. Unknown has no
            // registry name to check — leave it to the existing unrecognized-path handling.
            if (format != ArchiveFormat.Unknown)
            {
                string registryName = ArchiveFormatRegistryNames.ToRegistryName(format);
                if (!policy.IsFormatAllowed(registryName))
                {
                    unsupported.Add(new SkippedFile
                    {
                        Path = path,
                        Reason = $"This archive format ({registryName}) is blocked by Group Policy."
                    });
                    continue;
                }
            }

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
                    // DisableTarExtraction is a separate kill switch from BlockedFormats — it
                    // stops tar.exe from ever being spawned at all, not just a per-format block.
                    if (policy.DisableTarExtraction)
                        unsupported.Add(new SkippedFile
                        {
                            Path = path,
                            Reason = "tar.exe-based extraction is disabled by Group Policy."
                        });
                    else if (IsSupported(format, tarCapabilities))
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

        return new Classification(zipPaths, tarPaths, unsupported);
    }

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
