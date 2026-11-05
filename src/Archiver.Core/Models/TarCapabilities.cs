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

    /// <summary>
    /// T-F442: reads ISO 9660, cpio, ar and compress (.Z) - readers built into libarchive itself,
    /// so any tar.exe that gave its version (both builds measured). False when it gave none.
    /// </summary>
    public bool SupportsBuiltInReaders { get; init; }

    /// <summary>T-F442: reads CAB (its MSZIP blocks need zlib).</summary>
    public bool SupportsCab { get; init; }

    /// <summary>T-F442: reads lzip (needs liblzma built in; 3.5.2 runs an external <c>lzip</c> instead).</summary>
    public bool SupportsLzip { get; init; }

    /// <summary>The raw <c>tar --version</c> output the probe parsed, for diagnostics.</summary>
    public string Version { get; init; } = string.Empty;
}
