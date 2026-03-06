namespace Archiver.Core.Models;

public sealed record ArchiveError
{
    public string SourcePath { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
}
