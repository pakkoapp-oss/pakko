using System.Security.Cryptography;
using System.Text;

namespace Archiver.Core.Services.Zip.Decryption;

/// <summary>
/// WinZip AE-1/AE-2 primitives — all real cryptography (PBKDF2 key derivation, AES, HMAC-SHA1)
/// comes from <c>System.Security.Cryptography</c> (BCL, audited); this class only implements the
/// WinZip AE framing around those primitives (salt/verification-value layout, the trailing 10-byte
/// authentication tag) — a transcription of the published WinZip AE specification, not a new
/// cipher. T-F193: streaming — the caller authenticates the whole ciphertext first
/// (<see cref="Authenticate"/>, pass 1, no plaintext produced) and only then decrypts it as it
/// reads (<see cref="WinZipAesCtrStream"/>, pass 2), so neither pass holds the entry in memory.
/// </summary>
internal static class WinZipAesReader
{
    public const int PasswordVerificationLength = 2;
    public const int AuthenticationCodeLength = 10;
    private const int Pbkdf2Iterations = 1000;
    private const int HashChunkSize = 81920;

    public static int SaltLength(int strengthBits) => strengthBits switch
    {
        128 => 8,
        192 => 12,
        256 => 16,
        _ => throw new InvalidDataException($"Unsupported WinZip AES strength: {strengthBits} bits.")
    };

    /// <summary>Derives the AES key, HMAC key, and 2-byte password-verification value.</summary>
    public static (byte[] EncryptionKey, byte[] AuthenticationKey, byte[] PasswordVerify) DeriveKeys(
        string password, byte[] salt, int strengthBits)
    {
        int keyLength = strengthBits / 8;
        // CA5379/CA5350 + S5344/S4790 (both here and in Authenticate): the WinZip AE specification
        // hardcodes PBKDF2-HMAC-SHA1 (1000 iterations) and HMAC-SHA1 — real-world ZIP-password
        // compatibility, not an algorithm choice this code is free to make. Other parameters
        // derive different keys and cannot read a real WinZip-AES archive. See
        // docs/CONVENTIONS.md's Static-Analysis Won't-Fix Conventions.
#pragma warning disable CA5379
        using var pbkdf2 = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(password), salt, Pbkdf2Iterations, HashAlgorithmName.SHA1); // NOSONAR: S5344 — 1000 iterations are fixed by the WinZip AE spec (see CONVENTIONS.md)
#pragma warning restore CA5379
        byte[] derived = pbkdf2.GetBytes(keyLength * 2 + PasswordVerificationLength);
        return (derived[..keyLength], derived[keyLength..(keyLength * 2)], derived[(keyLength * 2)..]);
    }

    /// <summary>
    /// Pass 1: HMAC-SHA1 over the whole ciphertext, streamed in fixed-size chunks, compared in
    /// constant time with the 10-byte stored tag. Produces no plaintext.
    /// </summary>
    public static bool Authenticate(Stream ciphertext, byte[] authenticationKey, ReadOnlySpan<byte> storedTag)
    {
#pragma warning disable CA5350
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, authenticationKey); // NOSONAR: S4790 — HMAC-SHA1 is fixed by the WinZip AE spec (see CONVENTIONS.md)
#pragma warning restore CA5350
        byte[] buffer = new byte[HashChunkSize];
        int read;
        while ((read = ciphertext.Read(buffer, 0, buffer.Length)) > 0)
            hmac.AppendData(buffer, 0, read);

        byte[] tag = hmac.GetHashAndReset();
        return CryptographicOperations.FixedTimeEquals(tag.AsSpan(0, AuthenticationCodeLength), storedTag);
    }
}

/// <summary>
/// Pass 2: decrypts as the caller reads, XOR-ing with <see cref="AesCtrKeystream"/>.
/// </summary>
internal sealed class WinZipAesCtrStream : Stream
{
    private readonly Stream _ciphertext;
    private readonly AesCtrKeystream _keystream;

    public WinZipAesCtrStream(Stream ciphertext, byte[] key)
    {
        _ciphertext = ciphertext;
        _keystream = new AesCtrKeystream(key);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = _ciphertext.Read(buffer, offset, count);
        var chunk = buffer.AsSpan(offset, read);
        _keystream.Apply(chunk, chunk);
        return read;
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
        {
            _keystream.Dispose();
            _ciphertext.Dispose();
        }
        base.Dispose(disposing);
    }
}
