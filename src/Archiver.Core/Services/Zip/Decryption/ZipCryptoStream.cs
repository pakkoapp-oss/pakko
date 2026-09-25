namespace Archiver.Core.Services.Zip.Decryption;

/// <summary>
/// Traditional PKWARE encryption ("ZipCrypto") — read-only, decryption only: Pakko never writes it
/// (it is cryptographically broken; T-F193 writes AES only). T-F193: a streaming decryptor — the
/// 12-byte encryption header is checked up front, then the rest is decrypted as the caller reads.
/// ZipCrypto has no authentication; its one-byte password check accepts ~1 in 256 wrong passwords,
/// so callers rely on the content CRC-32 (TrailerCrcCheckStream) to reject that garbage.
/// </summary>
internal sealed class ZipCryptoStream : Stream
{
    public const int EncryptionHeaderLength = 12;
    private static readonly uint[] Table = BuildTable();

    private readonly Stream _ciphertext;
    private uint _key0;
    private uint _key1;
    private uint _key2;

    private ZipCryptoStream(Stream ciphertext, uint key0, uint key1, uint key2)
    {
        _ciphertext = ciphertext;
        _key0 = key0;
        _key1 = key1;
        _key2 = key2;
    }

    /// <summary>
    /// Reads and decrypts the 12-byte header from <paramref name="cipherWithHeader"/> and checks its
    /// last byte. On success returns a stream decrypting the remaining bytes as they are read; on a
    /// wrong password returns false (the caller still owns <paramref name="cipherWithHeader"/>).
    /// </summary>
    public static bool TryCreate(Stream cipherWithHeader, byte[] password, byte expectedCheckByte, out Stream? plaintext)
    {
        Span<byte> header = stackalloc byte[EncryptionHeaderLength];
        cipherWithHeader.ReadExactly(header);

        InitializeKeys(password, out uint key0, out uint key1, out uint key2);
        for (int i = 0; i < EncryptionHeaderLength; i++)
            header[i] = DecryptByte(header[i], ref key0, ref key1, ref key2);

        if (header[EncryptionHeaderLength - 1] != expectedCheckByte)
        {
            plaintext = null;
            return false;
        }

        plaintext = new ZipCryptoStream(cipherWithHeader, key0, key1, key2);
        return true;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = _ciphertext.Read(buffer, offset, count);
        for (int i = 0; i < read; i++)
            buffer[offset + i] = DecryptByte(buffer[offset + i], ref _key0, ref _key1, ref _key2);
        return read;
    }

    private static void InitializeKeys(byte[] password, out uint key0, out uint key1, out uint key2)
    {
        key0 = 0x12345678;
        key1 = 0x23456789;
        key2 = 0x34567890;
        foreach (byte b in password)
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

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override void Flush() { /* read-only */ }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _ciphertext.Dispose();
        // T-F244 item 3: the three keys ARE the password-derived state.
        _key0 = _key1 = _key2 = 0;
        base.Dispose(disposing);
    }
}
