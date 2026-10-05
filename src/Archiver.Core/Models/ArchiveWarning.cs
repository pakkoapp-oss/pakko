namespace Archiver.Core.Models;

/// <summary>
/// Something the user should know about an operation that still did what was asked (T-F280): the
/// source was processed and nothing was left out. Unlike an <see cref="ArchiveError"/> it does not
/// make the operation fail, and unlike a <see cref="SkippedFile"/> nothing was skipped.
/// </summary>
public sealed record ArchiveWarning
{
    /// <summary>The archive or source the warning is about.</summary>
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>English text of <see cref="Text"/>; for logs and the English-only CLI.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>The message as a code a frontend renders in the user's language (T-F209).</summary>
    public CoreText? Text { get; init; }
}
