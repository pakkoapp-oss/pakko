namespace Archiver.Core.Models;

/// <summary>What to repair from PAR2 recovery data (T-F275 step 4).</summary>
public sealed record RepairOptions
{
    /// <summary>Archives, or <c>.par2</c> files standing for the archive their set protects.</summary>
    public required IReadOnlyList<string> Paths { get; init; }

    /// <summary>Where the repaired copies go; null puts each next to its archive. Created when
    /// it does not exist.</summary>
    public string? OutputDirectory { get; init; }

    /// <summary>The user's choice to carry the "downloaded from the internet" mark over to the
    /// repaired copy; a policy that enforces the mark overrides a false here
    /// (<see cref="GroupPolicyOptions.EffectiveMotwMode"/>).</summary>
    public bool ApplyDownloadMark { get; init; } = true;
}
