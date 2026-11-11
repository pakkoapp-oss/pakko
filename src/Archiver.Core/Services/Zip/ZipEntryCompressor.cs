using System.IO.Compression;
using Archiver.Core.IO;

namespace Archiver.Core.Services.Zip;

/// <summary>
/// Result of compressing one file's bytes fully into memory. <see cref="Method"/> is always the
/// real compression method; when <see cref="IsAesEncrypted"/> is set (T-F193), the bytes are the
/// complete WinZip AES payload wrapped around that method's output.
/// </summary>
internal readonly record struct CompressedEntryData(
    byte[] CompressedBytes,
    uint Crc32,
    long UncompressedLength,
    ushort Method,
    bool IsAesEncrypted = false);

/// <summary>
/// Compresses a file's bytes into an in-memory buffer, independent of any live
/// <see cref="ZipArchive"/> — the building block <see cref="ZipEntryWriter"/>'s parallel
/// compression workers use, since <c>System.IO.Compression</c> gives no way to produce a
/// standalone compressed payload through <c>ZipArchiveEntry</c> itself.
/// </summary>
internal static class ZipEntryCompressor
{
    // Matches ZipArchiveEntry's own observed behavior (confirmed by inspecting its raw output):
    // CompressionLevel.NoCompression uses the ZIP "Stored" method (0, raw bytes, no deflate
    // framing) rather than a zero-effort Deflate stream — smaller and simpler to reproduce
    // exactly, and this hand-rolled writer's "Stored" path is trivially correct by construction.
    private const ushort StoredMethod = 0;
    private const ushort DeflateMethod = 8;

    // T-F318: deflater is a reused compressor from the run's
    // DeflateEncoderPool; null builds one for this entry.
    public static CompressedEntryData Compress(Stream sourceStream, CompressionLevel compressionLevel, string? password = null,
        DeflateEncoderPool.Deflater? deflater = null)
    {
        CompressedEntryData plain = CompressPlain(sourceStream, compressionLevel, deflater);
        if (password is null)
            return plain;

        using var encrypted = new MemoryStream(plain.CompressedBytes.Length + WinZipAesEncryptStream.Overhead);
        using (var aes = new WinZipAesEncryptStream(encrypted, password, plain.CompressedBytes.Length))
            aes.Write(plain.CompressedBytes);
        return plain with { CompressedBytes = encrypted.ToArray(), IsAesEncrypted = true };
    }

    private static CompressedEntryData CompressPlain(Stream sourceStream, CompressionLevel compressionLevel,
        DeflateEncoderPool.Deflater? deflater)
    {
        var acc = new Crc32.Accumulator();
        using var buffer = new MemoryStream();

        if (compressionLevel == CompressionLevel.NoCompression)
        {
            long uncompressedLength = CopyWithCrc(sourceStream, buffer, ref acc);
            return new CompressedEntryData(buffer.ToArray(), acc.Finish(), uncompressedLength, StoredMethod);
        }
        else
        {
            using var raw = new MemoryStream();
            long uncompressedLength = CopyWithCrc(sourceStream, raw, ref acc);

            // Up to .NET 10 a zero-length input made DeflateStream write nothing at all, which
            // 7-Zip rejects as corrupt; ZipArchiveEntry stores empty entries at every level, and
            // so does this writer (.claude/rules/core.md, the zero-byte rule).
            if (uncompressedLength == 0)
                return new CompressedEntryData(Array.Empty<byte>(), acc.Finish(), 0, StoredMethod);

            using DeflateEncoderPool.Deflater? own = deflater is null
                ? new DeflateEncoderPool.Deflater(DeflateEncoderPool.QualityOf(compressionLevel))
                : null;
            ReadOnlySpan<byte> packed = (deflater ?? own!).Compress(raw.GetBuffer().AsSpan(0, (int)raw.Length));

            // T-F299: Fastest (zlib-ng level 1) grows random data by ~5.5% and the other levels by
            // a few KB; 7-Zip stores such an entry instead, and so does this writer.
            if (packed.Length >= uncompressedLength)
                return new CompressedEntryData(raw.ToArray(), acc.Finish(), uncompressedLength, StoredMethod);

            return new CompressedEntryData(packed.ToArray(), acc.Finish(), uncompressedLength, DeflateMethod);
        }
    }

    private static long CopyWithCrc(Stream source, Stream destination, ref Crc32.Accumulator acc)
    {
        Span<byte> chunk = stackalloc byte[8192];
        long total = 0;
        int read;
        while ((read = source.Read(chunk)) > 0)
        {
            acc.Update(chunk[..read]);
            destination.Write(chunk[..read]);
            total += read;
        }
        return total;
    }
}
