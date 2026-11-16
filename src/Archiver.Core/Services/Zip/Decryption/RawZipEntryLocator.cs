using System.Linq;
using System.Text;

namespace Archiver.Core.Services.Zip.Decryption;


/// <summary>
/// Raw facts about one ZIP entry read directly from the central directory / local file header
/// bytes — independent of <see cref="System.IO.Compression.ZipArchiveEntry"/>, which exposes
/// neither the local-header offset nor the raw (possibly encrypted) entry bytes. Only used for
/// entries the caller already knows are encrypted (general-purpose bit 0) — see
/// <see cref="EncryptedZipEntryReader"/>.
/// </summary>
internal sealed record LocatedZipEntry
{
    public required long CompressedDataOffset { get; init; }
    public required long CompressedSize { get; init; }

    /// <summary>The declared uncompressed size, from the central directory (Zip64-resolved) — the
    /// same value <see cref="System.IO.Compression.ZipArchiveEntry.Length"/> reports. T-F231: the
    /// decrypted output is capped at it.</summary>
    public required long UncompressedSize { get; init; }
    public required ushort CompressionMethod { get; init; }
    public required bool GeneralPurposeEncryptedBit { get; init; }

    /// <summary>General-purpose bit 6: PKWARE strong encryption, which Pakko cannot decrypt.</summary>
    public bool StrongEncryptionBit { get; init; }
    public required uint StoredCrc32 { get; init; }

    /// <summary>T-F243: the byte the last ZipCrypto header byte must equal for the right password —
    /// the high byte of the local file time when the entry has a data descriptor (bit 3; the CRC-32
    /// is not known yet when the header is written), else the high byte of the CRC-32. 7-Zip
    /// (ZipHandler.cpp) checks the same.</summary>
    public required byte ZipCryptoCheckByte { get; init; }

    /// <summary>Null unless <see cref="CompressionMethod"/> is 99 (WinZip AE). 1 = AE-1 (keeps
    /// the real CRC-32 in the header), 2 = AE-2 (header CRC-32 is always 0; HMAC is authoritative).</summary>
    public int? AeVersion { get; init; }

    /// <summary>128/192/256, or 0 when the entry is not WinZip-AES-encrypted.</summary>
    public int AesStrengthBits { get; init; }

    /// <summary>The real compression method (e.g. 8 = Deflate, 0 = Store) applied before AES
    /// encryption. Equal to <see cref="CompressionMethod"/> itself when not AES-encrypted.</summary>
    public ushort RealCompressionMethod { get; init; }
}

internal static class RawZipEntryLocator
{
    private const uint CentralDirectorySignature = 0x02014b50;
    private const uint LocalFileHeaderSignature = 0x04034b50;
    private const uint EndOfCentralDirectorySignature = 0x06054b50;
    private const uint Zip64EndOfCentralDirectorySignature = 0x06064b50;
    private const uint Zip64EndOfCentralDirectoryLocatorSignature = 0x07064b50;
    private const int Zip64LocatorLength = 20;
    private const ushort Zip64ExtraId = 0x0001;
    private const uint Zip32Sentinel = 0xFFFFFFFF;
    private const ushort WinZipAesExtraId = 0x9901;
    private const ushort WinZipAesCompressionMethod = 99;
    // version(2) + vendor "AE"(2) + strength(1) + real compression method(2)
    private const int WinZipAesExtraMinLength = 7;
    private const ushort DataDescriptorFlag = 0x0008;

    public static LocatedZipEntry Locate(Stream zipStream, string entryFullName)
    {
        // T-F234: matched by decoded name, the name every caller sees — a raw UTF-8 byte compare
        // missed every legacy (OEM/ANSI) name.
        var pages = ZipNameCodePages.Current();
        CentralDirectoryRecord record = ReadCentralDirectory(zipStream).FirstOrDefault(r =>
                ZipEntryNameDecoder.Decode(r.NameBytes, r.GeneralPurposeFlag, r.HostOs, r.Extra, pages) == entryFullName)
            ?? throw new FileNotFoundException($"Entry not found in ZIP central directory: {entryFullName}");
        return BuildFromLocalHeader(zipStream, record);
    }

