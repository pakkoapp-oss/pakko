using System.Buffers.Binary;
using Archiver.Core.IO;

namespace Archiver.Core.Tests.Helpers;

/// <summary>
/// T-F234: writes a stored (method 0) ZIP whose entry names are given as raw bytes, with an explicit
/// general-purpose flag, "version made by" host OS and central-directory extra block — the shapes
/// real legacy writers produce (Windows "Compressed folders", 1C, scanners) that no .NET or 7za
/// writer can be told to emit. Deliberately knows nothing about name decoding: callers state the
/// raw bytes and assert the expected Unicode name from explicit code-page facts.
/// </summary>
internal static class LegacyZipBuilder
{
    public const byte HostFat = 0;
    public const byte HostUnix = 3;
    public const byte HostNtfs = 11;
    public const byte HostMacOs = 19;

    internal sealed record Entry(byte[] RawName, byte[] Content, ushort Flags = 0, byte HostOs = HostFat, byte[]? CentralExtra = null,
        byte[]? LocalRawName = null);

    public static string Write(string path, params Entry[] entries)
    {
        using var output = new MemoryStream();
        var localOffsets = new List<long>();

        foreach (var entry in entries)
        {
            localOffsets.Add(output.Position);
            uint crc = Crc32.Compute(new MemoryStream(entry.Content));
            WriteUInt32(output, 0x04034b50);
            WriteUInt16(output, 20);
            WriteUInt16(output, entry.Flags);
            WriteUInt16(output, 0);
            WriteUInt16(output, 0);
            WriteUInt16(output, 0x21);
            WriteUInt32(output, crc);
            WriteUInt32(output, (uint)entry.Content.Length);
            WriteUInt32(output, (uint)entry.Content.Length);
            byte[] localName = entry.LocalRawName ?? entry.RawName;
            WriteUInt16(output, (ushort)localName.Length);
            WriteUInt16(output, 0);
            output.Write(localName);
            output.Write(entry.Content);
        }

        long centralStart = output.Position;
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            byte[] extra = entry.CentralExtra ?? [];
            uint crc = Crc32.Compute(new MemoryStream(entry.Content));
            WriteUInt32(output, 0x02014b50);
            WriteUInt16(output, (ushort)((entry.HostOs << 8) | 20));
            WriteUInt16(output, 20);
            WriteUInt16(output, entry.Flags);
            WriteUInt16(output, 0);
            WriteUInt16(output, 0);
            WriteUInt16(output, 0x21);
            WriteUInt32(output, crc);
            WriteUInt32(output, (uint)entry.Content.Length);
            WriteUInt32(output, (uint)entry.Content.Length);
            WriteUInt16(output, (ushort)entry.RawName.Length);
            WriteUInt16(output, (ushort)extra.Length);
            WriteUInt16(output, 0);
            WriteUInt16(output, 0);
            WriteUInt16(output, 0);
            WriteUInt32(output, 0);
            WriteUInt32(output, (uint)localOffsets[i]);
            output.Write(entry.RawName);
            output.Write(extra);
        }

        long centralSize = output.Position - centralStart;
        WriteUInt32(output, 0x06054b50);
        WriteUInt16(output, 0);
        WriteUInt16(output, 0);
        WriteUInt16(output, (ushort)entries.Length);
        WriteUInt16(output, (ushort)entries.Length);
        WriteUInt32(output, (uint)centralSize);
        WriteUInt32(output, (uint)centralStart);
        WriteUInt16(output, 0);

        File.WriteAllBytes(path, output.ToArray());
        return path;
    }

    /// <summary>An Info-ZIP Unicode Path (0x7075) extra record: version, CRC-32 of the raw
    /// header name, then the UTF-8 name.</summary>
    public static byte[] UnicodePathExtra(byte[] rawHeaderName, byte[] utf8Name, byte version = 1, uint? crcOverride = null)
    {
        uint crc = crcOverride ?? Crc32.Compute(new MemoryStream(rawHeaderName));
        var record = new byte[4 + 5 + utf8Name.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(record, 0x7075);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(2), (ushort)(5 + utf8Name.Length));
        record[4] = version;
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(5), crc);
        utf8Name.CopyTo(record, 9);
        return record;
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }
}
