using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// The one classifier every operation uses to decide which engine may open an archive (T-F261):
/// Group Policy first (AllowedFormats/BlockedFormats, then DisableTarExtraction), then what the
/// system's tar.exe can read. Extraction, testing, listing and scanning all route through it, so
/// no operation can drift from what another would allow or refuse.
/// </summary>
public static class ArchiveFormatPolicy
{
    /// <summary>The reason a tar-family archive is skipped by Test: only the ZIP engine can test.</summary>
    public const string NoTestCapabilityReason = "tar-family archives have no test capability";

    /// <summary>
    /// A selection split by engine. <see cref="ZipPaths"/> also holds unrecognized paths, so the
    /// ZIP engine's own "not a recognized archive" handling reports them.
    /// </summary>
    /// <param name="ZipPaths">Paths the ZIP engine handles.</param>
    /// <param name="TarPaths">Paths tar.exe may open.</param>
    /// <param name="Unsupported">Paths refused by Group Policy or by tar.exe's capabilities.</param>
    public sealed record Classification(
        IReadOnlyList<string> ZipPaths,
        IReadOnlyList<string> TarPaths,
        IReadOnlyList<SkippedFile> Unsupported);

    /// <summary>Splits <paramref name="paths"/> by engine, refusing what policy or tar.exe cannot allow.</summary>
    public static Classification Classify(
        IReadOnlyList<string> paths, TarCapabilities tarCapabilities, GroupPolicyOptions policy)
    {
        var zipPaths = new List<string>();
        var tarPaths = new List<string>();
        var unsupported = new List<SkippedFile>();

        foreach (string path in paths)
        {
            ArchiveFormat format = ArchiveFormatDetector.Detect(path);
            if (GetRefusalReason(format, tarCapabilities, policy) is { } reason)
                unsupported.Add(new SkippedFile { Path = path, Reason = reason });
            else if (IsZipEngineFormat(format))
                zipPaths.Add(path);
            else
                tarPaths.Add(path);
        }

        return new Classification(zipPaths, tarPaths, unsupported);
    }

    /// <summary>
    /// Why an archive of <paramref name="format"/> may not be opened, or null when it may.
    /// Policy is checked before tar.exe's capabilities, so a refused format always names the
    /// policy — also when tar.exe was never probed because the policy disables it.
    /// <see cref="ArchiveFormat.Unknown"/> is never refused here: the ZIP engine reports it.
    /// </summary>
    public static string? GetRefusalReason(ArchiveFormat format, TarCapabilities tarCapabilities, GroupPolicyOptions policy)
    {
        if (format == ArchiveFormat.Unknown)
            return null;

        string registryName = ArchiveFormatRegistryNames.ToRegistryName(format);
        if (!policy.IsFormatAllowed(registryName))
            return $"This archive format ({registryName}) is blocked by Group Policy.";

        if (IsZipEngineFormat(format))
            return null;

        // DisableTarExtraction is a separate kill switch from BlockedFormats: tar.exe is never
        // started at all, not just refused per format.
        if (policy.DisableTarExtraction)
            return "tar.exe-based extraction is disabled by Group Policy.";

        return IsSupportedByTar(format, tarCapabilities) ? null : BuildUnsupportedReason(format, tarCapabilities);
    }

    /// <summary>True when Group Policy refuses <paramref name="format"/> (BlockedFormats/AllowedFormats, or DisableTarExtraction for a tar-family format).</summary>
    public static bool IsBlockedByPolicy(ArchiveFormat format, GroupPolicyOptions policy)
    {
        if (format == ArchiveFormat.Unknown)
            return false;
        if (!policy.IsFormatAllowed(ArchiveFormatRegistryNames.ToRegistryName(format)))
            return true;
        return !IsZipEngineFormat(format) && policy.DisableTarExtraction;
    }

    private static bool IsZipEngineFormat(ArchiveFormat format) => format is ArchiveFormat.Zip or ArchiveFormat.Unknown;

    private static bool IsSupportedByTar(ArchiveFormat format, TarCapabilities caps) => format switch
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