    /// <summary>T-F529: <see cref="LocateAll(Stream)"/> for some records only - one batch of a
    /// <see cref="ZipCatalog"/>, read from <paramref name="zipStream"/>, which it seeks.</summary>
    internal static List<LocatedZipEntry> LocateAll(Stream zipStream, IReadOnlyList<CentralDirectoryRecord> records)
    {
        var result = new List<LocatedZipEntry>(records.Count);
        foreach (CentralDirectoryRecord record in records)
            result.Add(BuildFromLocalHeader(zipStream, record));
        return result;
    }

    /// <summary>
    /// Walks the whole central directory once and returns every entry's <see cref="LocatedZipEntry"/>
    /// in central-directory order — the same order <see cref="System.IO.Compression.ZipArchive.Entries"/>
    /// populates its own list in, so a caller can pair the two positionally by index. Deliberately
    /// NOT name-based: positional pairing needs no name at all, so it stays exact even for two
    /// entries with the same name. (T-F234: names themselves come from <see cref="ReadEntryNames"/>.)
    /// <para>
    /// Two full passes over <paramref name="zipStream"/> deliberately: <see cref="ReadCentralDirectory"/>
    /// reads only the central directory (one contiguous forward scan, no seeking away), THEN this
    /// method seeks out to each entry's local header one at a time — interleaving the two (seeking
    /// to a local header mid-central-directory-scan) would corrupt the central directory read
    /// position for every entry after the first.
    /// </para>
    /// </summary>
    public static List<LocatedZipEntry> LocateAll(Stream zipStream) =>
        LocateAll(zipStream, [.. ReadCentralDirectory(zipStream)]);

    /// <summary>
    /// T-F189: cheap "does this archive need a password prompt at all" gate — reads only the
    /// central directory's own general-purpose bit flag per entry (no local-header seeks), so it
    /// stays fast even for a large archive. Deliberately checks EVERY entry, not just the first —
    /// this project's own <c>mixed_encrypted_and_plain.zip</c> test fixture puts its unencrypted
    /// entry first and its encrypted entry second, which a first-entry-only check (the pre-T-F189
    /// behavior) would silently miss. See docs/DECISIONS.md's T-F189 entry.
    /// </summary>
    public static bool HasAnyEncryptedEntry(Stream zipStream) =>
        ReadCentralDirectory(zipStream).Any(record => (record.GeneralPurposeFlag & 0x0001) != 0);

    /// <summary>
    /// T-F234: every entry's name decoded by 7-Zip's rule (<see cref="ZipEntryNameDecoder"/>), with
    /// its raw bytes, in central-directory order — the order <see cref="System.IO.Compression.ZipArchive.Entries"/>
    /// uses, so the two pair positionally. Reads the central directory only: no local-header seeks
    /// and no WinZip AES checks, so any archive whose directory .NET can read gets names too.
    /// T-F298: also each entry's NTFS/extended-timestamp modification time, null when it has none.
    /// </summary>
    /// T-F513: an archive with more than <paramref name="maxEntries"/> entries is refused
    /// (<see cref="ArchiveLimits.TooManyEntries"/>) before any of them is read.
    public static List<(byte[] RawName, string Name, DateTime? ExtendedModifiedUtc)> ReadEntryNames(
        Stream zipStream, ZipNameCodePages codePages, int maxEntries = ArchiveLimits.MaxEntries) =>
        ReadCentralDirectory(zipStream, resolveZip64: false, maxEntries)
            .Select(r => (r.NameBytes, ZipEntryNameDecoder.Decode(r.NameBytes, r.GeneralPurposeFlag, r.HostOs, r.Extra, codePages),
                ZipExtendedTime.TryReadModifiedUtc(r.Extra)))
            .ToList();

