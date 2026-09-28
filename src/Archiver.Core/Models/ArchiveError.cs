namespace Archiver.Core.Models;

public sealed record ArchiveError
{
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>English text of <see cref="Text"/>; for logs and the English-only CLI.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>The message as a code a frontend renders in the user's language (T-F209). Null
    /// only for an error built outside Core; render <see cref="Message"/> then.</summary>
    public CoreText? Text { get; init; }

    public Exception? Exception { get; init; }
}
