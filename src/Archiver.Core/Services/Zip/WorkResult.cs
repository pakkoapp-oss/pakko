using Archiver.Core.Models;

namespace Archiver.Core.Services.Zip;

internal enum WorkResultKind { Compressed, TempFileCompressed, SourceStored, DirectoryPlaceholder, Error }

/// <summary>
/// Outcome of processing one <see cref="FileWorkItem"/>, produced by a (possibly parallel)
/// compression worker and consumed strictly in enqueue order by the single writer thread — see
/// <see cref="ParallelSingleArchiveWriter"/>. Reparse-point skips are reported directly during
/// enumeration (never dispatched as work at all), so there is no "Skipped" case here.
///
/// <see cref="WorkResultKind.TempFileCompressed"/> replaced the original "large files stream sequentially,
/// single-threaded" design (T-F35 follow-up) — a file above the in-memory threshold is now ALSO
/// compressed in parallel, into a private temp file instead of a `byte[]`, removing the file-size
/// ceiling that design needed (see DECISIONS.md). Both compressed cases know crc/compressed/
/// uncompressed size fully upfront by the time the writer sees them.
///
/// <see cref="WorkResultKind.SourceStored"/> (T-F357): an unencrypted entry stored as it is, which
/// the writer copies from <see cref="Source"/> - the worker's own read handle, kept open (sharing
/// read only) so the bytes its CRC and size describe cannot change before the copy.
/// </summary>
internal sealed record WorkResult
{
    public required WorkResultKind Kind { get; init; }
    public string EntryName { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public DateTime LastWriteTime { get; init; }
    public CompressedEntryData Compressed { get; init; }
    public string TempFilePath { get; init; } = "";
    public uint Crc32 { get; init; }
    public long CompressedSize { get; init; }
    public long UncompressedSize { get; init; }
    public ushort Method { get; init; }

    /// <summary>T-F193: the temp file holds a WinZip AES payload around <see cref="Method"/>'s output.</summary>
    public bool IsAesEncrypted { get; init; }

    /// <summary>T-F357: owned by this result; the writer disposes it after copying, or the pipeline at its end.</summary>
    public FileStream? Source { get; init; }
    public CoreText? ErrorText { get; init; }
    public Exception? ErrorException { get; init; }

    public static WorkResult ForCompressed(string entryName, CompressedEntryData data, DateTime lastWriteTime) => new()
    {
        Kind = WorkResultKind.Compressed, EntryName = entryName, Compressed = data, LastWriteTime = lastWriteTime,
    };

    public static WorkResult ForTempFileCompressed( // NOSONAR: S107 — independent raw ZIP fields, same reasoning as ZipEntryWriter.WriteCompressedEntryFromStreamAsync
        string entryName, string tempFilePath, uint crc32, long compressedSize, long uncompressedSize,
        ushort method, DateTime lastWriteTime, bool isAesEncrypted = false) => new()
    {
        Kind = WorkResultKind.TempFileCompressed, EntryName = entryName, TempFilePath = tempFilePath,
        Crc32 = crc32, CompressedSize = compressedSize, UncompressedSize = uncompressedSize,
        Method = method, LastWriteTime = lastWriteTime, IsAesEncrypted = isAesEncrypted,
    };

    public static WorkResult ForSourceStored(string entryName, FileStream source, uint crc32, long size, DateTime lastWriteTime) => new()
    {
        Kind = WorkResultKind.SourceStored, EntryName = entryName, Source = source, Crc32 = crc32,
        CompressedSize = size, UncompressedSize = size, Method = ZipEntryWriter.StoredMethod, LastWriteTime = lastWriteTime,
    };

    public static WorkResult ForDirectoryPlaceholder(string entryName, DateTime lastWriteTime) => new()
    {
        Kind = WorkResultKind.DirectoryPlaceholder, EntryName = entryName, LastWriteTime = lastWriteTime,
    };

    public static WorkResult ForError(string sourcePath, CoreText text, Exception? exception) => new()
    {
        Kind = WorkResultKind.Error, SourcePath = sourcePath, ErrorText = text, ErrorException = exception,
    };
}
