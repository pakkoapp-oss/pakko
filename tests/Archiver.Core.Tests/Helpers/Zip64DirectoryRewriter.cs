using System.Buffers.Binary;

namespace Archiver.Core.Tests.Helpers;

/// <summary>
/// T-F193 Phase 0: rewrites a small, ordinary ZIP so its directory takes the full Zip64 shape a
/// &gt;4 GiB or &gt;65,534-entry archive has — every central record's compressed/uncompressed sizes
/// and local-header offset set to the 0xFFFFFFFF sentinel with the real values in a Zip64 (0x0001)
/// extra field, plus a Zip64 end-of-central-directory record and locator, and a classic EOCD
/// carrying 0xFFFF/0xFFFFFFFF sentinels. Same layout ZipEntryWriter emits for big archives, without
/// needing gigabytes of test data. Entry data and local headers are copied byte for byte.
/// </summary>
internal static class Zip64DirectoryRewriter
{
    public static void Rewrite(string sourceZip, string destZip)
    {
        byte[] src = File.ReadAllBytes(sourceZip);
        int eocd = FindEocd(src);
        int entryCount = BinaryPrimitives.ReadUInt16LittleEndian(src.AsSpan(eocd + 10));
        int cdOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(src.AsSpan(eocd + 16));

        using var output = new MemoryStream();
        output.Write(src, 0, cdOffset); // local headers + data, unchanged

        long newCdOffset = output.Position;
        int pos = cdOffset;
        for (int i = 0; i < entryCount; i++)
        {
            ReadOnlySpan<byte> rec = src.AsSpan(pos);
            uint compressed = BinaryPrimitives.ReadUInt32LittleEndian(rec[20..]);
            uint uncompressed = BinaryPrimitives.ReadUInt32LittleEndian(rec[24..]);
            int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(rec[28..]);
            int extraLen = BinaryPrimitives.ReadUInt16LittleEndian(rec[30..]);
            int commentLen = BinaryPrimitives.ReadUInt16LittleEndian(rec[32..]);
            uint localOffset = BinaryPrimitives.ReadUInt32LittleEndian(rec[42..]);

            byte[] zip64Extra = new byte[4 + 24];
            BinaryPrimitives.WriteUInt16LittleEndian(zip64Extra, 0x0001);
            BinaryPrimitives.WriteUInt16LittleEndian(zip64Extra.AsSpan(2), 24);
            BinaryPrimitives.WriteUInt64LittleEndian(zip64Extra.AsSpan(4), uncompressed);
            BinaryPrimitives.WriteUInt64LittleEndian(zip64Extra.AsSpan(12), compressed);
            BinaryPrimitives.WriteUInt64LittleEndian(zip64Extra.AsSpan(20), localOffset);

            byte[] fixedPart = rec[..46].ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(fixedPart.AsSpan(6), 45); // version needed: Zip64
            BinaryPrimitives.WriteUInt32LittleEndian(fixedPart.AsSpan(20), 0xFFFFFFFF);
            BinaryPrimitives.WriteUInt32LittleEndian(fixedPart.AsSpan(24), 0xFFFFFFFF);
            BinaryPrimitives.WriteUInt16LittleEndian(fixedPart.AsSpan(30), (ushort)(extraLen + zip64Extra.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(fixedPart.AsSpan(42), 0xFFFFFFFF);

            output.Write(fixedPart);
            output.Write(rec.Slice(46, nameLen));
            output.Write(zip64Extra);
            output.Write(rec.Slice(46 + nameLen, extraLen));
            output.Write(rec.Slice(46 + nameLen + extraLen, commentLen));
            pos += 46 + nameLen + extraLen + commentLen;
        }
        long newCdSize = output.Position - newCdOffset;

        long zip64EocdOffset = output.Position;
        var tail = new byte[56 + 20 + 22];
        Span<byte> t = tail;
        BinaryPrimitives.WriteUInt32LittleEndian(t, 0x06064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(t[4..], 44);
        BinaryPrimitives.WriteUInt16LittleEndian(t[12..], 45);
        BinaryPrimitives.WriteUInt16LittleEndian(t[14..], 45);
        BinaryPrimitives.WriteUInt64LittleEndian(t[24..], (ulong)entryCount);
        BinaryPrimitives.WriteUInt64LittleEndian(t[32..], (ulong)entryCount);
        BinaryPrimitives.WriteUInt64LittleEndian(t[40..], (ulong)newCdSize);
        BinaryPrimitives.WriteUInt64LittleEndian(t[48..], (ulong)newCdOffset);

        Span<byte> locator = t[56..];
        BinaryPrimitives.WriteUInt32LittleEndian(locator, 0x07064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(locator[8..], (ulong)zip64EocdOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(locator[16..], 1);

        Span<byte> classic = t[76..];
        BinaryPrimitives.WriteUInt32LittleEndian(classic, 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(classic[8..], 0xFFFF);
        BinaryPrimitives.WriteUInt16LittleEndian(classic[10..], 0xFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(classic[12..], 0xFFFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(classic[16..], 0xFFFFFFFF);
        output.Write(tail);

        File.WriteAllBytes(destZip, output.ToArray());
    }

    private static int FindEocd(byte[] bytes)
    {
        for (int i = bytes.Length - 22; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i)) == 0x06054b50)
                return i;
        }
        throw new InvalidDataException("No EOCD in source ZIP.");
    }
}
