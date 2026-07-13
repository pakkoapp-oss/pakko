namespace Archiver.Core.Models;

/// <summary>
/// Result of listing an archive's entries. Never throws to callers — a failure to list
/// (corrupted archive, tar.exe missing, unsupported format) is reported via Success/ErrorMessage,
/// matching every other IArchiveService/ITarService method's "never throw" convention.
/// </summary>
public sealed record ArchiveListResult
{
    public bool Success { get; init; }
    public IReadOnlyList<ArchiveEntryInfo> Entries { get; init; } = [];
    public string? ErrorMessage { get; init; }
}
