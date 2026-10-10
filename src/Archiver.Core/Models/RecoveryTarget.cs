namespace Archiver.Core.Models;

/// <summary>The archive a PAR2 file protects, or why it cannot be said (T-F275 step 3c). Exactly
/// one of the two is set.</summary>
public sealed record RecoveryTarget
{
    /// <summary>The protected file, found next to the PAR2 file and confirmed by the set's own hashes.</summary>
    public string? ArchivePath { get; init; }

    /// <summary>The set cannot be read, protects a file that is not there, or policy refuses it.</summary>
    public ArchiveError? Error { get; init; }
}
