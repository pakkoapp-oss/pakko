using System.Security.Cryptography;
using System.Text;

namespace Archiver.Core.Services.Zip.Decryption;

/// <summary>
/// WinZip AE-1/AE-2 decryption — all real cryptography (PBKDF2 key derivation, AES, HMAC-SHA1)
/// comes from <c>System.Security.Cryptography</c> (BCL, audited); this class only implements the
/// WinZip AE framing around those primitives (salt/verification-value layout, the AES-CTR
/// construction from a plain AES-ECB block encryptor, the trailing 10-byte authentication tag) —
/// a transcription of the published WinZip AE-1/AE-2 specification, not a new cipher.
/// </summary>
/// <summary>Outcome of <see cref="WinZipAesReader.TryDecrypt"/> — split into two failure shapes so
/// <see cref="EncryptedZipEntryReader"/> can distinguish a wrong password (the cheap 2-byte
/// verification value didn't match — password is likely wrong) from tampered/corrupted ciphertext
/// (the verification value matched, so the password is almost certainly right, but the HMAC over
/// the actual data didn't) rather than reporting both as the same generic failure.</summary>
internal enum WinZipAesDecryptOutcome
{
    Success,
    WrongPassword,
    AuthenticationFailed,
}

internal static class WinZipAesReader
{
    private const int Pbkdf2Iterations = 1000;
    private const int PasswordVerificationLength = 2;
    private const int AuthenticationCodeLength = 10;
    private const int AesBlockSize = 16;

    /// <summary>
    /// Decrypts and authenticates <paramref name="rawEntryData"/> (salt + password-verification
    /// value + AES-CTR ciphertext + 10-byte HMAC-SHA1 tag, exactly as stored in the ZIP entry).
    /// Returns false — with no exception — for either a wrong password (caught cheaply via the
    /// 2-byte verification value) or tampered/corrupted ciphertext (caught via the HMAC tag,
    /// checked before any plaintext is produced).
    /// </summary>
    public static WinZipAesDecryptOutcome TryDecrypt(
        ReadOnlySpan<byte> rawEntryData, string password, int strengthBits, out byte[] plaintext)
    {
        int saltLength = strengthBits switch
        {
            128 => 8,
            192 => 12,
            256 => 16,
            _ => throw new InvalidDataException($"Unsupported WinZip AES strength: {strengthBits} bits.")
        };
        int keyLength = strengthBits / 8;
        int minimumLength = saltLength + PasswordVerificationLength + AuthenticationCodeLength;

        if (rawEntryData.Length < minimumLength)
            throw new InvalidDataException("WinZip AES entry data is too short to contain salt/verification/auth fields.");

        byte[] salt = rawEntryData[..saltLength].ToArray();
        byte[] storedPasswordVerify = rawEntryData.Slice(saltLength, PasswordVerificationLength).ToArray();
        byte[] ciphertext = rawEntryData[(saltLength + PasswordVerificationLength)..^AuthenticationCodeLength].ToArray();
        byte[] storedAuthenticationCode = rawEntryData[^AuthenticationCodeLength..].ToArray();

        // CA5379/CA5350 (both this block and the HMACSHA1 use below): the WinZip AE-1/AE-2
        // specification hardcodes PBKDF2-HMAC-SHA1 (1000 iterations) and HMAC-SHA1 authentication
        // — real-world file format compatibility, not a choice this code makes. SHA-256/384/512
        // would derive a different key/tag and simply fail to read any real WinZip-AES-encrypted
        // ZIP. See docs/CONVENTIONS.md's Static-Analysis Won't-Fix Conventions.
#pragma warning disable CA5379, CA5350
        using var pbkdf2 = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(password), salt, Pbkdf2Iterations, HashAlgorithmName.SHA1);
        byte[] derived = pbkdf2.GetBytes(keyLength * 2 + PasswordVerificationLength);
        byte[] encryptionKey = derived[..keyLength];
        byte[] authenticationKey = derived[keyLength..(keyLength * 2)];
        byte[] derivedPasswordVerify = derived[(keyLength * 2)..];

        if (!derivedPasswordVerify.AsSpan().SequenceEqual(storedPasswordVerify))
        {
            plaintext = [];
            return WinZipAesDecryptOutcome.WrongPassword;
        }

        using var hmac = new HMACSHA1(authenticationKey);
#pragma warning restore CA5379, CA5350
        byte[] computedTag = hmac.ComputeHash(ciphertext);
        if (!computedTag.AsSpan(0, AuthenticationCodeLength).SequenceEqual(storedAuthenticationCode))
        {
            plaintext = [];
            return WinZipAesDecryptOutcome.AuthenticationFailed;
        }

        plaintext = DecryptCtr(ciphertext, encryptionKey);
        return WinZipAesDecryptOutcome.Success;
    }

    // WinZip AE always uses AES in CTR mode with a 128-bit little-endian counter starting at 1,
    // built from a plain AES-ECB single-block encryptor — .NET has no named CTR mode, but this is
    // the standard NIST SP 800-38A construction (encrypt the counter, XOR with ciphertext), not a
    // custom cipher.
    private static byte[] DecryptCtr(byte[] ciphertext, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor();

        byte[] plaintext = new byte[ciphertext.Length];
        byte[] counter = new byte[AesBlockSize];
        counter[0] = 1;
        byte[] keystreamBlock = new byte[AesBlockSize];

        int offset = 0;
        while (offset < ciphertext.Length)
        {
            encryptor.TransformBlock(counter, 0, AesBlockSize, keystreamBlock, 0);
            int chunkLength = Math.Min(AesBlockSize, ciphertext.Length - offset);
            for (int i = 0; i < chunkLength; i++)
                plaintext[offset + i] = (byte)(ciphertext[offset + i] ^ keystreamBlock[i]);
            offset += chunkLength;
            IncrementCounter(counter);
        }

        return plaintext;
    }

    private static void IncrementCounter(byte[] counter)
    {
        for (int i = 0; i < counter.Length; i++)
        {
            if (++counter[i] != 0)
                break;
        }
    }
}
