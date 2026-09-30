namespace Archiver.Core.Models;

/// <summary>
/// One entry inside an archive, as reported by IArchiveService/ITarService's ListEntriesAsync.
/// Flat — Path is the full archive-internal path ('/'-separated, no leading slash). Building a
/// folder hierarchy out of these is an App-layer concern (Archiver.Core has zero WinUI/UI-model
/// references), not Core's.
/// </summary>
public sealed record ArchiveEntryInfo
{
    public required string Path { get; init; }
    public long Size { get; init; }
    /// <summary>Null for tar-family, 7z and RAR entries: their listing has no per-entry packed
    /// size (a compressed tar is one whole stream). 0 is a real size, e.g. an empty ZIP entry.</summary>
    public long? CompressedSize { get; init; }

    // Null when not reliably derivable — tar-family listing has no per-entry CRC concept at all
    // (same whole-archive-vs-per-entry reason as CompressedSize). A value of 0 is a legitimate
    // CRC-32 (e.g. an empty file), so this must stay nullable rather than using 0 as a sentinel.
    public uint? Crc32 { get; init; }

    /// <summary>Local time, as the archive's own listing shows it. Tar-family (T-F214): to the
    /// minute within half a year of now, else the date only (00:00); null when tar.exe's date
    /// columns cannot be read.</summary>
    public DateTime? Modified { get; init; }

    public bool IsDirectory { get; init; }

    /// <summary>Null when the format cannot say without extracting (tar-family, 7z, RAR).</summary>
    public EntryEncryption? Encryption { get; init; }

    /// <summary>WinZip AE version (1 keeps the real CRC-32, 2 stores 0 and relies on the HMAC);
    /// null unless the entry is WinZip AES-encrypted.</summary>
    public int? AesVersion { get; init; }
}
