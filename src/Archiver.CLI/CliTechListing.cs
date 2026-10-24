using System.Globalization;
using Archiver.Core.Models;

namespace Archiver.CLI;

/// <summary>
/// T-F412: "l -slt", 7-Zip's technical listing (CPP/7zip/UI/Console/List.cpp at 26.02). Per archive:
/// "Listing archive: X", the "--" block with Path, Type and Physical Size, then "----------"
/// (headers, left out under -ba); per entry one "Name = value" line per field and a blank line.
/// 7-Zip's fields come from the format's handler; Pakko prints those it knows, in that order: ZIP
/// Path, Folder, Size, Packed Size, Modified, Encrypted, CRC; 7z Path, Size, Modified; tar-family and
/// RAR Path, Folder, Size, Modified. A value Pakko lacks is printed empty, as 7-Zip prints a property
/// the item does not have. Paths use '\', as 7-Zip's on Windows.
/// </summary>
public static class CliTechListing
{
    private const char ReplaceChar = '_';

    /// <summary>The lines before the first entry of <paramref name="archivePath"/>.</summary>
    public static IReadOnlyList<string> ArchiveHeader(string archivePath, ArchiveFormat format, long physicalSize, bool terminal)
    {
        string path = ForConsole(archivePath, terminal);
        return
        [
            "", "Listing archive: " + path, "",
            "--", "Path = " + path, "Type = " + CliArchiveTypeExclusion.TypeName(format),
            "Physical Size = " + physicalSize.ToString(CultureInfo.InvariantCulture),
            "", "----------",
        ];
    }

    /// <summary>One entry's block, its closing blank line included. <paramref name="format"/> is
    /// the archive's outer format by its bytes (a .tar.gz is GZip).</summary>
    public static IReadOnlyList<string> EntryBlock(ArchiveEntryInfo entry, ArchiveFormat format, bool terminal)
    {
        var lines = new List<string> { "Path = " + ForConsole(entry.Path.Replace('/', '\\'), terminal) };
        if (format != ArchiveFormat.SevenZip)
            lines.Add("Folder = " + (entry.IsDirectory ? "+" : "-"));
        lines.Add("Size = " + entry.Size.ToString(CultureInfo.InvariantCulture));
        if (format == ArchiveFormat.Zip)
            lines.Add("Packed Size = " + entry.CompressedSize?.ToString(CultureInfo.InvariantCulture));
        lines.Add("Modified = " + Modified(entry, format));
        if (format == ArchiveFormat.Zip)
        {
            lines.Add("Encrypted = " + entry.Encryption switch
            {
                null => "",
                EntryEncryption.None => "-",
                _ => "+",
            });
            lines.Add("CRC = " + entry.Crc32?.ToString("X8", CultureInfo.InvariantCulture));
        }
        lines.Add("");
        return lines;
    }

    /// <summary>
    /// <paramref name="text"/> as 7-Zip's CStdOutStream::Normalize_UString writes it: on a terminal
    /// every C0 control, DEL and C1 control and the BiDi controls U+202A..U+202E and U+2066..U+2069
    /// become '_'; redirected, only LF and CR do (one name stays one line).
    /// </summary>
    public static string ForConsole(string text, bool terminal) =>
        string.Create(text.Length, (text, terminal), static (span, state) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                char c = state.text[i];
                bool replace = state.terminal ? IsDangerousTerminalChar(c) : c is '\n' or '\r';
                span[i] = replace ? ReplaceChar : c;
            }
        });

    private static bool IsDangerousTerminalChar(char c) =>
        c < 0x20 || (c >= 0x7F && c <= 0x9F) || (c >= 0x202A && c <= 0x202E) || (c >= 0x2066 && c <= 0x2069);

    // 7-Zip's ConvertUtcFileTimeToString2 at the precision the item has: ZIP's to the second;
    // tar.exe's listing (tar-family, 7z, RAR) to the minute, or the day alone (T-F335).
    private static string Modified(ArchiveEntryInfo entry, ArchiveFormat format)
    {
        if (entry.Modified is not { } modified)
            return "";
        string pattern = "yyyy-MM-dd";
        if (entry.ModifiedHasTime)
            pattern += format == ArchiveFormat.Zip ? " HH:mm:ss" : " HH:mm";
        return modified.ToString(pattern, CultureInfo.InvariantCulture);
    }
}
