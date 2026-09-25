using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Archiver.Core.IO;

namespace Archiver.Core.Services.Zip.Decryption;

/// <summary>Outcome of either <see cref="EncryptedZipEntryReader.TryOpen(string, string, string, System.Text.Encoding?)"/>
/// overload.</summary>
internal enum EncryptedZipReadResult
{
    Success,

    /// <summary>Password rejected — either ZipCrypto's check byte or WinZip AE's password
    /// verification value/HMAC didn't match. Covers "wrong password" and "tampered ciphertext"
    /// alike for AE, since both are only detectable via the same HMAC check.</summary>
    WrongPassword,

    /// <summary>Password-level checks passed, but the content failed a subsequent integrity
    /// check — data corruption/tampering independent of the password itself. Returned directly
    /// from <see cref="EncryptedZipEntryReader.TryOpen(Stream, LocatedZipEntry, string, System.Text.Encoding?)"/> for a
    /// WinZip AE HMAC failure (checked eagerly, before any plaintext exists); surfaces lazily
    /// instead, thrown as <see cref="InvalidDataException"/> from the returned <see cref="Stream"/>
    /// only once the caller reads all the way to its end, for a ZipCrypto/AE-1 CRC-32 mismatch
    /// (see T-F189's streaming design point in docs/DECISIONS.md).</summary>
    Corrupted,

    /// <summary>T-F194: the password verified, but the entry's real compression method (e.g.
    /// BZip2/LZMA under AES) is neither Store nor Deflate — the only two System.IO.Compression can
    /// decode. Reported instead of throwing, so it can never escape a "never throws" service.</summary>
    UnsupportedCompressionMethod,
}

