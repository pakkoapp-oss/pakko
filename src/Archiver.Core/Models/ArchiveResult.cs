namespace Archiver.Core.Models;

public sealed record ArchiveResult
{
    public bool Success { get; init; }
    public IReadOnlyList<string> CreatedFiles { get; init; } = [];
    public IReadOnlyList<ArchiveError> Errors { get; init; } = [];
    public IReadOnlyList<SkippedFile> SkippedFiles { get; init; } = [];

    /// <summary>One entry per source the engine finished looking at (T-F260). A source with no
    /// entry here was not processed — the list is fail-closed by construction.</summary>
    public IReadOnlyList<SourceResult> Sources { get; init; } = [];

    /// <summary>Sources that may be deleted after the operation: exactly those whose
    /// <see cref="SourceResult.Outcome"/> is <see cref="SourceOutcome.Completed"/>.</summary>
    public IEnumerable<string> FullyProcessedSources =>
        Sources.Where(s => s.Outcome == SourceOutcome.Completed).Select(s => s.Path);
}
