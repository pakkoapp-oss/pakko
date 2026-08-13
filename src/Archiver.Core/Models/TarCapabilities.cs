namespace Archiver.Core.Models;

/// <summary>
/// What the system's tar.exe (libarchive) supports, probed once via ITarService.DetectCapabilitiesAsync
/// by parsing its version output. tar.exe can read RAR/7z but never write either — libarchive has
/// no writer for them (see CLAUDE.md's tar.exe format-support hard constraint).
/// </summary>
public sealed record TarCapabilities
{
    /// <summary>Can read RAR archives (read-only — no RAR writer exists in libarchive).</summary>
    public bool SupportsRar { get; init; }

    /// <summary>Can read 7z archives (read-only — no 7z writer exists in libarchive).</summary>
    public bool Supports7z { get; init; }

    /// <summary>Can read and write .tar.zst.</summary>
    public bool SupportsZstd { get; init; }

    /// <summary>Can read and write .tar.xz.</summary>
    public bool SupportsXz { get; init; }

    /// <summary>Can read and write .tar.lzma.</summary>
    public bool SupportsLzma { get; init; }

    /// <summary>Can read and write .tar.bz2.</summary>
    public bool SupportsBz2 { get; init; }

    /// <summary>The raw <c>tar --version</c> output the probe parsed, for diagnostics.</summary>
    public string Version { get; init; } = string.Empty;
}
