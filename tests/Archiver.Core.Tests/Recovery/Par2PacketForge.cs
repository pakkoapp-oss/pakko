using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Archiver.Core.Tests.Recovery;

/// <summary>
/// Builds PAR2 packets with any field values and a correct packet MD5 (T-F275), so hostile input
/// gets past the MD5 check and reaches the field validation — the way par2cmdline's own
/// block-count-wrap fixture is made. Computes MD5 itself rather than through the code under test.
/// </summary>
internal static class Par2PacketForge
{
    public static readonly byte[] Magic = "PAR2\0PKT"u8.ToArray();
    public static readonly byte[] MainType = "PAR 2.0\0Main\0\0\0\0"u8.ToArray();
    public static readonly byte[] FileDescType = "PAR 2.0\0FileDesc"u8.ToArray();
    public static readonly byte[] IfscType = "PAR 2.0\0IFSC\0\0\0\0"u8.ToArray();
    public static readonly byte[] RecoveryType = "PAR 2.0\0RecvSlic"u8.ToArray();

#pragma warning disable CA5351 // PAR 2.0 mandates MD5
    public static byte[] Md5(ReadOnlySpan<byte> data) => MD5.HashData(data);
#pragma warning restore CA5351

    public static byte[] Packet(byte[] setId, byte[] type, byte[] body)
    {
        var packet = new byte[64 + body.Length];
        Magic.CopyTo(packet, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(8), (ulong)packet.Length);
        setId.CopyTo(packet, 32);
        type.CopyTo(packet, 48);
        body.CopyTo(packet, 64);
        Md5(packet.AsSpan(32)).CopyTo(packet, 16);
        return packet;
    }

    public static byte[] MainBody(long sliceSize, params byte[][] fileIds)
    {
        var body = new byte[12 + 16 * fileIds.Length];
        BinaryPrimitives.WriteInt64LittleEndian(body, sliceSize);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), (uint)fileIds.Length);
        for (int i = 0; i < fileIds.Length; i++)
            fileIds[i].CopyTo(body, 12 + 16 * i);
        return body;
    }

    public static byte[] FileId(byte[] md5First16k, ulong length, string name)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[24 + nameBytes.Length];
        md5First16k.CopyTo(input, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(input.AsSpan(16), length);
        nameBytes.CopyTo(input, 24);
        return Md5(input);
    }

    public static byte[] FileDescBody(byte[] fileId, byte[] fileMd5, byte[] md5First16k, ulong length, string name)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        var body = new byte[56 + ((nameBytes.Length + 3) & ~3)];
        fileId.CopyTo(body, 0);
        fileMd5.CopyTo(body, 16);
        md5First16k.CopyTo(body, 32);
        BinaryPrimitives.WriteUInt64LittleEndian(body.AsSpan(48), length);
        nameBytes.CopyTo(body, 56);
        return body;
    }

    public static byte[] IfscBody(byte[] fileId, int entries)
    {
        var body = new byte[16 + 20 * entries];
        fileId.CopyTo(body, 0);
        return body;
    }

    public static byte[] RecoveryBody(uint exponent, int sliceSize, byte fill = 0)
    {
        var body = new byte[4 + sliceSize];
        BinaryPrimitives.WriteUInt32LittleEndian(body, exponent);
        body.AsSpan(4).Fill(fill);
        return body;
    }

    /// <summary>A consistent one-file set: Main, FileDesc, IFSC, and <paramref name="recovery"/>
    /// recovery packets — the checksums are zeros, which the reader does not judge.</summary>
    public static (byte[] SetId, byte[] FileId, List<byte[]> Packets) OneFileSet(long sliceSize, ulong fileLength, string name, int recovery = 0)
    {
        byte[] md5First16k = new byte[16];
        byte[] fileId = FileId(md5First16k, fileLength, name);
        byte[] main = MainBody(sliceSize, fileId);
        byte[] setId = Md5(main);
        ulong entries = fileLength / (ulong)sliceSize + (fileLength % (ulong)sliceSize == 0 ? 0UL : 1UL);
        var packets = new List<byte[]>
        {
            Packet(setId, MainType, main),
            Packet(setId, FileDescType, FileDescBody(fileId, new byte[16], md5First16k, fileLength, name)),
            Packet(setId, IfscType, IfscBody(fileId, (int)Math.Min(entries, 40000))),
        };
        for (uint e = 0; e < recovery; e++)
            packets.Add(Packet(setId, RecoveryType, RecoveryBody(e, (int)sliceSize, (byte)e)));
        return (setId, fileId, packets);
    }

    public static string Write(string folder, string name, IEnumerable<byte[]> packets)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllBytes(path, packets.SelectMany(p => p).ToArray());
        return path;
    }
}