/// <summary>
/// Orchestrates decrypting one ZIP entry: locates its raw bytes via
/// <see cref="RawZipEntryLocator"/>, decrypts via <see cref="ZipCryptoStream"/> or
/// <see cref="WinZipAesReader"/> depending on the entry's real compression method, and returns a
/// lazily-decompressing <see cref="Stream"/> — no destination planning, conflict resolution, or
/// security checks, all of which stay in the normal extraction pipeline that calls this in place
/// of <c>ZipArchiveEntry.Open()</c> (see the ZIP Password Support design's "no second extraction
/// path" invariant, <c>docs/TASKS.md</c> T-F189).
/// <para>
/// T-F189 design point (see docs/DECISIONS.md): the returned stream decompresses on demand rather
/// than eagerly materializing the full decompressed content into a byte array — this is what lets
/// <c>ProgressStream</c>/T-F16 wrap it for real byte-accurate progress on an encrypted entry,
/// exactly like <c>ZipArchiveEntry.Open()</c> today. WinZip AE's HMAC is still verified over the
/// *entire* ciphertext, eagerly, before this method returns anything — no plaintext is ever
/// released before that authentication succeeds. ZipCrypto/AE-1's secondary CRC-32 content check
/// (AE-2's header CRC is zeroed by design; HMAC alone is authoritative there) is verified lazily
/// by the returned stream itself as the caller reads it, via <see cref="VerifyingReadStream"/>,
/// which also caps every entry at its declared size (T-F231) — the same wrapper unencrypted
/// extraction uses (T-F246).
/// </para>
/// </summary>
internal static class EncryptedZipEntryReader
{
    /// <summary>Name-based convenience overload — locates the entry by name in
    /// <paramref name="zipPath"/> before delegating. Used directly by tests and by any caller that
    /// hasn't already resolved a <see cref="LocatedZipEntry"/> some other way. Production
    /// extraction code (ZipArchiveService) prefers the <see cref="LocatedZipEntry"/> overload
    /// below instead, paired positionally via <see cref="RawZipEntryLocator.LocateAll"/> — see
    /// that method's own doc comment for why name-based lookup is avoided there. The returned
    /// stream owns the file it opened.</summary>
    public static (EncryptedZipReadResult Result, Stream? Content) TryOpen(
        string zipPath, string entryFullName, string password, Encoding? ansi = null)
    {
        var fileStream = File.OpenRead(zipPath);
        try
        {
            LocatedZipEntry located = RawZipEntryLocator.Locate(fileStream, entryFullName);
            if (!located.GeneralPurposeEncryptedBit)
                throw new InvalidOperationException(
                    $"'{entryFullName}' is not encrypted — the caller must check the general-purpose " +
                    "encrypted bit before invoking EncryptedZipEntryReader.");

            var opened = Open(fileStream, located, password, ansi, ownsStream: true);
            if (opened.Content is null)
                fileStream.Dispose();
            return opened;
        }
        catch
        {
            fileStream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens the entry described by <paramref name="located"/> from <paramref name="zipStream"/>
    /// (which it seeks freely; the caller keeps ownership). T-F193: nothing is buffered — for
    /// WinZip AE the whole ciphertext is first authenticated straight from the stream (pass 1, HMAC
    /// only, no plaintext), then the returned stream decrypts and decompresses as it is read
    /// (pass 2). Both passes read the same open handle; callers open it via File.OpenRead
    /// (FileShare.Read), so no other process can modify the archive between the two passes.
    /// </summary>
    public static (EncryptedZipReadResult Result, Stream? Content) TryOpen(
        Stream zipStream, LocatedZipEntry located, string password, Encoding? ansi = null) =>
        Open(zipStream, located, password, ansi, ownsStream: false);

    // T-F244 item 3: the bytes a non-ASCII password was encrypted with depend on the tool — Windows
    // tools use the ANSI code page, others UTF-8. Same order as NanaZip: ZipCrypto ANSI first (7-Zip
    // >= 17 decodes it with CP_ACP), WinZip AES UTF-8 first (the spec's encoding). The next
    // candidate is tried only when the password check itself fails — never mid-stream.
    private static List<byte[]> PasswordCandidates(string password, bool aes, Encoding? ansi)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(password);
        byte[] ansiBytes = (ansi ?? ZipNameCodePages.System.Ansi).GetBytes(password);
        if (ansiBytes.AsSpan().SequenceEqual(utf8))
        {
            CryptographicOperations.ZeroMemory(ansiBytes);
            return [utf8];
        }
        return aes ? [utf8, ansiBytes] : [ansiBytes, utf8];
    }

    private static void Zero(List<byte[]> candidates)
    {
        foreach (byte[] candidate in candidates)
            CryptographicOperations.ZeroMemory(candidate);
    }

    /// <summary>
    /// T-F193: cheap password check only — WinZip AE's 2-byte verification value or ZipCrypto's
    /// check byte, no pass over the entry data. Returns <see cref="EncryptedZipReadResult.Success"/>
    /// or <see cref="EncryptedZipReadResult.WrongPassword"/>; a wrong-but-accepted ZipCrypto
    /// password (~1 in 256) is caught later by that entry's CRC-32, and tampered AES data by its HMAC.
    /// </summary>
    public static EncryptedZipReadResult VerifyPassword(
        Stream zipStream, LocatedZipEntry located, string password, Encoding? ansi = null)
    {
        EnsureWithinStream(zipStream, located);
        bool aes = located.CompressionMethod == 99;
        var candidates = PasswordCandidates(password, aes, ansi);
        try
        {
            foreach (byte[] candidate in candidates)
            {
                using var region = new EntryRegionStream(zipStream, located.CompressedDataOffset, located.CompressedSize, ownsSource: false);
                bool matches = aes ? AesVerifierMatches(region, located, candidate) : ZipCryptoCheckByteMatches(region, located, candidate);
                if (matches)
                    return EncryptedZipReadResult.Success;
            }
            return EncryptedZipReadResult.WrongPassword;
        }
        finally
        {
            Zero(candidates);
        }
    }

    private static bool AesVerifierMatches(Stream region, LocatedZipEntry located, byte[] passwordBytes)
    {
        byte[] salt = ReadExactly(region, WinZipAesReader.SaltLength(located.AesStrengthBits));
        byte[] storedVerify = ReadExactly(region, WinZipAesReader.PasswordVerificationLength);
        var (encryptionKey, authenticationKey, derivedVerify) = WinZipAesReader.DeriveKeys(passwordBytes, salt, located.AesStrengthBits);
        CryptographicOperations.ZeroMemory(encryptionKey);
        CryptographicOperations.ZeroMemory(authenticationKey);
        return derivedVerify.AsSpan().SequenceEqual(storedVerify);
    }

    private static bool ZipCryptoCheckByteMatches(Stream region, LocatedZipEntry located, byte[] passwordBytes)
    {
        if (!ZipCryptoStream.TryCreate(region, passwordBytes, located.ZipCryptoCheckByte, out Stream? plaintext))
            return false;
        plaintext!.Dispose();
        return true;
    }

    private static (EncryptedZipReadResult Result, Stream? Content) Open(
        Stream zipStream, LocatedZipEntry located, string password, Encoding? ansi, bool ownsStream)
    {
        EnsureWithinStream(zipStream, located);
        bool aes = located.CompressionMethod == 99;
        var candidates = PasswordCandidates(password, aes, ansi);
        try
        {
            // Pick the candidate by the cheap password check on a non-owning view, then open once —
            // a failed attempt must not dispose a stream the returned content will own.
            byte[] chosen = candidates[0];
            foreach (byte[] candidate in candidates)
            {
                using var region = new EntryRegionStream(zipStream, located.CompressedDataOffset, located.CompressedSize, ownsSource: false);
                if (aes ? AesVerifierMatches(region, located, candidate) : ZipCryptoCheckByteMatches(region, located, candidate))
                {
                    chosen = candidate;
                    break;
                }
            }
            return aes
                ? OpenWinZipAes(zipStream, located, chosen, ownsStream)
                : OpenZipCrypto(zipStream, located, chosen, ownsStream);
        }
        finally
        {
            Zero(candidates);
        }
    }

    // T-F194: CompressedSize comes from the local header — attacker-controlled independently of
    // the central directory. Bounding it by the bytes that actually exist keeps every read inside
    // the archive (no allocation is sized by it any more since T-F193's streaming reader).
    private static void EnsureWithinStream(Stream zipStream, LocatedZipEntry located)
    {
        if (located.CompressedSize < 0 || located.CompressedDataOffset < 0
            || located.CompressedSize > zipStream.Length - located.CompressedDataOffset)
            throw new InvalidDataException(
                $"Encrypted entry compressed size ({located.CompressedSize}) runs past the end of the archive.");
    }

    private static (EncryptedZipReadResult, Stream?) OpenWinZipAes(
        Stream zipStream, LocatedZipEntry located, byte[] passwordBytes, bool ownsStream)
    {
        int saltLength = WinZipAesReader.SaltLength(located.AesStrengthBits);
        long headerLength = saltLength + WinZipAesReader.PasswordVerificationLength;
        long ciphertextLength = located.CompressedSize - headerLength - WinZipAesReader.AuthenticationCodeLength;
        if (ciphertextLength < 0)
            throw new InvalidDataException("WinZip AES entry data is too short to contain salt/verification/auth fields.");

        long offset = located.CompressedDataOffset;
        byte[] salt, storedVerify, storedTag;
        using (var header = new EntryRegionStream(zipStream, offset, headerLength, ownsSource: false))
        {
            salt = ReadExactly(header, saltLength);
            storedVerify = ReadExactly(header, WinZipAesReader.PasswordVerificationLength);
        }
        using (var tag = new EntryRegionStream(zipStream, offset + headerLength + ciphertextLength,
                   WinZipAesReader.AuthenticationCodeLength, ownsSource: false))
        {
            storedTag = ReadExactly(tag, WinZipAesReader.AuthenticationCodeLength);
        }

        var (encryptionKey, authenticationKey, derivedVerify) =
            WinZipAesReader.DeriveKeys(passwordBytes, salt, located.AesStrengthBits);
        // T-F244 item 3: key material is wiped as soon as it is no longer needed (AES copies the
        // encryption key into its own state, which it clears on dispose).
        try
        {
            if (!derivedVerify.AsSpan().SequenceEqual(storedVerify))
                return (EncryptedZipReadResult.WrongPassword, null);

            using (var pass1 = new EntryRegionStream(zipStream, offset + headerLength, ciphertextLength, ownsSource: false))
            {
                if (!WinZipAesReader.Authenticate(pass1, authenticationKey, storedTag))
                    return (EncryptedZipReadResult.Corrupted, null);
            }

            if (!IsSupportedMethod(located.RealCompressionMethod))
                return (EncryptedZipReadResult.UnsupportedCompressionMethod, null);

            // AE-2 zeroes the header CRC-32 by design — HMAC (already checked above, before any
            // plaintext exists) is the sole authority there, so no trailer check is wrapped on.
            // AE-1 keeps the real CRC-32 — same trailer check ZipCrypto gets below.
            var pass2 = new EntryRegionStream(zipStream, offset + headerLength, ciphertextLength, ownsStream);
            uint? expectedCrc = located.AeVersion == 1 ? located.StoredCrc32 : null;
            return (EncryptedZipReadResult.Success,
                WrapDecompression(new WinZipAesCtrStream(pass2, encryptionKey), located.RealCompressionMethod, expectedCrc,
                    located.UncompressedSize));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
            CryptographicOperations.ZeroMemory(authenticationKey);
        }
    }
    private static (EncryptedZipReadResult, Stream?) OpenZipCrypto(
        Stream zipStream, LocatedZipEntry located, byte[] passwordBytes, bool ownsStream)
    {
        if (located.CompressedSize < ZipCryptoStream.EncryptionHeaderLength)
            throw new InvalidDataException("ZipCrypto entry data is shorter than the 12-byte encryption header.");

        var region = new EntryRegionStream(zipStream, located.CompressedDataOffset, located.CompressedSize, ownsStream);
        byte expectedCheckByte = located.ZipCryptoCheckByte;
        if (!ZipCryptoStream.TryCreate(region, passwordBytes, expectedCheckByte, out Stream? plaintext))
        {
            region.Dispose();
            return (EncryptedZipReadResult.WrongPassword, null);
        }

        if (!IsSupportedMethod(located.CompressionMethod))
        {
            plaintext!.Dispose();
            return (EncryptedZipReadResult.UnsupportedCompressionMethod, null);
        }

        return (EncryptedZipReadResult.Success,
            WrapDecompression(plaintext!, located.CompressionMethod, located.StoredCrc32, located.UncompressedSize));
    }

    private static bool IsSupportedMethod(ushort method) => method is 0 or 8;

    private static byte[] ReadExactly(Stream stream, int count)
    {
        byte[] buffer = new byte[count];
        stream.ReadExactly(buffer);
        return buffer;
    }

    // T-F231: every decrypted entry is capped at its declared size — AE-2 has no CRC, so the cap is
    // its only length check; ZipCrypto/AE-1 also get the CRC-32 check at end of stream.
    private static VerifyingReadStream WrapDecompression(Stream data, ushort method, uint? expectedCrc32, long declaredLength)
    {
        Stream decompressed = method switch
        {
            0 => data, // Store
            8 => new DeflateStream(data, CompressionMode.Decompress),
            _ => throw new NotSupportedException($"Unsupported compression method under encryption: {method}.")
        };

        return new VerifyingReadStream(decompressed, declaredLength, expectedCrc32);
    }
}
