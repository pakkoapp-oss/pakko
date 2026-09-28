namespace Archiver.Core.Models;

public sealed record SkippedFile
{
    public string Path { get; init; } = string.Empty;

    /// <summary>English text of <see cref="Text"/>; for logs and the English-only CLI.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>The reason as a code a frontend renders in the user's language (T-F209). Null
    /// only for a skip built outside Core; render <see cref="Reason"/> then.</summary>
    public CoreText? Text { get; init; }
}
