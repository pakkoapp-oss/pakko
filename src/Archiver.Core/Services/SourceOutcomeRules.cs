using Archiver.Core.IO;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

// T-F260: the one rule every engine uses to turn "what happened to this source" into a
// SourceResult, so "may this source be deleted" is decided in Core, not re-derived per frontend.
internal static class SourceOutcomeRules
{
    internal static SourceResult Classify(string path, bool produced, bool clean) => new()
    {
        Path = path,
        Outcome = (produced, clean) switch
        {
            (false, _) => SourceOutcome.NotProcessed,
            (true, true) => SourceOutcome.Completed,
            (true, false) => SourceOutcome.Partial,
        },
    };

    // A creation source that contains one of the archives it produced (destination folder inside
    // the source folder) must never be deleted — that would take the new archive with it.
    // T-F419: also when the archive got there by another spelling (a junction, \\?\, SUBST, the
    // admin share), compared by file identity; a side that exists but cannot be resolved counts as holding it.
    // A drive or share root is never deletable either: its Hidden+System entries are left out
    // (DirectoryWalker, T-F345), so it is never wholly in the archive.
    internal static IReadOnlyList<SourceResult> DowngradeSourcesContainingOutputs(
        IEnumerable<SourceResult> sources, IReadOnlyCollection<string> createdFiles)
    {
        List<HashSet<PathIdentity.Identity>?> holders = [.. createdFiles.Where(File.Exists).Select(PathIdentity.SelfAndAncestors)];
        return [.. sources.Select(s => s.Outcome == SourceOutcome.Completed
                && (IsRoot(s.Path) || createdFiles.Any(c => IsSameOrUnder(c, s.Path)) || HoldsAnOutput(s.Path, holders))
            ? s with { Outcome = SourceOutcome.Partial }
            : s)];
    }

    // T-F420: an extraction's archives, in order; one whose entry a later entry replaced is not
    // wholly on disk (ExtractionNameRegistry), so deleting it would lose that entry.
    internal static List<SourceResult> DowngradeNotWhollyOnDisk(List<SourceResult> sources, ExtractionNameRegistry names) =>
        [.. sources.Select((s, i) => s.Outcome == SourceOutcome.Completed && names.NotWhollyOnDisk(i)
            ? s with { Outcome = SourceOutcome.Partial }
            : s)];

    private static bool HoldsAnOutput(string source, List<HashSet<PathIdentity.Identity>?> holders)
    {
        if (holders.Count == 0 || (!File.Exists(source) && !Directory.Exists(source)))
            return false;
        if (PathIdentity.Resolve(source) is not { } resolved)
            return true;
        return holders.Exists(h => h is null || h.Contains(resolved.Identity));
    }

    private static bool IsRoot(string path) => Directory.Exists(path) && new DirectoryInfo(path).Parent is null;

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
