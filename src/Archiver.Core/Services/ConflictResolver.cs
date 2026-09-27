using Archiver.Core.Models;

namespace Archiver.Core.Services;

// T-F06: resolves ConflictBehavior.Ask into a concrete Skip/Overwrite/Rename decision by invoking
// the caller's ResolveConflictAsync callback, remembering an "apply to all" choice for the
// remainder of this instance's lifetime. One instance is constructed per ArchiveAsync/ExtractAsync
// call (before any loop), so "apply to all" spans every archive/entry in that one call — not just
// the current archive. Returning ConflictBehavior (not ConflictResolution) lets every existing
// call site keep its Skip/Overwrite/Rename switch/if branches completely unchanged; only the
// switched-on expression changes.
internal sealed class ConflictResolver(
    ConflictBehavior configured,
    Func<ConflictInfo, Task<ConflictDecision>>? resolveConflictAsync)
{
    private ConflictResolution? _sticky;

    // T-F216: how many conflicts the user answered Skip (directly or through "apply to all") —
    // their own decision, which the engines neither list nor warn about, yet still count against
    // a source being fully processed. The no-callback default Skip is not counted.
    public int UserSkipCount { get; private set; }

    // incomingSize/incomingModified describe the file that would replace existingPath, for a
    // prompt that compares both (T-F268); null when there is no such file yet (a new archive).
    public async Task<ConflictBehavior> ResolveAsync(string existingPath, long? incomingSize = null, DateTimeOffset? incomingModified = null)
    {
        if (configured != ConflictBehavior.Ask)
            return configured;

        if (_sticky is { } sticky)
            return CountUserSkip(Map(sticky));

        if (resolveConflictAsync is null)
            return ConflictBehavior.Skip; // Shell / no UI wired — safest non-destructive default

        ConflictDecision decision = await resolveConflictAsync(new ConflictInfo { ExistingPath = existingPath, IncomingSize = incomingSize, IncomingModified = incomingModified })
            .ConfigureAwait(false);

        if (decision.ApplyToAll)
            _sticky = decision.Resolution;

        return CountUserSkip(Map(decision.Resolution));
    }

    private ConflictBehavior CountUserSkip(ConflictBehavior behavior)
    {
        if (behavior == ConflictBehavior.Skip)
            UserSkipCount++;
        return behavior;
    }

    private static ConflictBehavior Map(ConflictResolution resolution) => resolution switch
    {
        ConflictResolution.Overwrite => ConflictBehavior.Overwrite,
        ConflictResolution.Rename => ConflictBehavior.Rename,
        _ => ConflictBehavior.Skip
    };
}
