using System.IO.Compression;
using Archiver.Core.IO;

namespace Archiver.Core.Services.Zip;

/// <summary>Result of compressing one file's bytes fully into memory.</summary>
internal readonly record struct CompressedEntryData(
    byte[] CompressedBytes,
    uint Crc32,
    long UncompressedLength,
    ushort Method);

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

    public static CompressedEntryData Compress(Stream sourceStream, CompressionLevel compressionLevel)
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
            long uncompressedLength;
            using (var deflate = new DeflateStream(buffer, compressionLevel, leaveOpen: true))
                uncompressedLength = CopyWithCrc(sourceStream, deflate, ref acc);
            return new CompressedEntryData(buffer.ToArray(), acc.Finish(), uncompressedLength, DeflateMethod);
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