    /// <summary>
    /// T-F280: the central-directory indexes of entries whose local header disagrees with the
    /// central directory — different raw name bytes, or, unless the entry uses a data descriptor
    /// (bit 3, where the local CRC and sizes are legitimately zero), a different CRC-32 or size.
    /// Other tools read the local header, so such an archive can extract differently elsewhere;
    /// 7-Zip's Test calls this "Headers Error". A local header that cannot be read is itself a
    /// mismatch, never an exception; a Zip64 field that cannot be resolved is not compared.
    /// </summary>
    /// T-F529: how many, and the first; the walk reads one record at a time.
    public static (int Count, int First) FindLocalHeaderMismatches(Stream zipStream)
    {
        int count = 0, first = -1, index = 0;
        foreach (CentralDirectoryRecord record in ReadCentralDirectory(zipStream, resolveZip64: false))
        {
            if (!LocalHeaderMatches(zipStream, record))
                first = count++ == 0 ? index : first;
            index++;
        }
        return (count, first);
    }

    private const int LocalHeaderFixedLength = 30;

    private static bool LocalHeaderMatches(Stream zipStream, CentralDirectoryRecord central)
    {
        (long? centralUncompressed, long? centralCompressed, long? localOffset) =
            TryResolveCentralZip64(central.Extra, central.UncompressedSize, central.CompressedSize, central.LocalHeaderOffset);
        if (localOffset is not { } offset)
            return true; // its local header cannot be found without the Zip64 offset: not compared
        if (offset > zipStream.Length - LocalHeaderFixedLength)
            return false;

        byte[] header = new byte[LocalHeaderFixedLength];
        zipStream.Seek(offset, SeekOrigin.Begin);
        ReadExact(zipStream, header);
        if (BitConverter.ToUInt32(header, 0) != LocalFileHeaderSignature)
            return false;
        ushort flags = BitConverter.ToUInt16(header, 6);
        uint crc32 = BitConverter.ToUInt32(header, 14);
        uint compressedField = BitConverter.ToUInt32(header, 18);
        uint uncompressedField = BitConverter.ToUInt32(header, 22);
        int nameLength = BitConverter.ToUInt16(header, 26);
        int extraLength = BitConverter.ToUInt16(header, 28);
        if (nameLength + extraLength > zipStream.Length - zipStream.Position)
            return false;
        byte[] name = ReadBytes(zipStream, nameLength);
        byte[] extra = ReadBytes(zipStream, extraLength);

        if (!name.AsSpan().SequenceEqual(central.NameBytes))
            return false;
        if ((flags & DataDescriptorFlag) != 0)
            return true;
        if (crc32 != central.Crc32)
            return false;

        (long? localUncompressed, long? localCompressed) = TryResolveLocalZip64(extra, uncompressedField, compressedField);
        return SameOrUnknown(localCompressed, centralCompressed) && SameOrUnknown(localUncompressed, centralUncompressed);
    }

    private static bool SameOrUnknown(long? a, long? b) => a is null || b is null || a == b;

    // The central record's Zip64 fields, in spec order, holding only the fields that are
    // sentinels. Null for a sentinel whose value cannot be read.
    internal static (long? Uncompressed, long? Compressed, long? Offset) TryResolveCentralZip64(
        byte[] extra, long uncompressed, long compressed, long offset)
    {
        byte[]? record = TryFindExtraRecord(extra, Zip64ExtraId);
        int position = 0;
        long? Next(long value)
        {
            if (value != Zip32Sentinel)
                return value;
            if (record is null || position > record.Length - 8)
                return null;
            long read = BitConverter.ToInt64(record, position);
            position += 8;
            return read >= 0 ? read : null;
        }
        long? realUncompressed = Next(uncompressed);
        long? realCompressed = Next(compressed);
        return (realUncompressed, realCompressed, Next(offset));
    }

    // A local header's Zip64 extra always holds both sizes, uncompressed first.
    private static (long? Uncompressed, long? Compressed) TryResolveLocalZip64(byte[] extra, uint uncompressed, uint compressed)
    {
        if (uncompressed != Zip32Sentinel && compressed != Zip32Sentinel)
            return (uncompressed, compressed);
        byte[]? record = TryFindExtraRecord(extra, Zip64ExtraId);
        if (record is null || record.Length < 16)
            return (uncompressed == Zip32Sentinel ? null : uncompressed, compressed == Zip32Sentinel ? null : compressed);
        long realUncompressed = BitConverter.ToInt64(record, 0);
        long realCompressed = BitConverter.ToInt64(record, 8);
        return (realUncompressed >= 0 ? realUncompressed : null, realCompressed >= 0 ? realCompressed : null);
    }

