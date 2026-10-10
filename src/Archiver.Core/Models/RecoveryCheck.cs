namespace Archiver.Core.Models;

/// <summary>What a PAR2 set says about the archive it protects (T-F275).</summary>
public enum RecoveryState
{
    /// <summary>Length, every block and the file MD5 match the set.</summary>
    Intact,

    /// <summary>Damaged, and the recovery blocks found can rebuild it.</summary>
    Repairable,

    /// <summary>Damaged beyond what the recovery blocks found can rebuild, or missing without enough of them.</summary>
    NotRepairable,

    /// <summary>Repairable in principle, but beyond the matrix size Pakko solves.</summary>
    RepairTooLarge,

    /// <summary>The archive tested as intact and the set disagrees with it: most likely a set left
    /// from an earlier version of the archive. A warning, not damage.</summary>
    DoesNotMatch,

    /// <summary>PAR2 files were found next to the archive, but no set in them can be read.</summary>
    Unusable,
}

/// <summary>
/// The PAR2 check of one archive during a test (T-F275 step 3), present only when PAR2 files were
/// found for it. Damage is also an <see cref="ArchiveError"/> in the same result and a set that
/// does not match or cannot be read an <see cref="ArchiveWarning"/>, so a frontend that ignores
/// this list still reports the outcome.
/// </summary>
public sealed record RecoveryCheck
{
    /// <summary>The archive the set protects — the path the user gave, or the file next to a
    /// <c>.par2</c> the user gave; never a name taken from inside the set.</summary>
    public required string ArchivePath { get; init; }

    public required RecoveryState State { get; init; }

    /// <summary>The PAR2 files the set was read from.</summary>
    public IReadOnlyList<string> SetFiles { get; init; } = [];

    /// <summary>The archive's blocks (PAR2 slices) in the set; 0 when <see cref="State"/> is <see cref="RecoveryState.Unusable"/>.</summary>
    public int Blocks { get; init; }

    public int DamagedBlocks { get; init; }

    /// <summary>The recovery blocks found in the set's files.</summary>
    public int RecoveryBlocks { get; init; }

    /// <summary>The verdict in words when <see cref="State"/> is <see cref="RecoveryState.Intact"/>,
    /// the one state that is neither an error nor a warning in the result; null otherwise.</summary>
    public CoreText? Text { get; init; }
}
