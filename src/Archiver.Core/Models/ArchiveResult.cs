namespace Archiver.Core.Models;

public sealed record ArchiveResult
{
    public bool Success { get; init; }
    public IReadOnlyList<string> CreatedFiles { get; init; } = [];
    public IReadOnlyList<ArchiveError> Errors { get; init; } = [];
    public IReadOnlyList<SkippedFile> SkippedFiles { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