    private static byte[]? TryFindExtraRecord(byte[] extra, ushort headerId)
    {
        try
        {
            return FindExtraRecord(extra, headerId);
        }
        catch (InvalidDataException)
        {
            return null; // a malformed extra block holds no usable record
        }
    }

    /// <summary>One central-directory record; <see cref="RecordStart"/> and <see cref="RecordEnd"/>
    /// bound its raw bytes in the archive (T-F529: a <see cref="ZipCatalog"/> batch copies them).</summary>
    internal sealed record CentralDirectoryRecord(
        byte[] NameBytes, long LocalHeaderOffset, uint Crc32, long CompressedSize, long UncompressedSize,
        ushort GeneralPurposeFlag, byte HostOs, byte[] Extra, long RecordStart, long RecordEnd);

    private static IEnumerable<CentralDirectoryRecord> ReadCentralDirectory(
        Stream zipStream, bool resolveZip64 = true, int maxEntries = ArchiveLimits.MaxEntries) =>
        WalkCentralDirectory(zipStream, FindCentralDirectory(zipStream, maxEntries), resolveZip64, maxEntries);

    /// <summary>
    /// The records of the directory <paramref name="extent"/> bounds, one at a time (T-F529: nothing
    /// is held). The walk keeps its own position, so a caller may seek <paramref name="zipStream"/>
    /// between two records. After the last one it checks what .NET's reader checks of the whole
    /// directory (<c>ZipArchive.ReadCentralDirectory</c>, v11.0.0, which reads records until a
    /// signature that is not one and then wants exactly the count the end record declares): the
    /// count, and no record right after the extent.
    /// </summary>
    /// resolveZip64: false for the name pass — names need no sizes or offsets, and a size sentinel
    /// without a Zip64 extra (which .NET's own reader tolerates) must not make an archive unreadable.
    /// T-F513: refuses more than maxEntries entries twice - by the count the end records declare,
    /// before a record is read, and by the records the directory's extent really holds, which a
    /// crafted archive can make more than it declares (46 bytes each at least).
    internal static IEnumerable<CentralDirectoryRecord> WalkCentralDirectory(
        Stream zipStream, CentralDirectoryExtent extent, bool resolveZip64 = true, int maxEntries = ArchiveLimits.MaxEntries)
    {
        // T-F193 Phase 0: bounded by the directory's own declared extent, not "up to the classic
        // EOCD" — in a Zip64 archive the Zip64 EOCD record and locator sit between the two, and
        // used to be misread as a malformed directory entry.
        long centralDirEnd = extent.Offset + extent.Size;
        long position = extent.Offset;
        long count = 0;
        while (position < centralDirEnd)
        {
            zipStream.Seek(position, SeekOrigin.Begin);
            uint signature = ReadUInt32(zipStream);
            if (signature != CentralDirectorySignature)
                throw new InvalidDataException("Malformed ZIP central directory (bad entry signature).");
            if (count >= maxEntries)
                throw ArchiveLimits.TooManyEntries(maxEntries);
            // As .NET's reader, which throws on the record past the count it expects: a caller may
            // keep something per index up to the declared count (T-F529).
            if (count >= extent.ExpectedEntries)
                throw new InvalidDataException(extent.DotNetRefusal ?? NumEntriesWrongText);

            ushort versionMadeBy = ReadUInt16(zipStream);
            ReadUInt16(zipStream); // version needed
            ushort generalPurposeFlag = ReadUInt16(zipStream);
            ReadUInt16(zipStream); // compression method — the local header's copy is the one used
            ReadUInt16(zipStream); // last mod time
            ReadUInt16(zipStream); // last mod date
            uint crc32 = ReadUInt32(zipStream);
            uint compressedSize = ReadUInt32(zipStream);
            uint uncompressedSize = ReadUInt32(zipStream);
            ushort nameLength = ReadUInt16(zipStream);
            ushort extraLength = ReadUInt16(zipStream);
            ushort commentLength = ReadUInt16(zipStream);
            ReadUInt16(zipStream); // disk number start
            ReadUInt16(zipStream); // internal attributes
            ReadUInt32(zipStream); // external attributes
            uint localHeaderOffset = ReadUInt32(zipStream);
            byte[] nameBytes = ReadBytes(zipStream, nameLength);
            byte[] extra = ReadBytes(zipStream, extraLength);
            if (commentLength > zipStream.Length - zipStream.Position)
                throw new EndOfStreamException();
            long recordEnd = zipStream.Position + commentLength;

            (long realUncompressedSize, long realCompressedSize, long realLocalHeaderOffset) = resolveZip64
                ? ResolveZip64Fields(extra, uncompressedSize, compressedSize, localHeaderOffset)
                : (uncompressedSize, compressedSize, localHeaderOffset);
            count++;
            yield return new CentralDirectoryRecord(
                nameBytes, realLocalHeaderOffset, crc32, realCompressedSize, realUncompressedSize, generalPurposeFlag,
                (byte)(versionMadeBy >> 8), extra, position, recordEnd);
            position = recordEnd;
        }

        if (extent.DotNetRefusal is { } refusal)
            throw new InvalidDataException(refusal);
        if (count != extent.ExpectedEntries || StartsWithRecord(zipStream, position))
            throw new InvalidDataException(NumEntriesWrongText);
    }

