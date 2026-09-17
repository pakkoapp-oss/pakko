using System.IO.Compression;

namespace Archiver.Core.Services.Zip.Decryption;

/// <summary>Outcome of <see cref="EncryptedZipEntryReader.TryOpen"/>.</summary>
internal enum EncryptedZipReadResult
{
    Success,

    /// <summary>Password rejected — either ZipCrypto's check byte or WinZip AE's password
    /// verification value/HMAC didn't match. Covers "wrong password" and "tampered ciphertext"
    /// alike for AE, since both are only detectable via the same HMAC check.</summary>
    WrongPassword,

    /// <summary>Password-level checks passed, but the decompressed content's own CRC-32 (ZipCrypto,
    /// or an AE-1 entry's real stored CRC) doesn't match — data corruption independent of the
    /// password itself.</summary>
    Corrupted,
}

/// <summary>
/// Orchestrates decrypting one ZIP entry: locates its raw bytes via
/// <see cref="RawZipEntryLocator"/>, decrypts via <see cref="ZipCryptoStream"/> or
/// <see cref="WinZipAesReader"/> depending on the entry's real compression method, decompresses,
/// and verifies the result. Produces only a decrypted-content <see cref="Stream"/> — no
/// destination planning, conflict resolution, or security checks, all of which stay in the normal
/// extraction pipeline that calls this in place of <c>ZipArchiveEntry.Open()</c> (see the ZIP
/// Password Support design's "no second extraction path" invariant, <c>docs/TASKS.md</c> T-F189).
/// </summary>
internal static class EncryptedZipEntryReader
{
    public static (EncryptedZipReadResult Result, Stream? Content) TryOpen(
        string zipPath, string entryFullName, string password)
    {
        using var fileStream = File.OpenRead(zipPath);
        LocatedZipEntry located = RawZipEntryLocator.Locate(fileStream, entryFullName);

        if (!located.GeneralPurposeEncryptedBit)
            throw new InvalidOperationException(
                $"'{entryFullName}' is not encrypted — the caller must check the general-purpose " +
                "encrypted bit before invoking EncryptedZipEntryReader.");

        fileStream.Seek(located.CompressedDataOffset, SeekOrigin.Begin);
        byte[] rawEntryData = new byte[located.CompressedSize];
        ReadExact(fileStream, rawEntryData);

        return located.CompressionMethod == 99
            ? OpenWinZipAes(located, rawEntryData, password)
            : OpenZipCrypto(located, rawEntryData, password);
    }

    private static (EncryptedZipReadResult, Stream?) OpenWinZipAes(
        LocatedZipEntry located, byte[] rawEntryData, string password)
    {
        var outcome = WinZipAesReader.TryDecrypt(
            rawEntryData, password, located.AesStrengthBits, out byte[] decompressedInput);
        switch (outcome)
        {
            case WinZipAesDecryptOutcome.WrongPassword:
                return (EncryptedZipReadResult.WrongPassword, null);
            case WinZipAesDecryptOutcome.AuthenticationFailed:
                return (EncryptedZipReadResult.Corrupted, null);
        }

        byte[] content = Decompress(decompressedInput, located.RealCompressionMethod);

        // AE-2 zeroes the header CRC-32 by design — HMAC (already checked above) is the sole
        // authority there. AE-1 keeps the real CRC-32, so it gets the same independent check
        // ZipCrypto relies on.
        if (located.AeVersion == 1 && Archiver.Core.IO.Crc32.Compute(new MemoryStream(content)) != located.StoredCrc32)
            return (EncryptedZipReadResult.Corrupted, null);

        return (EncryptedZipReadResult.Success, new MemoryStream(content));
    }

    private static (EncryptedZipReadResult, Stream?) OpenZipCrypto(
        LocatedZipEntry located, byte[] rawEntryData, string password)
    {
        byte expectedCheckByte = (byte)(located.StoredCrc32 >> 24);
        if (!ZipCryptoStream.TryDecrypt(rawEntryData, password, expectedCheckByte, out byte[] decompressedInput))
            return (EncryptedZipReadResult.WrongPassword, null);

        byte[] content = Decompress(decompressedInput, located.CompressionMethod);

        if (Archiver.Core.IO.Crc32.Compute(new MemoryStream(content)) != located.StoredCrc32)
            return (EncryptedZipReadResult.Corrupted, null);

        return (EncryptedZipReadResult.Success, new MemoryStream(content));
    }

    private static byte[] Decompress(byte[] data, ushort method)
    {
        if (method == 0) // Store
            return data;
        if (method != 8) // Deflate
            throw new NotSupportedException($"Unsupported compression method under encryption: {method}.");

        using var input = new MemoryStream(data);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    private static void ReadExact(Stream stream, byte[] buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
            if (read == 0)
                throw new EndOfStreamException();
            totalRead += read;
        }
    }
}
