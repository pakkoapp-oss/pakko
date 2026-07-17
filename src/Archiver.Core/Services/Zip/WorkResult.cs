namespace Archiver.Core.Services.Zip;

internal enum WorkResultKind { Compressed, LargePassthrough, DirectoryPlaceholder, Error }

/// <summary>
/// Outcome of processing one <see cref="FileWorkItem"/>, produced by a (possibly parallel)
/// compression worker and consumed strictly in enqueue order by the single writer thread — see
/// <see cref="ParallelSingleArchiveWriter"/>. Reparse-point skips are reported directly during
/// enumeration (never dispatched as work at all), so there is no "Skipped" case here.
/// </summary>
internal sealed record WorkResult
{
    public required WorkResultKind Kind { get; init; }
    public string EntryName { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public DateTime LastWriteTime { get; init; }
    public CompressedEntryData Compressed { get; init; }
    public long UncompressedLengthHint { get; init; }
    public string? ErrorMessage { get; init; }
    public Exception? ErrorException { get; init; }

    public static WorkResult ForCompressed(string entryName, CompressedEntryData data, DateTime lastWriteTime) => new()
    {
        Kind = WorkResultKind.Compressed, EntryName = entryName, Compressed = data, LastWriteTime = lastWriteTime,
    };

    public static WorkResult ForLargePassthrough(string sourcePath, string entryName, long uncompressedLengthHint, DateTime lastWriteTime) => new()
    {
        Kind = WorkResultKind.LargePassthrough, EntryName = entryName, SourcePath = sourcePath,
        UncompressedLengthHint = uncompressedLengthHint, LastWriteTime = lastWriteTime,
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
