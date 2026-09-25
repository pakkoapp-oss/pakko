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

/// <summary>The outcome of one source path of an operation (T-F260).</summary>
public sealed record SourceResult
{
    /// <summary>The source path as the engine processed it (trailing separators trimmed).</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>How completely this source was processed.</summary>
    public SourceOutcome Outcome { get; init; }
}
