namespace Archiver.Core.Models;

/// <summary>
/// What the system's tar.exe (libarchive) supports, probed once via ITarService.DetectCapabilitiesAsync
/// by parsing its version output. tar.exe reads RAR and cannot write it; a new enough one writes
/// 7z with --format=7zip (T-F342). That is not in this record: no version tells it, so
/// ITarService.CanCreateSevenZipAsync finds it by running the writer.
/// </summary>
public sealed record TarCapabilities
{
    /// <summary>Can read RAR archives (read-only — no RAR writer exists in libarchive).</summary>
    public bool SupportsRar { get; init; }

    /// <summary>Can read 7z archives.</summary>
    public bool Supports7z { get; init; }

    /// <summary>Can read and write .tar.zst.</summary>
    public bool SupportsZstd { get; init; }

    /// <summary>Can read and write .tar.xz.</summary>
    public bool SupportsXz { get; init; }

    /// <summary>Can read and write .tar.lzma.</summary>
    public bool SupportsLzma { get; init; }

    /// <summary>Can read and write .tar.bz2.</summary>
    public bool SupportsBz2 { get; init; }

    /// <summary>
    /// Can read and write .tar.gz, and use Deflate in a 7z: zlib is built into tar.exe (T-F403).
    /// True by default: every tar.exe measured has zlib, and a tar.exe that gave no version is
    /// not run for an archive at all.
    /// </summary>
    public bool SupportsGzip { get; init; } = true;

    /// <summary>The raw <c>tar --version</c> output the probe parsed, for diagnostics.</summary>
    public string Version { get; init; } = string.Empty;
}
