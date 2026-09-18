using Archiver.Core.Models;

namespace Archiver.Shell;

// T-F192: bridges the same scope gap StickyApplyToAllConflictResolver (T-F155) already bridges
// for conflicts. Core's own internal PasswordResolver (T-F189) remembers ApplyToRemaining only
// for the lifetime of ONE ExtractAsync call, but RunExtractHereAsync/RunExtractHereFlatAsync/
// RunExtractFolderAsync construct a fresh ExtractOptions (and therefore a fresh Core-side
// PasswordResolver) once PER ARCHIVE inside a foreach — so a raw wire-through would silently
// re-prompt after every archive in an Explorer multi-select even with the box checked. One
// instance of this wrapper is constructed ONCE before that foreach and reused across every
// archive, so "apply to remaining" spans the whole Explorer selection.
//
// Widening this scope has the same accepted tradeoff Core's own PasswordResolver._sticky already
// documents for a single ExtractAsync call: once a password is accepted with ApplyToRemaining
// set, every later archive in the selection skips its own verify() and trusts the sticky
// password — a differently-keyed later archive then surfaces as normal per-entry "wrong password"
// ArchiveErrors in that archive's own extraction, not a re-prompt. This wrapper just extends that
// existing, already-accepted behavior across archive boundaries instead of only within one.
public sealed class StickyPasswordResolver(Func<PasswordPromptInfo, bool, Task<PasswordDecision>> inner, bool canApplyToRemaining)
{
    private PasswordDecision? _sticky;

    public async Task<PasswordDecision> ResolveAsync(PasswordPromptInfo info)
    {
        if (_sticky is { } sticky)
            return sticky;

        var decision = await inner(info, canApplyToRemaining).ConfigureAwait(false);
        if (decision.ApplyToRemaining)
            _sticky = decision;

        return decision;
    }
}
