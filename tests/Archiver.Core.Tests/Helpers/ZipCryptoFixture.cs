using System.Buffers.Binary;
using Archiver.Core.IO;

namespace Archiver.Core.Tests.Helpers;

/// <summary>
/// T-F243/T-F244: writes a one-entry stored ZIP encrypted with traditional PKWARE ZipCrypto, from
/// APPNOTE 6.1 directly — independent of Pakko's own ZipCryptoStream. Covers what 7za cannot
/// produce: a data-descriptor entry (bit 3, where the password check byte is the high byte of the
/// file time, not of the CRC-32) and a password given as raw bytes (e.g. ANSI Cyrillic).
/// </summary>
internal static class ZipCryptoFixture
{
    public const ushort DosTime = 0xABCD;
    private const ushort DosDate = 0x5B21;

    public static string Write(string path, string entryName, byte[] content, byte[] passwordBytes, bool dataDescriptor, int headerSeed = 7)
    {
        uint crc = Crc32.Compute(new MemoryStream(content));
        byte checkByte = dataDescriptor ? (byte)(DosTime >> 8) : (byte)(crc >> 24);

        var header = new byte[12];
        new Random(headerSeed).NextBytes(header);
        header[11] = checkByte;
        byte[] plain = [.. header, .. content];

        var keys = new Keys(passwordBytes);
        byte[] cipher = new byte[plain.Length];
        for (int i = 0; i < plain.Length; i++)
            cipher[i] = keys.Encrypt(plain[i]);

        byte[] name = System.Text.Encoding.UTF8.GetBytes(entryName);
        ushort flags = (ushort)(0x0001 | (dataDescriptor ? 0x0008 : 0) | 0x0800);
        using var output = new MemoryStream();

        Write32(output, 0x04034b50);
        Write16(output, 20);
        Write16(output, flags);
        Write16(output, 0);
        Write16(output, DosTime);
        Write16(output, DosDate);
        Write32(output, dataDescriptor ? 0 : crc);
        Write32(output, dataDescriptor ? 0 : (uint)cipher.Length);
        Write32(output, dataDescriptor ? 0 : (uint)content.Length);
        Write16(output, (ushort)name.Length);
        Write16(output, 0);
        output.Write(name);
        output.Write(cipher);
        if (dataDescriptor)
        {
            Write32(output, 0x08074b50);
            Write32(output, crc);
            Write32(output, (uint)cipher.Length);
            Write32(output, (uint)content.Length);
        }

        long centralStart = output.Position;
        Write32(output, 0x02014b50);
        Write16(output, 20);
        Write16(output, 20);
        Write16(output, flags);
        Write16(output, 0);
        Write16(output, DosTime);
        Write16(output, DosDate);
        Write32(output, crc);
        Write32(output, (uint)cipher.Length);
        Write32(output, (uint)content.Length);
        Write16(output, (ushort)name.Length);
        Write16(output, 0);
        Write16(output, 0);
        Write16(output, 0);
        Write16(output, 0);
        Write32(output, 0);
        Write32(output, 0);
        output.Write(name);
        long centralSize = output.Position - centralStart;

        Write32(output, 0x06054b50);
        Write16(output, 0);
        Write16(output, 0);
        Write16(output, 1);
        Write16(output, 1);
        Write32(output, (uint)centralSize);
        Write32(output, (uint)centralStart);
        Write16(output, 0);

        File.WriteAllBytes(path, output.ToArray());
        return path;
    }

    private sealed class Keys
    {
        private uint _k0 = 0x12345678, _k1 = 0x23456789, _k2 = 0x34567890;

        public Keys(byte[] password)
        {
            foreach (byte b in password)
                Update(b);
        }

        public byte Encrypt(byte plain)
        {
            ushort temp = (ushort)(_k2 | 2);
            byte cipher = (byte)(plain ^ (byte)((temp * (temp ^ 1)) >> 8));
            Update(plain);
            return cipher;
        }

        private void Update(byte b)
        {
            _k0 = Crc32Byte(_k0, b);
            _k1 = (_k1 + (_k0 & 0xFF)) * 134775813 + 1;
            _k2 = Crc32Byte(_k2, (byte)(_k1 >> 24));
        }

        private static uint Crc32Byte(uint crc, byte b)
        {
            uint c = (crc ^ b) & 0xFF;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            return c ^ (crc >> 8);
        }
    }

    private static void Write16(Stream s, ushort v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, v);
        s.Write(b);
    }

    private static void Write32(Stream s, uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        s.Write(b);
    }
}
