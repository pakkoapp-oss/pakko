namespace Archiver.Core.Models;

// A *resolved* one-shot decision — deliberately has no Ask case, unlike ConflictBehavior (the
// configured policy). ConflictResolver maps this back onto ConflictBehavior for reuse in the
// existing Skip/Overwrite/Rename branches at each call site.
public enum ConflictResolution
{
    Skip,
    Overwrite,
    Rename
}

public sealed record ConflictDecision
{
    public required ConflictResolution Resolution { get; init; }

    // T-F06: when true, ConflictResolver remembers this Resolution for the remainder of the
    // current ArchiveAsync/ExtractAsync call, suppressing further ResolveConflictAsync calls.
    public bool ApplyToAll { get; init; }
}
