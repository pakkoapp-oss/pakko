using Archiver.Core.Models;

namespace Archiver.Shell;

// T-F155: bridges a scope gap between Archiver.Core's own ConflictResolver (T-F06) and Archiver.
// Shell's per-archive extraction loop. ConflictResolver's "apply to all" only lasts for one
// ExtractAsync call, but RunExtractHereAsync/RunExtractHereFlatAsync construct a fresh
// ExtractOptions (and therefore a fresh Core-side ConflictResolver) once PER ARCHIVE inside a
// foreach -- so a raw wire-through would silently re-prompt after every archive in an Explorer
// multi-select even with the box checked. One instance of this wrapper is constructed ONCE before
// that foreach and reused across every archive, so "apply to all" spans the whole selection,
// matching Explorer's own multi-file-conflict UX.
public sealed class StickyApplyToAllConflictResolver(Func<ConflictInfo, Task<ConflictDecision>> inner)
{
    private ConflictDecision? _sticky;

    public async Task<ConflictDecision> ResolveAsync(ConflictInfo conflict)
    {
        if (_sticky is { } sticky)
            return sticky;

        var decision = await inner(conflict).ConfigureAwait(false);
        if (decision.ApplyToAll)
            _sticky = decision;

        return decision;
    }
}
