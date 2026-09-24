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
    public required ushort CompressionMethod { get; init; }
    public required bool GeneralPurposeEncryptedBit { get; init; }
    public required uint StoredCrc32 { get; init; }

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
    private const ushort WinZipAesExtraId = 0x9901;
    private const ushort WinZipAesCompressionMethod = 99;
    // version(2) + vendor "AE"(2) + strength(1) + real compression method(2)
    private const int WinZipAesExtraMinLength = 7;

    public static LocatedZipEntry Locate(Stream zipStream, string entryFullName)
    {
        byte[] targetBytes = Encoding.UTF8.GetBytes(entryFullName);

        var record = ReadCentralDirectory(zipStream).FirstOrDefault(r => r.NameBytes.AsSpan().SequenceEqual(targetBytes))
            ?? throw new FileNotFoundException($"Entry not found in ZIP central directory: {entryFullName}");
        return BuildFromLocalHeader(zipStream, record.LocalHeaderOffset, record.Crc32, record.CompressedSize);
    }

    /// <summary>
    /// Walks the whole central directory once and returns every entry's <see cref="LocatedZipEntry"/>
    /// in central-directory order — the same order <see cref="System.IO.Compression.ZipArchive.Entries"/>
    /// populates its own list in, so a caller can pair the two positionally by index. Deliberately
    /// NOT name-based: a legacy (non-UTF-8-flagged) entry name decodes differently in
    /// <see cref="System.IO.Compression.ZipArchiveEntry.FullName"/> than a naive
    /// <see cref="Encoding.UTF8"/> byte-compare here would assume, and <c>Archiver.Core</c>'s
    /// zero-NuGet-dependency constraint rules out pulling in
    /// <c>System.Text.Encoding.CodePages</c> to decode it correctly — see docs/DECISIONS.md's
    /// T-F189 entry. Positional pairing sidesteps the encoding question entirely.
    /// <para>
    /// Two full passes over <paramref name="zipStream"/> deliberately: <see cref="ReadCentralDirectory"/>
    /// reads only the central directory (one contiguous forward scan, no seeking away), THEN this
    /// method seeks out to each entry's local header one at a time — interleaving the two (seeking
    /// to a local header mid-central-directory-scan) would corrupt the central directory read
    /// position for every entry after the first.
    /// </para>
    /// </summary>
    public static List<LocatedZipEntry> LocateAll(Stream zipStream)
    {
        var records = ReadCentralDirectory(zipStream);
        var result = new List<LocatedZipEntry>(records.Count);
        foreach (var record in records)
            result.Add(BuildFromLocalHeader(zipStream, record.LocalHeaderOffset, record.Crc32, record.CompressedSize));
        return result;
    }

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

    private sealed record CentralDirectoryRecord(
        byte[] NameBytes, long LocalHeaderOffset, uint Crc32, uint CompressedSize, ushort GeneralPurposeFlag);

    private static List<CentralDirectoryRecord> ReadCentralDirectory(Stream zipStream)
    {
        var records = new List<CentralDirectoryRecord>();

        long eocdOffset = FindEndOfCentralDirectory(zipStream);
        zipStream.Seek(eocdOffset + 16, SeekOrigin.Begin); // offset of start of central directory
        uint centralDirOffset = ReadUInt32(zipStream);

        zipStream.Seek(centralDirOffset, SeekOrigin.Begin);
        while (zipStream.Position < eocdOffset)
        {
            uint signature = ReadUInt32(zipStream);
            if (signature != CentralDirectorySignature)
                throw new InvalidDataException("Malformed ZIP central directory (bad entry signature).");

            ReadUInt16(zipStream); // version made by
            ReadUInt16(zipStream); // version needed
            ushort generalPurposeFlag = ReadUInt16(zipStream);
            ReadUInt16(zipStream); // compression method — the local header's copy is the one used
            ReadUInt16(zipStream); // last mod time
            ReadUInt16(zipStream); // last mod date
            uint crc32 = ReadUInt32(zipStream);
            uint compressedSize = ReadUInt32(zipStream);
            ReadUInt32(zipStream); // uncompressed size
            ushort nameLength = ReadUInt16(zipStream);
            ushort extraLength = ReadUInt16(zipStream);
            ushort commentLength = ReadUInt16(zipStream);
            ReadUInt16(zipStream); // disk number start
            ReadUInt16(zipStream); // internal attributes
            ReadUInt32(zipStream); // external attributes
            uint localHeaderOffset = ReadUInt32(zipStream);
            byte[] nameBytes = ReadBytes(zipStream, nameLength);
            ReadBytes(zipStream, extraLength); // central directory's own extra copy — unused
            ReadBytes(zipStream, commentLength);

            records.Add(new CentralDirectoryRecord(nameBytes, localHeaderOffset, crc32, compressedSize, generalPurposeFlag));
        }

        return records;
    }

    private static LocatedZipEntry BuildFromLocalHeader(
        Stream zipStream, long localHeaderOffset, uint centralCrc32, uint centralCompressedSize)
    {
        zipStream.Seek(localHeaderOffset, SeekOrigin.Begin);
        uint signature = ReadUInt32(zipStream);
        if (signature != LocalFileHeaderSignature)
            throw new InvalidDataException("Malformed ZIP local file header (bad signature).");

        ReadUInt16(zipStream); // version needed
        ushort generalPurposeFlag = ReadUInt16(zipStream);
        ushort method = ReadUInt16(zipStream);
        ReadUInt16(zipStream); // last mod time
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
        return new LocatedZipEntry
        {
            CompressedDataOffset = compressedDataOffset,
            CompressedSize = localCompressedSize != 0 ? localCompressedSize : centralCompressedSize,
            CompressionMethod = method,
            GeneralPurposeEncryptedBit = (generalPurposeFlag & 0x0001) != 0,
            StoredCrc32 = localCrc32 != 0 ? localCrc32 : centralCrc32,
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
