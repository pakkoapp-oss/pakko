namespace Archiver.Core.Models;

public sealed record ExtractOptions
{
    public IReadOnlyList<string> ArchivePaths { get; init; } = [];
    public string DestinationFolder { get; init; } = string.Empty;
    public ExtractMode Mode { get; init; } = ExtractMode.SeparateFolders;

    // Overrides the per-archive subfolder name that Mode.SeparateFolders would otherwise
    // derive from the archive's own file name. Only meaningful when ArchivePaths has exactly
    // one entry — callers extracting multiple archives at once have no single name to override.
    public string? SeparateFolderName { get; init; }

    public ConflictBehavior OnConflict { get; init; } = ConflictBehavior.Skip;
    public bool OpenDestinationFolder { get; init; } = false;
    public bool DeleteArchiveAfterExtraction { get; init; } = false;
}

public enum ExtractMode
{
    SeparateFolders,    // each archive → own subfolder
    SingleFolder        // all archives → one flat folder
}
