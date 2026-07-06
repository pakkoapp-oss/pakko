using System.Text.RegularExpressions;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// Parses `tar.exe --version` output into <see cref="TarCapabilities"/>. Extracted into a
/// separate class so T-F48 can unit-test format detection without launching a process.
/// </summary>
public static partial class TarVersionParser
{
    // libarchive 3.7+ threshold for RAR/7z/zstd — matches TESTING.md's documented
    // "requires Win 11 23H2+ tar.exe" note on all three formats.
    private static readonly Version MinVersionForModernFormats = new(3, 7, 0);

    /// <summary>
    /// Parses <paramref name="versionOutput"/> (the raw stdout of `tar.exe --version`) into
    /// <see cref="TarCapabilities"/>. Returns safe all-unsupported defaults if the output doesn't
    /// contain a recognizable libarchive version.
    /// </summary>
    public static TarCapabilities Parse(string versionOutput)
    {
        if (string.IsNullOrWhiteSpace(versionOutput))
            return new TarCapabilities();

        Match match = LibarchiveVersionRegex().Match(versionOutput);
        if (!match.Success)
            return new TarCapabilities();

        var version = new Version(
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value));

        bool supportsModernFormats = version >= MinVersionForModernFormats;

        return new TarCapabilities
        {
            Version = version.ToString(),
            SupportsXz = versionOutput.Contains("liblzma", StringComparison.OrdinalIgnoreCase),
            SupportsLzma = versionOutput.Contains("liblzma", StringComparison.OrdinalIgnoreCase),
            SupportsBz2 = versionOutput.Contains("bz2lib", StringComparison.OrdinalIgnoreCase),
            SupportsZstd = supportsModernFormats,
            Supports7z = supportsModernFormats,
            SupportsRar = supportsModernFormats,
        };
    }

    [GeneratedRegex(@"libarchive\s+(\d+)\.(\d+)\.(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex LibarchiveVersionRegex();
}
