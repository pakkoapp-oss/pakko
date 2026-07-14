namespace Archiver.Core.Models;

public sealed record ConflictInfo
{
    public required string ExistingPath { get; init; }
}
