namespace Archiver.Core.Models;

/// <summary>How completely one source (an archive being extracted, or a file/folder being
/// archived) was processed by an operation (T-F260).</summary>
public enum SourceOutcome
{
    /// <summary>Everything from this source was processed: nothing skipped, nothing failed, and
    /// its output exists. The only outcome that allows "Delete after operation".</summary>
    Completed,

    /// <summary>Some output was produced, but something from this source was skipped or failed,
    /// or only a subset of it was requested.</summary>
    Partial,

    /// <summary>No output was produced from this source.</summary>
    NotProcessed,
}

/// <summary>Why a source whose output is complete is still <see cref="SourceOutcome.Partial"/>
/// (T-F435): nothing was skipped or failed, so the summary shows no reason of its own.</summary>
public enum PartialCause
{
    /// <summary>A creation source holds one of the archives it produced.</summary>
    HoldsAnOutput,

    /// <summary>A creation source is a drive or share root, whose hidden system entries are left out.</summary>
    DriveRoot,

    /// <summary>An extracted archive's entry was replaced on disk by a later entry of the same name.</summary>
    EntryReplaced,
}

/// <summary>The outcome of one source path of an operation (T-F260).</summary>
public sealed record SourceResult
{
    /// <summary>The source path as the engine processed it (trailing separators trimmed).</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>How completely this source was processed.</summary>
    public SourceOutcome Outcome { get; init; }

    /// <summary>Set only when a rule after the run made the source <see cref="SourceOutcome.Partial"/>;
    /// null when a skip, an error, a filter or a subset did (the summary names those).</summary>
    public PartialCause? Cause { get; init; }
}
