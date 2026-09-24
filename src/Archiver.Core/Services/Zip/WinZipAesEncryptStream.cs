using System.Security.Cryptography;
using Archiver.Core.Services.Zip.Decryption;

namespace Archiver.Core.Services.Zip;

/// <summary>
/// T-F193: write side of WinZip AE-2, AES-256 — the inverse of <see cref="WinZipAesCtrStream"/>
/// plus <see cref="WinZipAesReader.Authenticate"/>. Writes the salt and password-verification
/// value on construction, encrypts every write with <see cref="AesCtrKeystream"/> and feeds the
/// resulting ciphertext (not the plaintext) into HMAC-SHA1, and appends the 10-byte
/// authentication code on dispose. Never closes the underlying stream: the caller reads the final
/// entry size from it afterward, so dispose order is compressor, then this stream, then the size.
/// <para>
/// Every instance draws a fresh random salt, so every entry gets its own key and keystream even
/// when two files have identical content — reusing a key would reuse the CTR keystream, which
/// breaks the encryption. The resulting archives are therefore deliberately not byte-for-byte
/// reproducible (an exception to T-F31/T-F32's determinism).
/// </para>
/// </summary>
internal sealed class WinZipAesEncryptStream : Stream
{
    public const int StrengthBits = 256;

    /// <summary>The WinZip AES extra field's strength code for <see cref="StrengthBits"/>.</summary>
    public const byte StrengthCode = 3;

    /// <summary>Bytes an entry grows by: salt + password-verification value + authentication code.</summary>
    public static readonly int Overhead =
        WinZipAesReader.SaltLength(StrengthBits) + WinZipAesReader.PasswordVerificationLength + WinZipAesReader.AuthenticationCodeLength;

    private const int ScratchSize = 65536;

    private readonly Stream _output;
    private readonly AesCtrKeystream _keystream;
    private readonly IncrementalHash _hmac;
    private readonly byte[] _scratch = new byte[ScratchSize];
    private bool _disposed;

    public WinZipAesEncryptStream(Stream output, string password)
    {
        _output = output;

        byte[] salt = new byte[WinZipAesReader.SaltLength(StrengthBits)];
        RandomNumberGenerator.Fill(salt);
        var (encryptionKey, authenticationKey, passwordVerify) = WinZipAesReader.DeriveKeys(password, salt, StrengthBits);

        _keystream = new AesCtrKeystream(encryptionKey);
        // CA5350 + S4790: HMAC-SHA1 is fixed by the WinZip AE spec — see WinZipAesReader.Authenticate.
#pragma warning disable CA5350
        _hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, authenticationKey); // NOSONAR: S4790 — fixed by the WinZip AE spec (see CONVENTIONS.md)
#pragma warning restore CA5350

        _output.Write(salt);
        _output.Write(passwordVerify);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        while (!buffer.IsEmpty)
        {
            int count = Math.Min(buffer.Length, _scratch.Length);
            var ciphertext = _scratch.AsSpan(0, count);
            _keystream.Apply(buffer[..count], ciphertext);
            _hmac.AppendData(ciphertext);
            _output.Write(ciphertext);
            buffer = buffer[count..];
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void WriteByte(byte value) => Write([value]);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            byte[] tag = _hmac.GetHashAndReset();
            _output.Write(tag, 0, WinZipAesReader.AuthenticationCodeLength);
            _hmac.Dispose();
            _keystream.Dispose();
        }
        base.Dispose(disposing);
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_disposed;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override void Flush() => _output.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
