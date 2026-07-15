using System.IO.Compression;

namespace Archiver.Core.Models;

public sealed record ArchiveOptions
{
    public IReadOnlyList<string> SourcePaths { get; init; } = [];
    public string DestinationFolder { get; init; } = string.Empty;
    public string? ArchiveName { get; init; }              // null = auto-name from source
    public ArchiveMode Mode { get; init; } = ArchiveMode.SingleArchive;
    public ConflictBehavior OnConflict { get; init; } = ConflictBehavior.Skip;
    public bool OpenDestinationFolder { get; init; } = false;
    public bool DeleteSourceFiles { get; init; } = false;
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.Optimal;
    public ArchiveContainerFormat Format { get; init; } = ArchiveContainerFormat.Zip;

    // T-F06: invoked once per conflicting destination path when OnConflict == Ask. Null (e.g.
    // Archiver.Shell, or a test that doesn't wire it) falls back to Skip — see ConflictResolver.
    public Func<ConflictInfo, Task<ConflictDecision>>? ResolveConflictAsync { get; init; }
}

public enum ArchiveMode
{
    SingleArchive,      // all sources → one .zip
    SeparateArchives    // one .zip per source item
}

public enum ConflictBehavior
{
    Overwrite,
    Skip,
    Rename,
    Ask
}
