using System.IO.Compression;

namespace Archiver.Core.Models;

public sealed record ArchiveOptions
{
    public IReadOnlyList<string> SourcePaths { get; init; } = [];
    public string DestinationFolder { get; init; } = string.Empty;
    /// <summary>Null auto-names the archive from <see cref="SourcePaths"/> (see ArchiveNaming.ResolveSingleArchiveName).</summary>
    public string? ArchiveName { get; init; }
    public ArchiveMode Mode { get; init; } = ArchiveMode.SingleArchive;
    public ConflictBehavior OnConflict { get; init; } = ConflictBehavior.Skip;
    public bool OpenDestinationFolder { get; init; } = false;
    public bool DeleteSourceFiles { get; init; } = false;
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.Optimal;
    public ArchiveContainerFormat Format { get; init; } = ArchiveContainerFormat.Zip;

    /// <summary>
    /// T-F06: invoked once per conflicting destination path when <see cref="OnConflict"/> is
    /// <see cref="ConflictBehavior.Ask"/>. Null (e.g. Archiver.Shell, or a test that doesn't wire
    /// it) falls back to Skip — see ConflictResolver.
    /// </summary>
    public Func<ConflictInfo, Task<ConflictDecision>>? ResolveConflictAsync { get; init; }
}

/// <summary>Whether multiple source items produce one archive or one archive each.</summary>
public enum ArchiveMode
{
    /// <summary>All sources go into a single archive.</summary>
    SingleArchive,

    /// <summary>Each source item gets its own archive.</summary>
    SeparateArchives
}

/// <summary>How a destination conflict is resolved.</summary>
public enum ConflictBehavior
{
    /// <summary>Overwrite the existing item.</summary>
    Overwrite,

    /// <summary>Leave the existing item untouched.</summary>
    Skip,

    /// <summary>
    /// On extraction: renames per-file inside a merged existing folder — this does NOT mean
    /// "always create a fresh whole folder." For shell-only "always fresh" behavior, use
    /// ExtractOptions.SeparateFolderName instead.
    /// </summary>
    Rename,

    /// <summary>Ask the caller via <see cref="ArchiveOptions.ResolveConflictAsync"/>/ExtractOptions.ResolveConflictAsync.</summary>
    Ask
}
