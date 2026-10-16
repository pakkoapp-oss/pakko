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
        // T-F403: only a library built into tar.exe counts; without one, tar.exe runs a program of
        // that name from its working folder or PATH (measured for zstd and bzip2).
        return new TarCapabilities
        {
            Version = version.ToString(),
            SupportsGzip = versionOutput.Contains("zlib", StringComparison.OrdinalIgnoreCase),
            SupportsXz = hasLzma,
            SupportsLzma = hasLzma,
            SupportsBz2 = versionOutput.Contains("bz2lib", StringComparison.OrdinalIgnoreCase),
            SupportsZstd = supportsModernFormats && versionOutput.Contains("libzstd", StringComparison.OrdinalIgnoreCase),
            Supports7z = supportsModernFormats && hasLzma,
            SupportsRar = supportsModernFormats,
        };
    }

    /// <summary>
    /// Whether <paramref name="version"/> (<see cref="TarCapabilities.Version"/>) is new enough for
    /// RAR, 7z and zstd, so a refusal can say "needs a newer tar.exe" only when that is the reason.
    /// </summary>
    internal static bool MeetsModernVersion(string version) =>
        Version.TryParse(version, out Version? parsed) && parsed >= MinVersionForModernFormats;

    [GeneratedRegex(@"libarchive\s+(\d+)\.(\d+)\.(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex LibarchiveVersionRegex();
}
