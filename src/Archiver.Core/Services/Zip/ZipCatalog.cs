using System.IO.Compression;
using Archiver.Core.Services.Zip.Decryption;
using static Archiver.Core.Services.Zip.Decryption.RawZipEntryLocator;

namespace Archiver.Core.Services.Zip;

/// <summary>
/// T-F234: one ZIP entry with the name Pakko uses for it everywhere (list, extract, test, scan) —
/// decoded by 7-Zip's rule, never <see cref="ZipArchiveEntry.FullName"/>, which .NET decodes as
/// UTF-8 for every entry without the UTF-8 flag.
/// </summary>
/// <param name="Entry">The .NET entry, for its data, sizes and CRC-32; valid until the walk moves
/// past its batch (T-F529).</param>
/// <param name="FullName">The decoded name, '/'-separated.</param>
/// <param name="CollidesAfterDecoding">True when an earlier entry has different raw name bytes but
/// the same decoded name — writing both would silently lose one.</param>
/// <param name="ExtendedModifiedUtc">T-F298: the NTFS/extended-timestamp time, null when the entry
/// has none.</param>
/// <param name="Located">T-F529: the entry's raw facts, when the walk was asked for them; null when
/// they could not be read.</param>
internal sealed record NamedZipEntry(ZipArchiveEntry Entry, string FullName, bool CollidesAfterDecoding,
    DateTime? ExtendedModifiedUtc = null, LocatedZipEntry? Located = null)
{
    /// <summary>T-F298: the time extraction gives the file or folder — the extended time, else the
    /// entry's DOS time.</summary>
    public DateTime ModifiedUtc => ExtendedModifiedUtc ?? Entry.LastWriteTime.UtcDateTime;

    /// <summary>T-F529: the entry's place in the central directory.</summary>
    public int Index { get; init; }
}

/// <summary>One entry's name from <see cref="ZipCatalog.Names"/>: no .NET entry, no data.</summary>
internal readonly record struct ZipCatalogName(int Index, string FullName, long Length)
{
    public bool IsFolder => FullName.EndsWith('/');
}

/// <summary>What <see cref="ZipCatalog.Entries"/> reads of each entry's local header.</summary>
internal enum ZipLocate
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>Its raw facts; a batch whose local headers cannot all be read gets none (a list,
    /// a scan: as when the whole archive's could not be read).</summary>
    BestEffort,

    /// <summary>Its raw facts; a local header that cannot be read fails the walk (an extraction or a
    /// test with a password: as before).</summary>
    Required,
}

/// <summary>
/// T-F529: the one way Pakko reads a ZIP's entries (it replaced T-F234's <c>ZipArchiveReader</c>,
/// which held .NET's <see cref="ZipArchive"/> of every entry and a list of every name). The central
/// directory is walked as a stream; nothing of the whole archive is held but what a caller keeps.
/// <see cref="Names"/> walks it for names only; <see cref="Entries"/> walks it in batches, each
/// read by .NET's own <see cref="ZipArchive"/> over a <see cref="CentralDirectoryOverlayStream"/>,
/// so an entry's data is still opened, decompressed (Deflate64 included) and checked by .NET.
/// Each walk re-checks what .NET's reader checked of the whole directory (the count its end record
/// declares), so an archive that walks to the end is one .NET would have read.
/// </summary>
internal sealed class ZipCatalog : IDisposable
{
    internal const int DefaultBatchEntries = 4096;
    internal const int BatchBytes = 1 << 20;

    private readonly FileStream _directory;
    private readonly FileStream _data;
    private readonly ZipNameCodePages _codePages;
    private readonly CentralDirectoryExtent _extent;
    private readonly int _maxEntries;
    private readonly int _batchEntries;
    private readonly bool _forceZip64Overlay;

    private ZipCatalog(FileStream directory, FileStream data, ZipNameCodePages codePages, CentralDirectoryExtent extent,
        int maxEntries, int batchEntries, bool forceZip64Overlay)
    {
        _directory = directory;
        _data = data;
        _codePages = codePages;
        _extent = extent;
        _maxEntries = maxEntries;
        _batchEntries = batchEntries;
        _forceZip64Overlay = forceZip64Overlay;
    }

