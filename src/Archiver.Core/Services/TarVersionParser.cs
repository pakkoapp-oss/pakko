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
        bool hasLzma = versionOutput.Contains("liblzma", StringComparison.OrdinalIgnoreCase);

        // T-F402: a new enough tar.exe can still lack a codec. Windows Server 2022's 3.8.4 is built
        // with zlib only: no zstd (`--zstd` then runs an external program), and no LZMA/LZMA2, the
        // method of nearly every 7z and the 7z writer's default. RAR needs no external library.
        return new TarCapabilities
        {
            Version = version.ToString(),
            SupportsXz = hasLzma,
            SupportsLzma = hasLzma,
            SupportsBz2 = versionOutput.Contains("bz2lib", StringComparison.OrdinalIgnoreCase),
            SupportsZstd = supportsModernFormats && versionOutput.Contains("libzstd", StringComparison.OrdinalIgnoreCase),
            Supports7z = supportsModernFormats && hasLzma,
            SupportsRar = supportsModernFormats,
        };
    }

    [GeneratedRegex(@"libarchive\s+(\d+)\.(\d+)\.(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex LibarchiveVersionRegex();
}
