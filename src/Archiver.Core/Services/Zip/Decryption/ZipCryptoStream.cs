using System.Text;

namespace Archiver.Core.Services.Zip.Decryption;

/// <summary>
/// Legacy PKWARE "traditional" ZIP encryption (APPNOTE.TXT section 6.1) — a direct transcription
/// of the published algorithm, not a hand-rolled cipher: 3 running 32-bit keys updated one byte
/// at a time via the same CRC-32 polynomial ZIP itself uses. Cryptographically weak (a known-
/// plaintext attack breaks it in seconds) — kept only because it is still what many real-world
/// ZIP files use; never used for writing (see <c>docs/SPEC.md</c>).
/// <para>
/// Uses its own small 256-entry CRC-32 table rather than <see cref="Archiver.Core.IO.Crc32"/> —
/// that type's <c>Accumulator</c> always starts from the fixed initial register 0xFFFFFFFF and
/// finishes with a fixed final XOR, matching the "hash a whole file" use case it exists for. This
/// algorithm instead runs the same table one byte at a time from an arbitrary, continuously-
/// evolving register with no final XOR — a different protocol role for the same table, not the
/// same operation, so reusing that type would mean bolting on an escape hatch rather than sharing
/// real behavior.
/// </para>
/// </summary>
internal static class ZipCryptoStream
{
    private const int EncryptionHeaderLength = 12;
    private static readonly uint[] Table = BuildTable();

    /// <summary>
    /// Decrypts <paramref name="cipherWithHeader"/> (the 12-byte encryption header followed by the
    /// encrypted compressed data, exactly as stored in the ZIP entry). Returns false — with no
    /// exception — when the trailing byte of the decrypted header doesn't match
    /// <paramref name="expectedCheckByte"/>, which is the standard fast, non-cryptographic
    /// "probably wrong password" signal this format defines (a real CRC/decompression check is
    /// still needed afterward for certainty).
    /// </summary>
    public static bool TryDecrypt(
        ReadOnlySpan<byte> cipherWithHeader, string password, byte expectedCheckByte, out byte[] decrypted)
    {
        if (cipherWithHeader.Length < EncryptionHeaderLength)
            throw new InvalidDataException("ZipCrypto entry data is shorter than the 12-byte encryption header.");

        InitializeKeys(password, out uint key0, out uint key1, out uint key2);

        Span<byte> header = stackalloc byte[EncryptionHeaderLength];
        for (int i = 0; i < EncryptionHeaderLength; i++)
            header[i] = DecryptByte(cipherWithHeader[i], ref key0, ref key1, ref key2);

        if (header[EncryptionHeaderLength - 1] != expectedCheckByte)
        {
            decrypted = [];
            return false;
        }

        decrypted = new byte[cipherWithHeader.Length - EncryptionHeaderLength];
        for (int i = 0; i < decrypted.Length; i++)
            decrypted[i] = DecryptByte(cipherWithHeader[EncryptionHeaderLength + i], ref key0, ref key1, ref key2);

        return true;
    }

    private static void InitializeKeys(string password, out uint key0, out uint key1, out uint key2)
    {
        key0 = 0x12345678;
        key1 = 0x23456789;
        key2 = 0x34567890;
        foreach (byte b in Encoding.UTF8.GetBytes(password))
            UpdateKeys(b, ref key0, ref key1, ref key2);
    }

    private static void UpdateKeys(byte plainByte, ref uint key0, ref uint key1, ref uint key2)
    {
        key0 = Crc32Step(key0, plainByte);
        key1 = unchecked((key1 + (key0 & 0xFF)) * 134775813 + 1);
        key2 = Crc32Step(key2, (byte)(key1 >> 24));
    }

    private static byte DecryptByte(byte cipherByte, ref uint key0, ref uint key1, ref uint key2)
    {
        ushort temp = (ushort)((key2 | 2) & 0xFFFF);
        byte keystreamByte = (byte)((temp * (temp ^ 1)) >> 8);
        byte plainByte = (byte)(cipherByte ^ keystreamByte);
        UpdateKeys(plainByte, ref key0, ref key1, ref key2);
        return plainByte;
    }

    private static uint Crc32Step(uint crc, byte b) => (crc >> 8) ^ Table[(crc ^ b) & 0xFF];

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }
}