    /// <summary>The path, as opened.</summary>
    public string Path => _directory.Name;

    /// <summary>Each entry's decoded name and raw facts, read from its local header, without .NET's
    /// reader (T-F189's password check). Throws what a local header that cannot be read throws.</summary>
    public IEnumerable<(string FullName, LocatedZipEntry Located)> Located()
    {
        foreach (CentralDirectoryRecord record in WalkCentralDirectory(_directory, _extent, resolveZip64: true, _maxEntries))
        {
            string name = ZipEntryNameDecoder.Decode(record.NameBytes, record.GeneralPurposeFlag, record.HostOs, record.Extra, _codePages);
            yield return (name, LocateAll(_data, [record])[0]);
        }
    }

    /// <summary>How many entries the end record declares; a walk that ends has met exactly these.</summary>
    public long DeclaredEntries => _extent.ExpectedEntries;

    // T-F513: the end records are read first - an archive over ArchiveLimits.MaxEntries is refused
    // before any record is.
    public static ZipCatalog Open(string path, ZipNameCodePages codePages) =>
        Open(path, codePages, ArchiveLimits.MaxEntries, DefaultBatchEntries, forceZip64Overlay: false);

    // Test seams: the limit, the batch and the overlay's end records.
    internal static ZipCatalog Open(string path, ZipNameCodePages codePages, int maxEntries, int batchEntries, bool forceZip64Overlay)
    {
        FileStream directory = File.OpenRead(path);
        FileStream? data = null;
        try
        {
            // A device (NUL, a pipe) is no ZIP: .NET's reader said so too, as damaged data.
            if (!directory.CanSeek)
                throw new InvalidDataException("A ZIP archive is read from a file, not a device.");
            CentralDirectoryExtent extent = FindCentralDirectory(directory, maxEntries);
            data = File.OpenRead(path);
            return new ZipCatalog(directory, data, codePages, extent, maxEntries, batchEntries, forceZip64Overlay);
        }
        catch
        {
            data?.Dispose();
            directory.Dispose();
            throw;
        }
    }

    /// <summary>Every entry's decoded name and declared size, in directory order, without .NET's
    /// reader. Nothing is held. The size is the directory's 32-bit field, or its Zip64 value when it
    /// holds the marker.</summary>
    public IEnumerable<ZipCatalogName> Names()
    {
        int index = 0;
        foreach (CentralDirectoryRecord record in WalkCentralDirectory(_directory, _extent, resolveZip64: false, _maxEntries))
        {
            string name = ZipEntryNameDecoder.Decode(record.NameBytes, record.GeneralPurposeFlag, record.HostOs, record.Extra, _codePages);
            yield return new ZipCatalogName(index++, name, DeclaredLength(record));
        }
    }

    /// <summary>
    /// Every entry, in directory order, a batch at a time: a batch's entries are valid until the
    /// walk moves past it, so a caller reads each where it gets it. With <paramref name="collisions"/>,
    /// T-F234's <see cref="NamedZipEntry.CollidesAfterDecoding"/> is set (the one thing held then: each
    /// distinct file name with its first raw bytes); <paramref name="locate"/> says whether each
    /// entry's raw facts (an encrypted entry's offsets) are read. A walk that ends has passed every
    /// check .NET's reader made of the whole archive; a caller that acts on entries before the end
    /// (extract, test, scan) walks once first without acting.
    /// </summary>
    public IEnumerable<NamedZipEntry> Entries(bool collisions = false, ZipLocate locate = ZipLocate.None)
    {
        Dictionary<string, byte[]>? firstRawByName = collisions ? new(StringComparer.Ordinal) : null;
        var batch = new List<CentralDirectoryRecord>(_batchEntries);
        long batchBytes = 0;
        int firstIndex = 0;
        foreach (CentralDirectoryRecord record in WalkCentralDirectory(_directory, _extent, resolveZip64: false, _maxEntries))
        {
            batch.Add(record);
            batchBytes += record.RecordEnd - record.RecordStart;
            if (batch.Count < _batchEntries && batchBytes < BatchBytes)
                continue;
            foreach (NamedZipEntry entry in ReadBatch(batch, firstIndex, firstRawByName, locate))
                yield return entry;
            firstIndex += batch.Count;
            batch.Clear();
            batchBytes = 0;
        }
        if (batch.Count > 0)
        {
            foreach (NamedZipEntry entry in ReadBatch(batch, firstIndex, firstRawByName, locate))
                yield return entry;
        }
    }

