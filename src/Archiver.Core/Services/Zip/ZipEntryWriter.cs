using System.IO.Compression;
using System.Text;
using Archiver.Core.IO;
using Archiver.Core.Models;

namespace Archiver.Core.Services.Zip;

/// <summary>
/// Hand-rolled ZIP container writer (local file headers, central directory, EOCD/Zip64) used
/// by the T-F35 parallel <c>SingleArchive</c> pipeline. Exists only because
/// <see cref="System.IO.Compression.ZipArchive"/> gives no public API to compress a file's
/// bytes independently of the live archive and splice the result in afterward — see
/// <c>DECISIONS.md</c>'s T-F35 entry. Owns the whole output file for the archive it writes
/// (small entries compressed fully in memory by parallel workers, larger entries compressed by
/// parallel workers into a private temp file and streamed in from there, directory placeholders)
/// — a <see cref="ZipArchive"/> and this writer can never share one output stream, since each
/// keeps its own independent bookkeeping. Both write paths know crc/compressed/uncompressed size
/// fully upfront (the in-memory buffer or the finished temp file), so there is no unknown-size
/// placeholder-then-patch mechanism here — see DECISIONS.md's T-F35 follow-up entry for why that
/// mechanism (needed for T-F35's original "large files stream sequentially" design) was removed
/// once compression stopped needing to happen directly on the writer thread for any file size.
///
/// Compression itself still goes through <c>System.IO.Compression.DeflateStream</c> — only the
/// ZIP container bytes (headers/CRC/central directory) are written by hand.
/// </summary>
internal sealed class ZipEntryWriter : IAsyncDisposable
{
    private const uint LocalFileHeaderSignature = 0x04034b50;
    private const uint CentralDirectorySignature = 0x02014b50;
    private const uint EndOfCentralDirectorySignature = 0x06054b50;
    private const uint Zip64EndOfCentralDirectorySignature = 0x06064b50;
    private const uint Zip64EndOfCentralDirectoryLocatorSignature = 0x07064b50;
    private const ushort Zip64ExtraFieldTag = 0x0001;

    private const uint Zip32Marker = 0xFFFFFFFF;
    private const ushort Zip16Marker = 0xFFFF;

    // Values >= this threshold in a 32-bit ZIP field are represented as Zip32Marker instead,
    // with the real 64-bit value carried in a Zip64 extra field.
    private const long Zip64Threshold = 0xFFFFFFFF;

    private const ushort DefaultVersionNeeded = 20;
    private const ushort Zip64VersionNeeded = 45;

    // internal — reused by ParallelSingleArchiveWriter's temp-file compression worker, which
    // needs to pick the same ZIP method code before writing the header for a not-yet-in-memory,
    // not-yet-fully-known entry (T-F35 follow-up: compress-to-temp-file path, no size limit).
    internal const ushort StoredMethod = 0;
    internal const ushort DeflateMethod = 8;

    internal static ushort SelectMethod(CompressionLevel compressionLevel) =>
        compressionLevel == CompressionLevel.NoCompression ? StoredMethod : DeflateMethod;

    private const int CopyBufferSize = 81920; // matches ZipArchiveService.CopyBufferSize
    private const int FileStreamBufferSize = 262144; // matches ZipArchiveService.FileStreamBufferSize

    private const uint DirectoryExternalAttributes = 0x10; // FILE_ATTRIBUTE_DIRECTORY
    private const uint FileExternalAttributes = 0x20; // FILE_ATTRIBUTE_ARCHIVE

    private readonly FileStream _output;
    private readonly List<CentralDirectoryRecord> _records = [];
    private bool _disposed;

