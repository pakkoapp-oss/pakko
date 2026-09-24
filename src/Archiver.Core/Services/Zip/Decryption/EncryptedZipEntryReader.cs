using System.IO.Compression;
using Archiver.Core.IO;

namespace Archiver.Core.Services.Zip.Decryption;

/// <summary>Outcome of either <see cref="EncryptedZipEntryReader.TryOpen(string, string, string)"/>
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
    /// from <see cref="EncryptedZipEntryReader.TryOpen(Stream, LocatedZipEntry, string)"/> for a
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
/// by the returned stream itself as the caller reads it, via <see cref="TrailerCrcCheckStream"/> —
/// consistent with how a normal unencrypted extraction also never checks CRC-32 during Extract,
/// only during Test (see ZipArchiveService.TestArchiveEntries).
/// </para>
/// </summary>
internal static class EncryptedZipEntryReader
{
    /// <summary>Name-based convenience overload — locates the entry by name in
    /// <paramref name="zipPath"/> before delegating. Used directly by tests and by any caller that
    /// hasn't already resolved a <see cref="LocatedZipEntry"/> some other way. Production
    /// extraction code (ZipArchiveService) prefers the <see cref="LocatedZipEntry"/> overload
    /// below instead, paired positionally via <see cref="RawZipEntryLocator.LocateAll"/> — see
    /// that method's own doc comment for why name-based lookup is avoided there.</summary>
    public static (EncryptedZipReadResult Result, Stream? Content) TryOpen(
        string zipPath, string entryFullName, string password)
    {
        using var fileStream = File.OpenRead(zipPath);
        LocatedZipEntry located = RawZipEntryLocator.Locate(fileStream, entryFullName);

        if (!located.GeneralPurposeEncryptedBit)
            throw new InvalidOperationException(
                $"'{entryFullName}' is not encrypted — the caller must check the general-purpose " +
                "encrypted bit before invoking EncryptedZipEntryReader.");

        return TryOpen(fileStream, located, password);
    }

    /// <summary>
    /// Reads and decrypts the entry described by <paramref name="located"/> from
    /// <paramref name="zipStream"/> (seeked internally — caller's position is not preserved).
    /// The ciphertext itself is buffered into memory (unavoidable — WinZip AE's HMAC must
    /// authenticate the whole ciphertext before any of it can be trusted); the returned stream on
    /// success decompresses that already-verified buffer lazily rather than eagerly expanding it.
    /// </summary>
    public static (EncryptedZipReadResult Result, Stream? Content) TryOpen(
        Stream zipStream, LocatedZipEntry located, string password)
    {
        // T-F189 (advisor-caught): RawZipEntryLocator.BuildFromLocalHeader doesn't parse the
        // Zip64 (0x0001) extra field, so a Zip64-sized entry under a classic 32-bit header reads
        // back here as the 0xFFFFFFFF sentinel (or any other value that simply can't be buffered
        // safely). Allocating that would throw OutOfMemoryException/OverflowException uncaught —
        // this project's hard constraint is that Archiver.Core services never throw to callers.
        // Fail closed with a structured, caught exception instead; Zip64 encrypted entries are
        // simply not supported (see docs/DECISIONS.md's T-F189 entry).
        if (located.CompressedSize is < 0 or >= int.MaxValue)
            throw new InvalidDataException(
                $"Encrypted entry compressed size ({located.CompressedSize}) is not supported (possibly Zip64).");

        // T-F194: CompressedSize comes from the local header — attacker-controlled independently
        // of the central directory — and sizes the allocation below. Bounding it by the bytes that
        // actually exist makes the allocation provably no larger than the archive file itself.
        if (located.CompressedSize > zipStream.Length - located.CompressedDataOffset)
            throw new InvalidDataException(
                $"Encrypted entry compressed size ({located.CompressedSize}) runs past the end of the archive.");

        zipStream.Seek(located.CompressedDataOffset, SeekOrigin.Begin);
        byte[] rawEntryData = new byte[located.CompressedSize];
        ReadExact(zipStream, rawEntryData);

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

        if (!IsSupportedMethod(located.RealCompressionMethod))
            return (EncryptedZipReadResult.UnsupportedCompressionMethod, null);

        // AE-2 zeroes the header CRC-32 by design — HMAC (already checked above, before any
        // plaintext exists) is the sole authority there, so no trailer check is wrapped on.
        // AE-1 keeps the real CRC-32 — same trailer check ZipCrypto gets below.
        uint? expectedCrc = located.AeVersion == 1 ? located.StoredCrc32 : null;
        return (EncryptedZipReadResult.Success,
            WrapDecompression(decompressedInput, located.RealCompressionMethod, expectedCrc));
    }

    private static (EncryptedZipReadResult, Stream?) OpenZipCrypto(
        LocatedZipEntry located, byte[] rawEntryData, string password)
    {
        byte expectedCheckByte = (byte)(located.StoredCrc32 >> 24);
        if (!ZipCryptoStream.TryDecrypt(rawEntryData, password, expectedCheckByte, out byte[] decompressedInput))
            return (EncryptedZipReadResult.WrongPassword, null);

        if (!IsSupportedMethod(located.CompressionMethod))
            return (EncryptedZipReadResult.UnsupportedCompressionMethod, null);

        return (EncryptedZipReadResult.Success,
            WrapDecompression(decompressedInput, located.CompressionMethod, located.StoredCrc32));
    }

    private static bool IsSupportedMethod(ushort method) => method is 0 or 8;

    private static Stream WrapDecompression(byte[] data, ushort method, uint? expectedCrc32)
    {
        Stream decompressed = method switch
        {
            0 => new MemoryStream(data), // Store
            8 => new DeflateStream(new MemoryStream(data), CompressionMode.Decompress),
            _ => throw new NotSupportedException($"Unsupported compression method under encryption: {method}.")
        };

        return expectedCrc32 is { } crc ? new TrailerCrcCheckStream(decompressed, crc) : decompressed;
    }

    /// <summary>
    /// Wraps a decompressed-content stream, computing a running CRC-32 as the caller reads it and
    /// comparing against <paramref name="expectedCrc32"/> only once end-of-stream is reached —
    /// keeps the T-F189 streaming property (no full-content buffering) while still catching
    /// ZipCrypto/AE-1 content corruption independent of the password itself. A mismatch throws
    /// <see cref="InvalidDataException"/> from the final <see cref="Read"/> call, which the
    /// existing extraction pipeline's <c>catch (InvalidDataException)</c> already handles the same
    /// way it handles any other structurally-corrupt entry.
    /// </summary>
    private sealed class TrailerCrcCheckStream(Stream inner, uint expectedCrc32) : Stream
    {
        private Crc32.Accumulator _accumulator = new();
        private bool _finished;

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            if (read > 0)
            {
                _accumulator.Update(buffer.AsSpan(offset, read));
            }
            else if (!_finished)
            {
                _finished = true;
                uint computed = _accumulator.Finish();
                if (computed != expectedCrc32)
                    throw new InvalidDataException(
                        $"Decrypted entry failed CRC-32 check (expected {expectedCrc32:X8}, got {computed:X8}).");
            }
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
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
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
