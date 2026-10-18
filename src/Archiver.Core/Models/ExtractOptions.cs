namespace Archiver.Core.Models;

public sealed record ExtractOptions
{
    public IReadOnlyList<string> ArchivePaths { get; init; } = [];
    public string DestinationFolder { get; init; } = string.Empty;
    public ExtractMode Mode { get; init; } = ExtractMode.SeparateFolders;

    /// <summary>
    /// Overrides the per-archive subfolder name that <see cref="ExtractMode.SeparateFolders"/>
    /// would otherwise derive from the archive's own file name. Only meaningful when
    /// <see cref="ArchivePaths"/> has exactly one entry — callers extracting multiple archives at
    /// once have no single name to override.
    /// </summary>
    public string? SeparateFolderName { get; init; }

    /// <summary>
    /// T-F205: in <see cref="ExtractMode.SingleFolder"/>, drop an archive's single root folder when
    /// it is named like the archive itself — NanaZip's "Extract to name\" (ElimDup, on by default
    /// there), so <c>name.zip</c> holding <c>name/...</c> does not become <c>name\name\...</c>.
    /// False (the default) keeps every root folder ("extract with full paths").
    /// </summary>
    public bool EliminateDuplicateRootFolder { get; init; }

    /// <summary>
    /// T-F411: drop an archive's single root folder when it has this name, without case (7-Zip's
    /// -spe, which takes the last part of -o). Decided over every entry of the archive before
    /// <see cref="SelectedEntryPaths"/> and <see cref="EntryFilter"/>, so a selected or filtered
    /// extraction drops it too; a root that is a file is not dropped. Null (the default) leaves
    /// <see cref="EliminateDuplicateRootFolder"/>'s rule, which compares with the archive's name.
    /// </summary>
    public string? DuplicateRootName { get; init; }

    public ConflictBehavior OnConflict { get; init; } = ConflictBehavior.Skip;
    public bool OpenDestinationFolder { get; init; } = false;

    /// <summary>
    /// T-F94: invoked when an archive's declared uncompressed size vs. its compressed size looks
    /// like a decompression bomb AND the destination has enough free space to hold it — returning
    /// true proceeds with extraction, false declines (archive is skipped). Null (the default)
    /// auto-declines, preserving the pre-T-F94 safe behavior for callers that don't wire a
    /// callback (Archiver.Shell, and any test that doesn't set this). See ArchiveEntrySecurity's
    /// EvaluateCompressionBombAsync and DECISIONS.md's T-F94 entry.
    /// </summary>
    public Func<CompressionBombWarning, Task<bool>>? ConfirmCompressionBombExtraction { get; init; }

    /// <summary>
    /// T-F05: non-null/non-empty restricts extraction to just these archive-internal entry paths
    /// ('/'-separated, matching ArchiveEntryInfo.Path) instead of every entry. Only meaningful when
    /// <see cref="ArchivePaths"/> has exactly one entry — "Extract selected" from the archive
    /// browser always targets the single archive currently open in that view. A selected
    /// directory path implies its full nested contents. Null/empty (the default) extracts
    /// everything, unaffected.
    /// </summary>
    public IReadOnlyList<string>? SelectedEntryPaths { get; init; }

    /// <summary>
    /// T-F06: invoked once per conflicting entry when <see cref="OnConflict"/> is
    /// <see cref="ConflictBehavior.Ask"/>. Null (e.g. Archiver.Shell, or a test that doesn't wire
    /// it) falls back to Skip — see ConflictResolver.
    /// </summary>
    public Func<ConflictInfo, Task<ConflictDecision>>? ResolveConflictAsync { get; init; }

    /// <summary>
    /// T-F189: invoked once per encrypted ZIP archive, before its entry loop runs, when the
    /// archive contains at least one encrypted entry. Null (e.g. Archiver.Shell/Archiver.CLI until
    /// T-F191/T-F192 ship, or a test that doesn't wire it) preserves the pre-T-F189 behavior
    /// exactly: the archive is rejected with "password-protected and cannot be extracted." Mirrors
    /// <see cref="Models.ArchiveOptions.ResolvePasswordAsync"/> (same field name/type on both
    /// records, added there too even though it has no caller yet — see the T-F157→T-F158
    /// retrofit precedent in docs/DECISIONS.md).
    /// </summary>
    public Func<PasswordPromptInfo, Task<PasswordDecision>>? ResolvePasswordAsync { get; init; }

    /// <summary>
    /// T-F360: false leaves the archive's download mark (Zone.Identifier) off the extracted files,
    /// the user's choice for one extraction. Default true. An EnforceMOTW Group Policy overrides
    /// it either way (<see cref="GroupPolicyOptions.EffectiveMotwMode"/>).
    /// </summary>
    public bool ApplyDownloadMark { get; init; } = true;

    /// <summary>
    /// T-F408: called once for each file and folder that arrived in the destination, with its full
    /// path; a folder's path ends with a directory separator. A file that was skipped, or could not
    /// be moved in, is not named. Called on the extraction's own thread, after the archive's
    /// entries are in place; an exception from it is the caller's defect and is not caught. Null
    /// (the default) names nothing.
    /// </summary>
    public Action<string>? OnItemExtracted { get; init; }

    /// <summary>
    /// T-F410: non-null extracts only the entries it returns true for, given the entry's name as
    /// the archive has it ('/'-separated, a folder's ending with '/') and whether it is a folder.
    /// Unlike <see cref="SelectedEntryPaths"/> a folder that passes does not bring its content:
    /// each entry is asked on its own. Both set: an entry must pass both. A filtered extraction is a
    /// subset (no root folder is collapsed, the archive is never fully processed), and when no entry
    /// passes nothing is created, not even the destination. Called on the extraction's own thread; an
    /// exception from it is not caught.
    /// </summary>
    public Func<string, bool, bool>? EntryFilter { get; init; }
}

/// <summary>How multiple archives being extracted at once land relative to each other.</summary>
public enum ExtractMode
{
    /// <summary>
    /// Each archive unconditionally gets its own destination subfolder — even a genuinely
    /// multi-root archive is wrapped (T-F156's deliberate exception for this mode only).
    /// </summary>
    SeparateFolders,

    /// <summary>
    /// All archives land in one flat destination folder with full paths: a multi-root archive is
    /// NOT wrapped in a subfolder (T-F156), and a single root folder is kept (T-F205), unless
    /// <see cref="ExtractOptions.EliminateDuplicateRootFolder"/> applies.
    /// </summary>
    SingleFolder
}
