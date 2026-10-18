using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.CLI;

/// <summary>
/// T-F410: 7-Zip's -stx{Type} on x, l and t. An archive whose own format, by its bytes, is one
/// of the excluded types is not opened ("Cannot open the file as archive", exit 2); the other
/// archives go on. As in 7-Zip only the outermost format counts: -stxtar leaves a .tar.gz alone,
/// -stxgzip refuses it (measured on 7za 26.02). The names are 7-Zip's for the formats Pakko reads.
/// </summary>
public static class CliArchiveTypeExclusion
{
    private static readonly Dictionary<string, ArchiveFormat> FormatByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zip"] = ArchiveFormat.Zip,
        ["7z"] = ArchiveFormat.SevenZip,
        ["tar"] = ArchiveFormat.Tar,
        ["gzip"] = ArchiveFormat.GZip,
        ["bzip2"] = ArchiveFormat.Bz2,
        ["xz"] = ArchiveFormat.Xz,
        ["zstd"] = ArchiveFormat.Zstd,
        ["lzma"] = ArchiveFormat.Lzma,
        ["rar"] = ArchiveFormat.Rar,
        ["rar5"] = ArchiveFormat.Rar,
    };

    /// <summary>The first excluded type Pakko does not know, or null when it knows them all.</summary>
    public static string? FirstUnknown(IReadOnlyList<string> excludedTypes) =>
        excludedTypes.FirstOrDefault(name => !FormatByName.ContainsKey(name));

    /// <summary>
    /// <paramref name="archivePaths"/> without the archives of an excluded type, and those left
    /// out. Each name must be known (<see cref="FirstUnknown"/>).
    /// </summary>
    public static (IReadOnlyList<string> Kept, IReadOnlyList<string> Refused) Apply(
        IReadOnlyList<string> archivePaths, IReadOnlyList<string> excludedTypes)
    {
        if (excludedTypes.Count == 0)
            return (archivePaths, []);
        var excluded = new HashSet<ArchiveFormat>(excludedTypes.Select(name => FormatByName[name]));
        var kept = new List<string>();
        var refused = new List<string>();
        foreach (string path in archivePaths)
            (excluded.Contains(ArchiveFormatDetector.Detect(path)) ? refused : kept).Add(path);
        return (kept, refused);
    }
}
