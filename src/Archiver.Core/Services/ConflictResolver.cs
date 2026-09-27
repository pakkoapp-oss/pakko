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

    // incomingSize/incomingModified describe the file that would replace existingPath, for a
    // prompt that compares both (T-F268); null when there is no such file yet (a new archive).
    public async Task<ConflictBehavior> ResolveAsync(string existingPath, long? incomingSize = null, DateTimeOffset? incomingModified = null)
    {
        if (configured != ConflictBehavior.Ask)
            return configured;

        if (_sticky is { } sticky)
            return Map(sticky);

        if (resolveConflictAsync is null)
            return ConflictBehavior.Skip; // Shell / no UI wired — safest non-destructive default

        ConflictDecision decision = await resolveConflictAsync(new ConflictInfo { ExistingPath = existingPath, IncomingSize = incomingSize, IncomingModified = incomingModified })
            .ConfigureAwait(false);

        if (decision.ApplyToAll)
            _sticky = decision.Resolution;

        return Map(decision.Resolution);
    }

    private static ConflictBehavior Map(ConflictResolution resolution) => resolution switch
    {
        ConflictResolution.Overwrite => ConflictBehavior.Overwrite,
        ConflictResolution.Rename => ConflictBehavior.Rename,
        _ => ConflictBehavior.Skip
    };
}