    // System.IO.Compression's SR.NumEntriesWrong (v11.0.0): the text .NET's reader gave for this.
    internal const string NumEntriesWrongText =
        "Number of entries expected in End Of Central Directory does not correspond to number of entries in Central Directory.";

    // .NET tries a record only where its 46-byte fixed part fits.
    private static bool StartsWithRecord(Stream zipStream, long position)
    {
        if (position > zipStream.Length - 46)
            return false;
        zipStream.Seek(position, SeekOrigin.Begin);
        return ReadUInt32(zipStream) == CentralDirectorySignature;
    }

    // T-F193 Phase 0: a central record whose 32-bit size/offset field holds the 0xFFFFFFFF sentinel
    // carries the real value in its Zip64 (0x0001) extra field — in spec order (uncompressed,
    // compressed, local-header offset), containing only the fields that were sentinels.
    internal static (long UncompressedSize, long CompressedSize, long LocalHeaderOffset) ResolveZip64Fields(
        byte[] extra, uint uncompressedSize, uint compressedSize, uint localHeaderOffset)
    {
        bool needUncompressed = uncompressedSize == Zip32Sentinel;
        bool needCompressed = compressedSize == Zip32Sentinel;
        bool needOffset = localHeaderOffset == Zip32Sentinel;
        if (!needUncompressed && !needCompressed && !needOffset)
            return (uncompressedSize, compressedSize, localHeaderOffset);

        byte[] record = FindExtraRecord(extra, Zip64ExtraId)
            ?? throw new InvalidDataException("ZIP entry uses Zip64 sentinel values but has no Zip64 extra field.");

        int position = 0;
        long realUncompressed = uncompressedSize;
        long realCompressed = compressedSize;
        long realOffset = localHeaderOffset;
        if (needUncompressed)
        {
            realUncompressed = ReadZip64Value(record, position);
            position += 8;
        }
        if (needCompressed)
        {
            realCompressed = ReadZip64Value(record, position);
            position += 8;
        }
        if (needOffset)
            realOffset = ReadZip64Value(record, position);

        return (realUncompressed, realCompressed, realOffset);
    }

    private static long ReadZip64Value(byte[] record, int position)
    {
        if (position > record.Length - 8)
            throw new InvalidDataException("Malformed Zip64 extra field (too short).");
        ulong value = BitConverter.ToUInt64(record, position);
        if (value > long.MaxValue)
            throw new InvalidDataException("Zip64 extra field value out of range.");
        return (long)value;
    }

