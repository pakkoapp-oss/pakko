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

    // T-F94: invoked when an archive's declared uncompressed size vs. its compressed size looks
    // like a decompression bomb AND the destination has enough free space to hold it — returning
    // true proceeds with extraction, false declines (archive is skipped). Null (the default)
    // auto-declines, preserving the pre-T-F94 safe behavior for callers that don't wire a
    // callback (Archiver.Shell, and any test that doesn't set this). See ArchiveEntrySecurity's
    // EvaluateCompressionBombAsync and DECISIONS.md's T-F94 entry.
    public Func<CompressionBombWarning, Task<bool>>? ConfirmCompressionBombExtraction { get; init; }

    // T-F05: non-null/non-empty restricts extraction to just these archive-internal entry paths
    // ('/'-separated, matching ArchiveEntryInfo.Path) instead of every entry. Only meaningful when
    // ArchivePaths has exactly one entry — "Extract selected" from the archive browser always
    // targets the single archive currently open in that view. A selected directory path implies
    // its full nested contents. Null/empty (the default) extracts everything, unaffected.
    public IReadOnlyList<string>? SelectedEntryPaths { get; init; }

    // T-F06: invoked once per conflicting entry when OnConflict == Ask. Null (e.g.
    // Archiver.Shell, or a test that doesn't wire it) falls back to Skip — see ConflictResolver.
    public Func<ConflictInfo, Task<ConflictDecision>>? ResolveConflictAsync { get; init; }
}

public enum ExtractMode
{
    SeparateFolders,    // each archive → own subfolder
    SingleFolder        // all archives → one flat folder
}
