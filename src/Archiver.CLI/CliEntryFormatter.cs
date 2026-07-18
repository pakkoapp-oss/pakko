using System.Globalization;
using Archiver.Core.Models;

namespace Archiver.CLI;

/// <summary>
/// Formats ArchiveEntryInfo rows for the 'l' (List) command — tab-separated, pipeline-friendly,
/// not a reimplementation of 7z's own box-drawn table. Nullable fields (Crc32/Modified, always
/// null for tar-family entries) render as a literal '-' so column count stays stable for any tool
/// splitting on tab.
/// </summary>
public static class CliEntryFormatter
{
    public const string Header = "Size\tCompressed\tCrc32\tModified\tType\tPath";

    public static string FormatRow(ArchiveEntryInfo entry)
    {
        string crc = entry.Crc32 is { } crc32 ? crc32.ToString("x8", CultureInfo.InvariantCulture) : "-";
        string modifiedText = entry.Modified is { } modified
            ? modified.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
            : "-";
        string type = entry.IsDirectory ? "d" : "f";

        return $"{entry.Size}\t{entry.CompressedSize}\t{crc}\t{modifiedText}\t{type}\t{entry.Path}";
    }
}