    private static LocatedZipEntry BuildFromLocalHeader(Stream zipStream, CentralDirectoryRecord central)
    {
        uint centralCrc32 = central.Crc32;
        long centralCompressedSize = central.CompressedSize;
        // T-F240: 30 = the local header's fixed part. A crafted offset past the end otherwise
        // throws ArgumentOutOfRange (MemoryStream) or EndOfStream, which callers don't all map.
        if (central.LocalHeaderOffset > zipStream.Length - 30)
            throw new InvalidDataException("ZIP local header offset is past the end of the archive.");
        zipStream.Seek(central.LocalHeaderOffset, SeekOrigin.Begin);
        uint signature = ReadUInt32(zipStream);
        if (signature != LocalFileHeaderSignature)
            throw new InvalidDataException("Malformed ZIP local file header (bad signature).");

        ReadUInt16(zipStream); // version needed
        ushort generalPurposeFlag = ReadUInt16(zipStream);
        ushort method = ReadUInt16(zipStream);
        ushort lastModTime = ReadUInt16(zipStream);
        ReadUInt16(zipStream); // last mod date
        uint localCrc32 = ReadUInt32(zipStream);
        uint localCompressedSize = ReadUInt32(zipStream);
        ReadUInt32(zipStream); // uncompressed size
        ushort nameLength = ReadUInt16(zipStream);
        ushort extraLength = ReadUInt16(zipStream);
        ReadBytes(zipStream, nameLength);
        byte[] extra = ReadBytes(zipStream, extraLength);

        long compressedDataOffset = zipStream.Position;

        int? aeVersion = null;
        int aesStrengthBits = 0;
        ushort realMethod = method;

        if (method == WinZipAesCompressionMethod)
        {
            byte[] record = FindExtraRecord(extra, WinZipAesExtraId)
                ?? throw new InvalidDataException(
                    "Entry's compression method is WinZip AES (99) but no 0x9901 extra field was found.");
            if (record.Length < WinZipAesExtraMinLength)
                throw new InvalidDataException("Malformed WinZip AES (0x9901) extra field (too short).");

            aeVersion = BitConverter.ToUInt16(record, 0);
            byte strengthCode = record[4]; // record[2..4] is the "AE" vendor id, skipped
            aesStrengthBits = strengthCode switch
            {
                1 => 128,
                2 => 192,
                3 => 256,
                _ => throw new InvalidDataException($"Unknown WinZip AES strength code {strengthCode}.")
            };
            realMethod = BitConverter.ToUInt16(record, 5);
        }

        // Data-descriptor entries (general-purpose bit 3) legitimately zero these local-header
        // fields; the central directory's copy is authoritative in that case. AE-2 also zeroes
        // the local CRC-32 deliberately (by design, not a data descriptor) — its central-directory
        // copy is 0 too, so falling back here is a no-op for AE-2 and correct for data descriptors.
        uint storedCrc32 = localCrc32 != 0 ? localCrc32 : centralCrc32;
        return new LocatedZipEntry
        {
            CompressedDataOffset = compressedDataOffset,
            // T-F193 Phase 0: 0xFFFFFFFF means the real size is in a Zip64 extra (7-Zip writes this
            // when reading from stdin) — the central directory's already-resolved value is used then.
            CompressedSize = localCompressedSize is not 0 and not Zip32Sentinel ? localCompressedSize : centralCompressedSize,
            UncompressedSize = central.UncompressedSize,
            CompressionMethod = method,
            GeneralPurposeEncryptedBit = (generalPurposeFlag & 0x0001) != 0,
            StrongEncryptionBit = (generalPurposeFlag & 0x0040) != 0,
            StoredCrc32 = storedCrc32,
            ZipCryptoCheckByte = (generalPurposeFlag & DataDescriptorFlag) != 0
                ? (byte)(lastModTime >> 8)
                : (byte)(storedCrc32 >> 24),
            AeVersion = aeVersion,
            AesStrengthBits = aesStrengthBits,
            RealCompressionMethod = realMethod,
        };
    }

