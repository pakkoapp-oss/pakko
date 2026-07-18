namespace Archiver.Core.Services.Zip;

internal enum WorkResultKind { Compressed, TempFileCompressed, DirectoryPlaceholder, Error }

/// <summary>
/// Outcome of processing one <see cref="FileWorkItem"/>, produced by a (possibly parallel)
/// compression worker and consumed strictly in enqueue order by the single writer thread — see
/// <see cref="ParallelSingleArchiveWriter"/>. Reparse-point skips are reported directly during
/// enumeration (never dispatched as work at all), so there is no "Skipped" case here.
///
/// <see cref="TempFileCompressed"/> replaced the original "large files stream sequentially,
/// single-threaded" design (T-F35 follow-up) — a file above the in-memory threshold is now ALSO
/// compressed in parallel, into a private temp file instead of a `byte[]`, removing the file-size
/// ceiling that design needed (see DECISIONS.md). Both compressed cases know crc/compressed/
/// uncompressed size fully upfront by the time the writer sees them.
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
    public string? ErrorMessage { get; init; }
    public Exception? ErrorException { get; init; }

    public static WorkResult ForCompressed(string entryName, CompressedEntryData data, DateTime lastWriteTime) => new()
    {
        Kind = WorkResultKind.Compressed, EntryName = entryName, Compressed = data, LastWriteTime = lastWriteTime,
    };

    public static WorkResult ForTempFileCompressed(
        string entryName, string tempFilePath, uint crc32, long compressedSize, long uncompressedSize,
        ushort method, DateTime lastWriteTime) => new()
    {
        Kind = WorkResultKind.TempFileCompressed, EntryName = entryName, TempFilePath = tempFilePath,
        Crc32 = crc32, CompressedSize = compressedSize, UncompressedSize = uncompressedSize,
        Method = method, LastWriteTime = lastWriteTime,
    };

    public static WorkResult ForDirectoryPlaceholder(string entryName, DateTime lastWriteTime) => new()
    {
        Kind = WorkResultKind.DirectoryPlaceholder, EntryName = entryName, LastWriteTime = lastWriteTime,
    };

    public static WorkResult ForError(string sourcePath, string message, Exception? exception) => new()
    {
        Kind = WorkResultKind.Error, SourcePath = sourcePath, ErrorMessage = message, ErrorException = exception,
    };
}
