namespace Archiver.App.Core;

/// <summary>
/// One entry in the archive browser's current folder view — a path inside an archive, distinct
/// from Archiver.App.Models.FileItem (a top-level pending-selection path queued for an
/// Archive/Extract operation). Plain, no WinUI dependency — lives here so ArchiveTreeIndex stays
/// unit-testable without a WinUI test host.
/// </summary>
public sealed record ArchiveEntryViewModel
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public required bool IsFolder { get; init; }
    public long Size { get; init; }
    public long CompressedSize { get; init; }
    public uint? Crc32 { get; init; }
    public DateTime? Modified { get; init; }

    public string ModifiedDisplay => Modified?.ToString("yyyy-MM-dd HH:mm") ?? "—";
    public string SizeDisplay => IsFolder ? string.Empty : FormatSize(Size);

    // CompressedSize is 0 for every tar-routed format (TarProcessService's listing never
    // populates it — the underlying gzip/xz/etc. stream is whole-archive, not per-entry) so this
    // column reads blank for RAR/7z/tar.* and only ever shows a real value for ZIP.
    public string CompressedSizeDisplay => IsFolder || CompressedSize <= 0 ? string.Empty : FormatSize(CompressedSize);

    // Crc32 is null (not 0) for folders and every tar-routed format — 0 is a legitimate CRC-32
    // for an empty file, so it can't double as a "not available" sentinel the way CompressedSize's
    // <= 0 guard does.
    public string CrcDisplay => IsFolder || Crc32 is null ? string.Empty : $"{Crc32:X8}";

    // T-F98: set by MainViewModel.RefreshCurrentFolder for a nested-archive row only, when
    // drilling into it would exceed NestedArchivePolicy.MaxDepth — the one case where
    // double-click on an archive entry does NOT transparently drill in. False for every other
    // row (folders, plain files, and archives still within the depth limit).
    public bool NestedDepthLimitReached { get; init; }

    // T-F110: a file's icon is a heads-up for what double-click does. A recognized archive
    // entry drills in transparently (T-F98) so it gets View (eye) like a preview does, UNLESS
    // drilling in would exceed the depth limit — that case is blocked, so it gets Hide
    // (crossed-out eye) instead. A non-archive file gets View for a PreviewPolicy-allowlisted
    // type (silent preview), Hide for anything else (confirm-and-extract-next-to-archive,
    // T-F109). Purely informational: the confirm dialog itself stays the real gate for
    // non-allowlisted types (see SECURITY.md/DECISIONS.md's T-F109 entry) — an icon alone
    // isn't a substitute for a synchronous checkpoint a double-click can't skip past.
    // Segoe MDL2 Assets glyphs (folder / view / hide) — \uXXXX escapes only, never a literal
    // non-ASCII character in source (CONVENTIONS.md; this has shipped as a mojibake bug three
    // times already in this repo per CLAUDE.md's feedback note).
    public string Icon => IsFolder
        ? "\uE8B7"
        : Archiver.Core.Services.ArchiveFormatDetector.IsRecognizedArchiveExtension(Name)
            ? (NestedDepthLimitReached ? "\uED1A" : "\uE890")
            : (Archiver.Core.Services.PreviewPolicy.IsPreviewable(Name) ? "\uE890" : "\uED1A");

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
    };
}
