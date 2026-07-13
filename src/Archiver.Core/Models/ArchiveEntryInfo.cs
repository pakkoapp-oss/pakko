namespace Archiver.Core.Models;

/// <summary>
/// One entry inside an archive, as reported by IArchiveService/ITarService's ListEntriesAsync.
/// Flat — Path is the full archive-internal path ('/'-separated, no leading slash). Building a
/// folder hierarchy out of these is an App-layer concern (Archiver.Core has zero WinUI/UI-model
/// references), not Core's.
/// </summary>
public sealed record ArchiveEntryInfo
{
    public required string Path { get; init; }
    public long Size { get; init; }
    public long CompressedSize { get; init; }

    // Null when not reliably derivable — tar-family listing leaves this null rather than parsing
    // tar -tvf's date column, which was observed locale-mangled on a non-English-locale machine
    // (see TarProcessService's ScanForUnsafeEntriesAsync comments and DECISIONS.md's T-F84 entry).
    public DateTime? Modified { get; init; }

    public bool IsDirectory { get; init; }
}
