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
    public SkipIfFormatUnsupportedAttribute(string format) => Skip = TarFormatSupport.SkipReason(format);
}

/// <summary>
/// <see cref="SkipIfFormatUnsupportedAttribute"/> for a theory. xUnit 2 cannot skip one row, so the
/// rows of a format a tar.exe may lack go in a theory of their own (T-F476: windows-2022's 3.8.9
/// has no xz, LZMA, bzip2 or zstd).
/// </summary>
public sealed class SkipIfFormatUnsupportedTheoryAttribute : TheoryAttribute
{
    public SkipIfFormatUnsupportedTheoryAttribute(string format) => Skip = TarFormatSupport.SkipReason(format);
}

internal static class TarFormatSupport
{
    public static string? SkipReason(string format)
    {
        if (!File.Exists(@"C:\Windows\System32\tar.exe"))
            return "tar.exe not present at C:\\Windows\\System32\\tar.exe";

        TarCapabilities capabilities = new TarSandboxedService(new GroupPolicyOptions()).DetectCapabilitiesAsync().GetAwaiter().GetResult();

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

        return supported ? null : $"tar.exe (version {capabilities.Version}) does not support '{format}' on this system.";
    }
}
