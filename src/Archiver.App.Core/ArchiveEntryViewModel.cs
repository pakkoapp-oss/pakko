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

    // Segoe MDL2 Assets glyphs (folder / page) — \uXXXX escapes only, never a literal
    // non-ASCII character in source (CONVENTIONS.md; this has shipped as a mojibake bug three
    // times already in this repo per CLAUDE.md's feedback note).
    public string Icon => IsFolder ? "\uE8B7" : "\uE7C3";

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
    };
}