    /// <summary>Walks every entry without acting: what <see cref="Entries"/> would throw, thrown before
    /// a caller acts on any entry (T-F529: as .NET's reader threw on open, when it held them all).</summary>
    public void Check(ZipLocate locate = ZipLocate.None)
    {
        foreach (NamedZipEntry _ in Entries(locate: locate))
        {
            /* walked for its checks only */
        }
    }

    // The batch's records are contiguous in the directory: one read copies them for the overlay.
    private IEnumerable<NamedZipEntry> ReadBatch(
        List<CentralDirectoryRecord> records, int firstIndex, Dictionary<string, byte[]>? firstRawByName, ZipLocate locate)
    {
        long start = records[0].RecordStart;
        byte[] raw = new byte[records[^1].RecordEnd - start];
        _data.Seek(start, SeekOrigin.Begin);
        _data.ReadExactly(raw);

        List<LocatedZipEntry>? located = locate switch
        {
            ZipLocate.Required => LocateAll(_data, [.. records.Select(ResolveZip64)]),
            ZipLocate.BestEffort => TryLocate(records),
            _ => null,
        };
        using var overlay = new CentralDirectoryOverlayStream(_data, raw, records.Count, _extent.DiskNumber, _forceZip64Overlay);
        using var archive = new ZipArchive(overlay, ZipArchiveMode.Read, leaveOpen: true);
        System.Collections.ObjectModel.ReadOnlyCollection<ZipArchiveEntry> entries = archive.Entries;
        overlay.HideTail();
        if (entries.Count != records.Count)
            throw new InvalidDataException("ZIP central directory entry count mismatch while reading entry names.");

        for (int i = 0; i < records.Count; i++)
        {
            CentralDirectoryRecord record = records[i];
            int index = firstIndex + i;
            string name = ZipEntryNameDecoder.Decode(record.NameBytes, record.GeneralPurposeFlag, record.HostOs, record.Extra, _codePages);
            yield return new NamedZipEntry(entries[i], name, Collides(firstRawByName, name, record.NameBytes),
                ZipExtendedTime.TryReadModifiedUtc(record.Extra), located?[i]) { Index = index };
        }
    }

    private static bool Collides(Dictionary<string, byte[]>? firstRawByName, string name, byte[] rawName)
    {
        if (firstRawByName is null || name.EndsWith('/'))
            return false;
        if (firstRawByName.TryGetValue(name, out byte[]? firstRaw))
            return !firstRaw.AsSpan().SequenceEqual(rawName);
        firstRawByName[name] = rawName;
        return false;
    }

    // T-F189: a local header that cannot be read leaves the batch without raw facts, not the read
    // failing: its encrypted entries are then reported as unreadable by each caller's own rule.
    private List<LocatedZipEntry>? TryLocate(List<CentralDirectoryRecord> records)
    {
        try
        {
            return LocateAll(_data, [.. records.Select(ResolveZip64)]);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return null;
        }
    }

    private static CentralDirectoryRecord ResolveZip64(CentralDirectoryRecord record)
    {
        (long uncompressed, long compressed, long offset) = ResolveZip64Fields(
            record.Extra, (uint)record.UncompressedSize, (uint)record.CompressedSize, (uint)record.LocalHeaderOffset);
        return record with { UncompressedSize = uncompressed, CompressedSize = compressed, LocalHeaderOffset = offset };
    }

    private static long DeclaredLength(CentralDirectoryRecord record)
    {
        (long? uncompressed, _, _) = TryResolveCentralZip64(record.Extra, record.UncompressedSize, record.CompressedSize, record.LocalHeaderOffset);
        return uncompressed ?? record.UncompressedSize;
    }

    public void Dispose()
    {
        _data.Dispose();
        _directory.Dispose();
    }
}
