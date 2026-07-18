using System.IO.Compression;

namespace Archiver.CLI;

/// <summary>
/// Maps 7z's -mx=0..9 compression-level scale onto System.IO.Compression.CompressionLevel's four
/// discrete values. Documented explicitly (not a naive /9*4 approximation) per CLI.md's switch
/// table and this repo's requirement that the bucketing be reflected in --help output and
/// ARCHITECTURE.md, not left implicit.
/// </summary>
public static class CliCompressionLevelMapper
{
    public static CompressionLevel? TryMap(int mx) => mx switch
    {
        0 => CompressionLevel.NoCompression,
        1 or 2 => CompressionLevel.Fastest,
        >= 3 and <= 6 => CompressionLevel.Optimal,   // 7z's own default -mx5 lands here
        >= 7 and <= 9 => CompressionLevel.SmallestSize,
        _ => null,
    };
}