    private static byte[]? FindExtraRecord(byte[] extra, ushort headerId)
    {
        int position = 0;
        while (position + 4 <= extra.Length)
        {
            ushort id = BitConverter.ToUInt16(extra, position);
            ushort size = BitConverter.ToUInt16(extra, position + 2);
            // T-F194: size is attacker-controlled — must fit what's actually left in the block.
            if (size > extra.Length - position - 4)
                throw new InvalidDataException("Malformed ZIP extra field (record size runs past the extra block).");
            if (id == headerId)
                return extra.AsSpan(position + 4, size).ToArray();
            position += 4 + size;
        }
        return null;
    }

    /// <summary>T-F529: where the central directory is, how many records .NET's reader expects there
    /// and the disk number it reads entries against - one parse for every pass and batch.</summary>
    /// <param name="Offset">Where the directory starts.</param>
    /// <param name="Size">How long the end record says it is.</param>
    /// <param name="ExpectedEntries">How many records .NET's reader wants in it.</param>
    /// <param name="DiskNumber">The disk .NET's reader reads entries against.</param>
    /// <param name="DotNetRefusal">What .NET's reader refused the end records for, in its own words;
    /// thrown by a walk once Pakko's own checks of the records passed, as before T-F529, when Pakko's
    /// pass ran first and .NET's reader after it.</param>
    internal readonly record struct CentralDirectoryExtent(
        long Offset, long Size, long ExpectedEntries, uint DiskNumber, string? DotNetRefusal = null);

    // T-F529: the end records as .NET's reader takes them (dotnet/runtime v11.0.0, ZipArchive.
    // ReadEndOfCentralDirectory, ReadEndOfCentralDirectoryInnerWork, TryReadZip64EndOfCentralDirectory
    // and its two Inner*Work, quoted in docs/DECISIONS.md "T-F529"): the classic record's values, or the
    // Zip64 record's when the classic one holds a 0xFFFF/0xFFFFFFFF marker and the locator stands right
    // before it; a split or spanned archive refused, with .NET's own texts. A ZipCatalog batch shows
    // .NET only its own records, so these checks of the whole archive are made here.
    // T-F513: and refuses an entry count over maxEntries in either record, wherever the locator stands,
    // so it is never looser than .NET. Pakko's own checks, which ran before .NET's reader did, keep
    // their order and texts: the Zip64 record's offset and signature, the directory's extent.
    internal static CentralDirectoryExtent FindCentralDirectory(Stream zipStream, int maxEntries = ArchiveLimits.MaxEntries)
    {
        long eocdOffset = FindEndOfCentralDirectory(zipStream);
        zipStream.Seek(eocdOffset + 4, SeekOrigin.Begin);
        ushort thisDisk = ReadUInt16(zipStream);
        ushort directoryDisk = ReadUInt16(zipStream);
        ushort countOnThisDisk = ReadUInt16(zipStream);
        ushort count = ReadUInt16(zipStream);
        if (count > maxEntries)
            throw ArchiveLimits.TooManyEntries(maxEntries);
        uint size32 = ReadUInt32(zipStream);
        uint offset32 = ReadUInt32(zipStream);
        ushort commentLength = ReadUInt16(zipStream);
        // .NET's ZipEndOfCentralDirectoryBlock.TryReadBlockInitialize: a comment past the end is no
        // end record, and none is searched for further.
        string? refusal = null;
        if (commentLength > zipStream.Length - zipStream.Position)
            refusal = EndRecordNotFoundText;
        else if (thisDisk != directoryDisk || count != countOnThisDisk)
            refusal = SplitSpannedText;

        var extent = new CentralDirectoryExtent(offset32, size32, count, thisDisk, refusal);
        bool wantsZip64 = thisDisk == ushort.MaxValue || offset32 == Zip32Sentinel || count == ushort.MaxValue;
        if (eocdOffset >= Zip64LocatorLength)
        {
            CentralDirectoryExtent? zip64 = ReadZip64Extent(zipStream, eocdOffset, maxEntries);
            if (zip64 is { } found && wantsZip64 && refusal is null)
                extent = found;
        }
        else if (wantsZip64 && refusal is null)
        {
            extent = extent with { DotNetRefusal = Zip64RecordNotWhereExpectedText };
        }

        if (extent.Offset > zipStream.Length || extent.Size > zipStream.Length - extent.Offset)
            throw new InvalidDataException("ZIP central directory extends outside the archive.");
        return extent;
    }

