namespace Archiver.Core.Models;

public sealed record ArchiveResult
{
    /// <summary>No errors — the same as <see cref="Outcome"/> not being <see cref="OperationOutcome.Failed"/>.</summary>
    public bool Success => Errors.Count == 0;

    public IReadOnlyList<string> CreatedFiles { get; init; } = [];

    /// <summary>The PAR2 files written next to the archives in <see cref="CreatedFiles"/>
    /// (T-F275, <see cref="ArchiveOptions.RecoveryPercent"/>): an index and a volume per archive.</summary>
    public IReadOnlyList<string> RecoveryFiles { get; init; } = [];

    /// <summary>The PAR2 check of each tested archive that has a set (T-F275 step 3); filled only by
    /// <see cref="Interfaces.IExtractionRouter.TestAsync"/> with <c>verifyRecoveryData</c>.</summary>
    public IReadOnlyList<RecoveryCheck> RecoveryChecks { get; init; } = [];

    public IReadOnlyList<ArchiveError> Errors { get; init; } = [];
    public IReadOnlyList<SkippedFile> SkippedFiles { get; init; } = [];

    /// <summary>Entries not extracted because the file already existed and the conflict choice
    /// was to keep it (T-F313). The ZIP engine names them here and not in <see cref="SkippedFiles"/>,
    /// so they do not change <see cref="Outcome"/>; the tar engine lists the same case as a skip.</summary>
    public IReadOnlyList<SkippedFile> KeptExistingFiles { get; init; } = [];

    /// <summary>What the user should know although everything asked was done (T-F280). A warning
    /// never fails the operation and never makes a source undeletable.</summary>
    public IReadOnlyList<ArchiveWarning> Warnings { get; init; } = [];

    /// <summary>One entry per source the engine finished looking at (T-F260). A source with no
    /// entry here was not processed — the list is fail-closed by construction.</summary>
    public IReadOnlyList<SourceResult> Sources { get; init; } = [];

    /// <summary>Sources that may be deleted after the operation: exactly those whose
    /// <see cref="SourceResult.Outcome"/> is <see cref="SourceOutcome.Completed"/>.</summary>
    public IEnumerable<string> FullyProcessedSources =>
        Sources.Where(s => s.Outcome == SourceOutcome.Completed).Select(s => s.Path);

    /// <summary>What the operation achieved (T-F260): errors win, then whether anything at all was
    /// done — an output was created or a source was processed — decides between skips and nothing.
    /// Warnings only tell a clean run from one with something to show (T-F280).</summary>
    public OperationOutcome Outcome
    {
        get
        {
            if (Errors.Count > 0)
                return OperationOutcome.Failed;
            if (SkippedFiles.Count == 0)
                return Warnings.Count > 0 ? OperationOutcome.CompletedWithWarnings : OperationOutcome.Completed;
            bool anythingDone = CreatedFiles.Count > 0 || Sources.Any(s => s.Outcome != SourceOutcome.NotProcessed);
            return anythingDone ? OperationOutcome.CompletedWithSkips : OperationOutcome.NothingDone;
        }
    }
}
