using System.Globalization;
using Archiver.Core.Models;

namespace Archiver.CLI;

/// <summary>
/// Formats ArchiveEntryInfo rows for the 'l' (List) command — tab-separated, pipeline-friendly,
/// not a reimplementation of 7z's own box-drawn table. Nullable fields (Compressed/Crc32 always
/// null for tar-family entries, Modified when unreadable) render as a literal '-' so column count stays stable for any tool
/// splitting on tab. Encrypted is '?' when the format cannot say (tar-family, 7z, RAR), '+'
/// when the entry is encrypted by a method Pakko cannot name (T-F221 item 7).
/// </summary>
public static class CliEntryFormatter
{
    public const string Header = "Size\tCompressed\tCrc32\tModified\tType\tEncrypted\tPath";

    public static string FormatRow(ArchiveEntryInfo entry)
    {
        string crc = entry.Crc32 is { } crc32 ? crc32.ToString("x8", CultureInfo.InvariantCulture) : "-";
        string modifiedFormat = entry.ModifiedHasTime ? "yyyy-MM-ddTHH:mm:ss" : "yyyy-MM-dd";
        string modifiedText = entry.Modified is { } modified
            ? modified.ToString(modifiedFormat, CultureInfo.InvariantCulture)
            : "-";
        string compressed = entry.CompressedSize is { } packed ? packed.ToString(CultureInfo.InvariantCulture) : "-";
        string type = entry.IsDirectory ? "d" : "f";
        string encrypted = entry.Encryption switch
        {
            null => "?",
            EntryEncryption.None => "-",
            EntryEncryption.ZipCrypto => "ZipCrypto",
            EntryEncryption.Aes128 => "AES-128",
            EntryEncryption.Aes192 => "AES-192",
            EntryEncryption.Aes256 => "AES-256",
            _ => "+",
        };

        return $"{entry.Size}\t{compressed}\t{crc}\t{modifiedText}\t{type}\t{encrypted}\t{entry.Path}";
    }
}
