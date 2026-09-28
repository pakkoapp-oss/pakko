namespace Archiver.Core.Models;

public sealed record ArchiveResult
{
    /// <summary>No errors — the same as <see cref="Outcome"/> not being <see cref="OperationOutcome.Failed"/>.</summary>
    public bool Success => Errors.Count == 0;

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

    /// <summary>What the operation achieved (T-F260): errors win, then whether anything at all was
    /// done — an output was created or a source was processed — decides between skips and nothing.</summary>
    public OperationOutcome Outcome
    {
        get
        {
            if (Errors.Count > 0)
                return OperationOutcome.Failed;
            if (SkippedFiles.Count == 0)
                return OperationOutcome.Completed;
            bool anythingDone = CreatedFiles.Count > 0 || Sources.Any(s => s.Outcome != SourceOutcome.NotProcessed);
            return anythingDone ? OperationOutcome.CompletedWithSkips : OperationOutcome.NothingDone;
        }
    }
}
