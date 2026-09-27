namespace Archiver.Core.Models;

public sealed record ConflictInfo
{
    public required string ExistingPath { get; init; }

    /// <summary>Size of the file that would replace <see cref="ExistingPath"/>; null when not known yet (a new archive).</summary>
    public long? IncomingSize { get; init; }

    /// <summary>Last-modified time of the incoming file; null when not known yet (a new archive).</summary>
    public DateTimeOffset? IncomingModified { get; init; }
}