    // The Zip64 record a locator right before the end record points at, checked whether or not the
    // classic record asks for it (as before); null when no locator stands there.
    private static CentralDirectoryExtent? ReadZip64Extent(Stream zipStream, long eocdOffset, int maxEntries)
    {
        zipStream.Seek(eocdOffset - Zip64LocatorLength, SeekOrigin.Begin);
        if (ReadUInt32(zipStream) != Zip64EndOfCentralDirectoryLocatorSignature)
            return null;
        ReadUInt32(zipStream); // disk with the Zip64 EOCD
        long zip64EocdOffset = ReadInt64Checked(zipStream);
        if (zip64EocdOffset > zipStream.Length - 56)
            throw new InvalidDataException("Zip64 end-of-central-directory offset is outside the archive.");

        zipStream.Seek(zip64EocdOffset, SeekOrigin.Begin);
        if (ReadUInt32(zipStream) != Zip64EndOfCentralDirectorySignature)
            throw new InvalidDataException("Malformed Zip64 end-of-central-directory record.");
        zipStream.Seek(zip64EocdOffset + 16, SeekOrigin.Begin);
        uint zip64ThisDisk = ReadUInt32(zipStream);
        ReadUInt32(zipStream); // disk with the directory
        ulong zip64CountOnThisDisk = ReadUInt64(zipStream);
        ulong zip64Count = ReadUInt64(zipStream);
        if (zip64Count > (ulong)maxEntries)
            throw ArchiveLimits.TooManyEntries(maxEntries);
        long zip64Size = ReadInt64Checked(zipStream);
        long zip64Offset = ReadInt64Checked(zipStream);
        return new CentralDirectoryExtent(zip64Offset, zip64Size, (long)zip64Count, zip64ThisDisk,
            zip64Count != zip64CountOnThisDisk ? SplitSpannedText : null);
    }

    // System.IO.Compression's own texts (v11.0.0 Strings.resx: EOCDNotFound, SplitSpanned, Zip64EOCDNotWhereExpected).
    private const string EndRecordNotFoundText = "End of Central Directory record could not be found.";
    private const string SplitSpannedText = "Split or spanned archives are not supported.";
    private const string Zip64RecordNotWhereExpectedText = "Zip 64 End of Central Directory Record not where indicated.";

    private static long ReadInt64Checked(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExactSpan(stream, buffer);
        ulong value = BitConverter.ToUInt64(buffer);
        if (value > long.MaxValue)
            throw new InvalidDataException("ZIP field value out of range.");
        return (long)value;
    }

    private static long FindEndOfCentralDirectory(Stream zipStream)
    {
        long searchLength = Math.Min(zipStream.Length, 22 + 65536);
        byte[] tail = new byte[searchLength];
        zipStream.Seek(-searchLength, SeekOrigin.End);
        ReadExact(zipStream, tail);

        for (int i = tail.Length - 22; i >= 0; i--)
        {
            if (BitConverter.ToUInt32(tail, i) == EndOfCentralDirectorySignature)
                return zipStream.Length - searchLength + i;
        }

        throw new InvalidDataException("End of central directory record not found — not a valid ZIP file.");
    }

    private static void ReadExact(Stream stream, byte[] buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
            if (read == 0)
                throw new EndOfStreamException();
            totalRead += read;
        }
    }

    private static byte[] ReadBytes(Stream stream, int count)
    {
        byte[] buffer = new byte[count];
        ReadExact(stream, buffer);
        return buffer;
    }

    private static ushort ReadUInt16(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExactSpan(stream, buffer);
        return BitConverter.ToUInt16(buffer);
    }

    private static uint ReadUInt32(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExactSpan(stream, buffer);
        return BitConverter.ToUInt32(buffer);
    }

    private static ulong ReadUInt64(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExactSpan(stream, buffer);
        return BitConverter.ToUInt64(buffer);
    }

    private static void ReadExactSpan(Stream stream, Span<byte> buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = stream.Read(buffer[totalRead..]);
            if (read == 0)
                throw new EndOfStreamException();
            totalRead += read;
        }
    }
}