    public ZipEntryWriter(string path)
    {
        _output = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 4096, useAsync: false);
    }

    public int EntryCount => _records.Count;

    /// <summary>Writes an entry whose compressed bytes are already fully known (small-file parallel path).</summary>
    public async Task WriteCompressedEntryAsync(
        string entryName, CompressedEntryData data, DateTime lastWriteTime, CancellationToken ct)
    {
        // A byte[] can never reach the 4 GiB Zip64Threshold (int32 length limit), so only the
        // uncompressed length can trigger this — kept as a defensive check, not dead code removed,
        // in case this method is ever called with data compressed from something larger someday.
        bool needsZip64 = data.UncompressedLength >= Zip64Threshold;
        long localHeaderOffset = _output.Position;

        WriteLocalFileHeader(entryName, lastWriteTime, data.Crc32, data.CompressedBytes.Length,
            data.UncompressedLength, data.Method, needsZip64);
        await _output.WriteAsync(data.CompressedBytes, ct).ConfigureAwait(false);

        RecordEntry(entryName, lastWriteTime, data.Crc32, data.Method, data.CompressedBytes.Length,
            data.UncompressedLength, localHeaderOffset, isDirectory: false);
    }

    /// <summary>
    /// Writes an entry whose compressed bytes already exist as a complete, finished stream (T-F35
    /// follow-up: the temp-file compression path — a file is compressed once, in full, into a
    /// private temp file by a background worker, so by the time this method runs, crc/compressed
    /// size/uncompressed size are all already known, exactly like <see cref="WriteCompressedEntryAsync"/>'s
    /// in-memory case). Streams <paramref name="compressedSource"/>'s bytes into the archive via a
    /// bounded-buffer copy — never materializes the whole entry in memory, so this scales to any
    /// file size without a memory ceiling (see DECISIONS.md's T-F35 entry for why the file-size
    /// threshold that used to gate this could be removed once compression stopped needing an
    /// in-memory buffer for the "not tiny" case).
    /// </summary>
    public async Task WriteCompressedEntryFromStreamAsync(
        string entryName, Stream compressedSource, long compressedLength, long uncompressedLength,
        uint crc32, ushort method, DateTime lastWriteTime, CancellationToken ct)
    {
        bool needsZip64 = uncompressedLength >= Zip64Threshold || compressedLength >= Zip64Threshold;
        long localHeaderOffset = _output.Position;

        WriteLocalFileHeader(entryName, lastWriteTime, crc32, compressedLength, uncompressedLength, method, needsZip64);
        await compressedSource.CopyToAsync(_output, CopyBufferSize, ct).ConfigureAwait(false);

        RecordEntry(entryName, lastWriteTime, crc32, method, compressedLength, uncompressedLength,
            localHeaderOffset, isDirectory: false);
    }

    public Task WriteDirectoryPlaceholderAsync(string entryName, DateTime lastWriteTime, CancellationToken ct)
    {
        long localHeaderOffset = _output.Position;
        WriteLocalFileHeader(entryName, lastWriteTime, crc32: 0, compressedSize: 0, uncompressedSize: 0,
            StoredMethod, needsZip64: false);
        RecordEntry(entryName, lastWriteTime, crc32: 0, StoredMethod, compressedSize: 0, uncompressedSize: 0,
            localHeaderOffset, isDirectory: true);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads from <paramref name="source"/> in <paramref name="buffer"/>-sized chunks, updating a
    /// running CRC-32 as it goes, writing each chunk straight to <paramref name="destination"/> —
    /// used both by this writer (no longer directly — see history) and by
    /// <c>ParallelSingleArchiveWriter</c>'s temp-file compression worker, so a file's CRC is
    /// computed in the same single read pass as compression instead of a second full read.
    /// </summary>
    internal static async Task<(long Total, uint Crc32)> CopyWithCrcAsync(
        FileStream source, Stream destination, byte[] buffer,
        IProgress<ProgressReport>? progress, long totalBytes, long startOffset, string entryName,
        CancellationToken ct)
    {
        var acc = new Crc32.Accumulator();
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            acc.Update(buffer.AsSpan(0, read));
            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            total += read;

            if (progress != null && totalBytes > 0)
            {
                long transferred = startOffset + total;
                progress.Report(new ProgressReport
                {
                    Percent = (int)(transferred * 100L / totalBytes),
                    BytesTransferred = transferred,
                    TotalBytes = totalBytes,
                    CurrentFile = entryName,
                });
            }
        }
        return (total, acc.Finish());
    }

    private void WriteLocalFileHeader(
        string entryName, DateTime lastWriteTime, uint crc32, long compressedSize, long uncompressedSize,
        ushort method, bool needsZip64)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(entryName);
        ushort flags = IsAsciiOnly(entryName) ? (ushort)0 : (ushort)0x0800; // bit 11 = UTF-8 name/comment
        ushort versionNeeded = needsZip64 ? Zip64VersionNeeded : DefaultVersionNeeded;
        uint dosDateTime = DosDateTime.Encode(lastWriteTime);

        byte[] extra = needsZip64
            ? BuildZip64LocalExtraField((ulong)uncompressedSize, (ulong)compressedSize)
            : [];

        WriteUInt32(LocalFileHeaderSignature);
        WriteUInt16(versionNeeded);
        WriteUInt16(flags);
        WriteUInt16(method);
        WriteUInt16((ushort)(dosDateTime & 0xFFFF));
        WriteUInt16((ushort)(dosDateTime >> 16));

        WriteUInt32(crc32);
        WriteUInt32(needsZip64 ? Zip32Marker : (uint)compressedSize);
        WriteUInt32(needsZip64 ? Zip32Marker : (uint)uncompressedSize);

        WriteUInt16((ushort)nameBytes.Length);
        WriteUInt16((ushort)extra.Length);
        _output.Write(nameBytes);

        if (extra.Length > 0)
            _output.Write(extra);
    }

    private static byte[] BuildZip64LocalExtraField(ulong uncompressedSize, ulong compressedSize)
    {
        var buffer = new byte[4 + 8 + 8];
        WriteUInt16To(buffer, 0, Zip64ExtraFieldTag);
        WriteUInt16To(buffer, 2, 16); // sub-field data size: two 8-byte values
        WriteUInt64To(buffer, 4, uncompressedSize);
        WriteUInt64To(buffer, 12, compressedSize);
        return buffer;
    }

    private void RecordEntry(
        string entryName, DateTime lastWriteTime, uint crc32, ushort method, long compressedSize,
        long uncompressedSize, long localHeaderOffset, bool isDirectory)
    {
        _records.Add(new CentralDirectoryRecord(
            entryName, method, DosDateTime.Encode(lastWriteTime), crc32, compressedSize, uncompressedSize,
            localHeaderOffset, isDirectory));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        long centralDirectoryOffset = _output.Position;
        foreach (var record in _records)
            WriteCentralDirectoryRecord(record);
        long centralDirectorySize = _output.Position - centralDirectoryOffset;

        WriteEndOfCentralDirectory(centralDirectoryOffset, centralDirectorySize, _records.Count);

        await _output.FlushAsync().ConfigureAwait(false);
        await _output.DisposeAsync().ConfigureAwait(false);
    }

    private void WriteCentralDirectoryRecord(CentralDirectoryRecord record)
    {
        bool needsZip64 = record.CompressedSize >= Zip64Threshold
            || record.UncompressedSize >= Zip64Threshold
            || record.LocalHeaderOffset >= Zip64Threshold;

        byte[] nameBytes = Encoding.UTF8.GetBytes(record.EntryName);
        ushort flags = IsAsciiOnly(record.EntryName) ? (ushort)0 : (ushort)0x0800;
        ushort versionNeeded = needsZip64 ? Zip64VersionNeeded : DefaultVersionNeeded;
        byte[] extra = needsZip64 ? BuildZip64CentralExtraField(record) : [];
        uint externalAttributes = record.IsDirectory ? DirectoryExternalAttributes : FileExternalAttributes;

        WriteUInt32(CentralDirectorySignature);
        WriteUInt16(versionNeeded); // version made by — host byte 0 (MS-DOS-compatible), same low byte as version-needed
        WriteUInt16(versionNeeded);
        WriteUInt16(flags);
        WriteUInt16(record.Method);
        WriteUInt16((ushort)(record.DosDateTime & 0xFFFF));
        WriteUInt16((ushort)(record.DosDateTime >> 16));
        WriteUInt32(record.Crc32);
        WriteUInt32(needsZip64 && record.CompressedSize >= Zip64Threshold ? Zip32Marker : (uint)record.CompressedSize);
        WriteUInt32(needsZip64 && record.UncompressedSize >= Zip64Threshold ? Zip32Marker : (uint)record.UncompressedSize);
        WriteUInt16((ushort)nameBytes.Length);
        WriteUInt16((ushort)extra.Length);
        WriteUInt16(0); // file comment length
        WriteUInt16(0); // disk number start
        WriteUInt16(0); // internal file attributes
        WriteUInt32(externalAttributes);
        WriteUInt32(needsZip64 && record.LocalHeaderOffset >= Zip64Threshold ? Zip32Marker : (uint)record.LocalHeaderOffset);
        _output.Write(nameBytes);
        if (extra.Length > 0)
            _output.Write(extra);
    }

    private static byte[] BuildZip64CentralExtraField(CentralDirectoryRecord record)
    {
        // Spec order: original size, compressed size, relative header offset, disk start
        // number — include only the sub-fields whose fixed-width field used the marker value.
        using var ms = new MemoryStream();
        var sizeBuf = new byte[8];

        if (record.UncompressedSize >= Zip64Threshold)
        {
            WriteUInt64To(sizeBuf, 0, (ulong)record.UncompressedSize);
            ms.Write(sizeBuf);
        }
        if (record.CompressedSize >= Zip64Threshold)
        {
            WriteUInt64To(sizeBuf, 0, (ulong)record.CompressedSize);
            ms.Write(sizeBuf);
        }
        if (record.LocalHeaderOffset >= Zip64Threshold)
        {
            WriteUInt64To(sizeBuf, 0, (ulong)record.LocalHeaderOffset);
            ms.Write(sizeBuf);
        }

        byte[] payload = ms.ToArray();
        var result = new byte[4 + payload.Length];
        WriteUInt16To(result, 0, Zip64ExtraFieldTag);
        WriteUInt16To(result, 2, (ushort)payload.Length);
        payload.CopyTo(result, 4);
        return result;
    }

    private void WriteEndOfCentralDirectory(long centralDirectoryOffset, long centralDirectorySize, int entryCount)
    {
        bool needsZip64 = entryCount >= Zip16Marker
            || centralDirectorySize >= Zip64Threshold
            || centralDirectoryOffset >= Zip64Threshold;

        if (needsZip64)
        {
            long zip64EocdOffset = _output.Position;
            WriteUInt32(Zip64EndOfCentralDirectorySignature);
            WriteUInt64(44); // size of remaining zip64 EOCD record (fixed portion, no extensible data sector)
            WriteUInt16(Zip64VersionNeeded); // version made by
            WriteUInt16(Zip64VersionNeeded); // version needed to extract
            WriteUInt32(0); // number of this disk
            WriteUInt32(0); // disk where central directory starts
            WriteUInt64((ulong)entryCount); // entries on this disk
            WriteUInt64((ulong)entryCount); // total entries
            WriteUInt64((ulong)centralDirectorySize);
            WriteUInt64((ulong)centralDirectoryOffset);

            WriteUInt32(Zip64EndOfCentralDirectoryLocatorSignature);
            WriteUInt32(0); // disk with zip64 EOCD
            WriteUInt64((ulong)zip64EocdOffset);
            WriteUInt32(1); // total number of disks
        }

        WriteUInt32(EndOfCentralDirectorySignature);
        WriteUInt16(0); // number of this disk
        WriteUInt16(0); // disk where central directory starts
        WriteUInt16(entryCount >= Zip16Marker ? Zip16Marker : (ushort)entryCount);
        WriteUInt16(entryCount >= Zip16Marker ? Zip16Marker : (ushort)entryCount);
        WriteUInt32(centralDirectorySize >= Zip64Threshold ? Zip32Marker : (uint)centralDirectorySize);
        WriteUInt32(centralDirectoryOffset >= Zip64Threshold ? Zip32Marker : (uint)centralDirectoryOffset);
        WriteUInt16(0); // comment length
    }

    private static bool IsAsciiOnly(string value)
    {
        foreach (char c in value)
            if (c > 0x7F) return false;
        return true;
    }

    private void WriteUInt16(ushort value)
    {
        Span<byte> buf = stackalloc byte[2];
        WriteUInt16To(buf, 0, value);
        _output.Write(buf);
    }

    private void WriteUInt32(uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        WriteUInt32To(buf, 0, value);
        _output.Write(buf);
    }

    private void WriteUInt64(ulong value)
    {
        Span<byte> buf = stackalloc byte[8];
        WriteUInt64To(buf, 0, value);
        _output.Write(buf);
    }

    private static void WriteUInt16To(Span<byte> buffer, int offset, ushort value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], value);

    private static void WriteUInt32To(Span<byte> buffer, int offset, uint value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], value);

    private static void WriteUInt64To(Span<byte> buffer, int offset, ulong value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buffer[offset..], value);

    private sealed record CentralDirectoryRecord(
        string EntryName, ushort Method, uint DosDateTime, uint Crc32, long CompressedSize,
        long UncompressedSize, long LocalHeaderOffset, bool IsDirectory);
}
