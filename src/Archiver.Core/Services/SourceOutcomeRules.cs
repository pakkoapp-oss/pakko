using Archiver.Core.Models;

namespace Archiver.Core.Services;

// T-F260: the one rule every engine uses to turn "what happened to this source" into a
// SourceResult, so "may this source be deleted" is decided in Core, not re-derived per frontend.
internal static class SourceOutcomeRules
{
    internal static SourceResult Classify(string path, bool produced, bool clean) => new()
    {
        Path = path,
        Outcome = !produced ? SourceOutcome.NotProcessed
            : clean ? SourceOutcome.Completed
            : SourceOutcome.Partial,
    };

    // A creation source that contains one of the archives it produced (destination folder inside
    // the source folder) must never be deleted — that would take the new archive with it.
    internal static IReadOnlyList<SourceResult> DowngradeSourcesContainingOutputs(
        IEnumerable<SourceResult> sources, IReadOnlyCollection<string> createdFiles) =>
        [.. sources.Select(s => s.Outcome == SourceOutcome.Completed && createdFiles.Any(c => IsSameOrUnder(c, s.Path))
            ? s with { Outcome = SourceOutcome.Partial }
            : s)];

    private static bool IsSameOrUnder(string candidate, string root)
    {
        string full = Path.GetFullPath(candidate);
        string rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        // A drive root ("C:\") keeps its separator after trimming — don't append a second one.
        string prefix = Path.EndsInDirectorySeparator(rootFull) ? rootFull : rootFull + Path.DirectorySeparatorChar;
        return full.Equals(rootFull, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
