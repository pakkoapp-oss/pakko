using System.Buffers.Binary;
using System.Text;

namespace Archiver.Core.Recovery;

/// <summary>
/// The PAR 2.0 packet layout (T-F275): a 64-byte header — magic, length, MD5 of everything from
/// the set ID on, set ID, type — then a body whose length is a multiple of 4. Integers are
/// little-endian. Builds the packets Pakko writes.
/// </summary>
internal static class Par2Packets
{
    internal const int HeaderLength = 64;
    internal const int LengthOffset = 8;
    internal const int HashOffset = 16;
    internal const int SetIdOffset = 32;
    internal const int TypeOffset = 48;

    /// <summary>The RecvSlic body before the slice data: the exponent.</summary>
    internal const int RecoveryPrefixLength = 4;

    internal const int First16kLength = 16 * 1024;

    internal static ReadOnlySpan<byte> Magic => "PAR2\0PKT"u8;
    internal static ReadOnlySpan<byte> MainType => "PAR 2.0\0Main\0\0\0\0"u8;
    internal static ReadOnlySpan<byte> FileDescType => "PAR 2.0\0FileDesc"u8;
    internal static ReadOnlySpan<byte> IfscType => "PAR 2.0\0IFSC\0\0\0\0"u8;
    internal static ReadOnlySpan<byte> RecoveryType => "PAR 2.0\0RecvSlic"u8;
    internal static ReadOnlySpan<byte> CreatorType => "PAR 2.0\0Creator\0"u8;

    internal const string CreatorName = "Pakko (https://github.com/pakkoapp-oss/pakko)";

    /// <summary>Slice size, one file in the recovery set, its file ID; the set ID is its MD5.</summary>
    internal static byte[] MainBody(long sliceSize, ReadOnlySpan<byte> fileId)
    {
        var body = new byte[12 + Par2Md5.Size];
        BinaryPrimitives.WriteInt64LittleEndian(body, sliceSize);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), 1);
        fileId.CopyTo(body.AsSpan(12));
        return body;
    }

    /// <summary>MD5(MD5 of the first 16 KiB ‖ length ‖ name), the name unpadded.</summary>
    internal static byte[] FileId(ReadOnlySpan<byte> md5First16k, long length, ReadOnlySpan<byte> name)
    {
        var input = new byte[Par2Md5.Size + 8 + name.Length];
        md5First16k.CopyTo(input);
        BinaryPrimitives.WriteInt64LittleEndian(input.AsSpan(Par2Md5.Size), length);
        name.CopyTo(input.AsSpan(Par2Md5.Size + 8));
        return Par2Md5.Hash(input);
    }

    internal static byte[] FileDescBody(ReadOnlySpan<byte> fileId, ReadOnlySpan<byte> fileMd5, ReadOnlySpan<byte> md5First16k, long length, ReadOnlySpan<byte> name)
    {
        var body = new byte[3 * Par2Md5.Size + 8 + PaddedLength(name.Length)];
        fileId.CopyTo(body);
        fileMd5.CopyTo(body.AsSpan(Par2Md5.Size));
        md5First16k.CopyTo(body.AsSpan(2 * Par2Md5.Size));
        BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(3 * Par2Md5.Size), length);
        name.CopyTo(body.AsSpan(3 * Par2Md5.Size + 8));
        return body;
    }

    internal static byte[] IfscBody(ReadOnlySpan<byte> fileId, IReadOnlyList<Par2SliceChecksum> slices)
    {
        var body = new byte[Par2Md5.Size + slices.Count * Par2SliceChecksum.Length];
        fileId.CopyTo(body);
        for (int i = 0; i < slices.Count; i++)
            slices[i].WriteTo(body.AsSpan(Par2Md5.Size + i * Par2SliceChecksum.Length));
        return body;
    }

    internal static byte[] CreatorBody() => Pad(Encoding.ASCII.GetBytes(CreatorName));

    /// <summary>A whole packet around <paramref name="body"/>.</summary>
    internal static byte[] Build(ReadOnlySpan<byte> setId, ReadOnlySpan<byte> type, ReadOnlySpan<byte> body)
    {
        var packet = new byte[HeaderLength + body.Length];
        WriteHeader(packet, setId, type, body.Length);
        body.CopyTo(packet.AsSpan(HeaderLength));
        Par2Md5.Hash(packet.AsSpan(SetIdOffset)).CopyTo(packet.AsSpan(HashOffset));
        return packet;
    }

    /// <summary>The header of a packet whose MD5 is filled in later, once its body is written.</summary>
    internal static void WriteHeader(Span<byte> header, ReadOnlySpan<byte> setId, ReadOnlySpan<byte> type, long bodyLength)
    {
        Magic.CopyTo(header);
        BinaryPrimitives.WriteInt64LittleEndian(header[LengthOffset..], HeaderLength + bodyLength);
        header.Slice(HashOffset, Par2Md5.Size).Clear();
        setId.CopyTo(header[SetIdOffset..]);
        type.CopyTo(header[TypeOffset..]);
    }

    internal static int PaddedLength(int length) => (length + 3) & ~3;

    private static byte[] Pad(byte[] bytes)
    {
        var padded = new byte[PaddedLength(bytes.Length)];
        bytes.CopyTo(padded, 0);
        return padded;
    }
}

/// <summary>One IFSC entry: the MD5 and CRC-32 of a slice, zero-padded to the slice size.</summary>
internal readonly record struct Par2SliceChecksum(UInt128 Md5, uint Crc32)
{
    internal const int Length = Par2Md5.Size + 4;

    internal static Par2SliceChecksum Read(ReadOnlySpan<byte> entry) =>
        new(BinaryPrimitives.ReadUInt128LittleEndian(entry), BinaryPrimitives.ReadUInt32LittleEndian(entry[Par2Md5.Size..]));

    internal void WriteTo(Span<byte> entry)
    {
        BinaryPrimitives.WriteUInt128LittleEndian(entry, Md5);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[Par2Md5.Size..], Crc32);
    }
}
