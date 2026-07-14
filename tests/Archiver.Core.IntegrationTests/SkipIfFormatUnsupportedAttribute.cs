using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// Marks a test as requiring a tar.exe build that supports the named format ("rar", "7z",
/// "zstd", "xz", "lzma", "bz2"). Skips if tar.exe is absent, or if DetectCapabilitiesAsync
/// reports the format unsupported (e.g. RAR5/7z require Windows 11 23H2+).
/// </summary>
public sealed class SkipIfFormatUnsupportedAttribute : FactAttribute
{
    public SkipIfFormatUnsupportedAttribute(string format)
    {
        if (!File.Exists(@"C:\Windows\System32\tar.exe"))
        {
            Skip = "tar.exe not present at C:\\Windows\\System32\\tar.exe";
            return;
        }

        TarCapabilities capabilities = new TarSandboxedService().DetectCapabilitiesAsync().GetAwaiter().GetResult();

        bool supported = format.ToLowerInvariant() switch
        {
            "rar" or "rar5" => capabilities.SupportsRar,
            "7z" => capabilities.Supports7z,
            "zstd" or "zst" => capabilities.SupportsZstd,
            "xz" => capabilities.SupportsXz,
            "lzma" => capabilities.SupportsLzma,
            "bz2" => capabilities.SupportsBz2,
            _ => throw new ArgumentException($"Unknown format '{format}'.", nameof(format)),
        };

        if (!supported)
            Skip = $"tar.exe (version {capabilities.Version}) does not support '{format}' on this system.";
    }
}
