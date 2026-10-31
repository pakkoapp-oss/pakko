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

    // T-F434: several sources in one archive. A skip keeps the source it lies in, by full path; an
    // error or a filter keeps every source (they are not attributed). Fail-closed: a skip that no
    // source holds, or a path that is not fully qualified, keeps every source - a relative path would
    // be placed by the current folder (pakko a out.zip . other), and another spelling (\\?\) by no one.
    // Both sides go through GetFullPath only once they are fully qualified, where it needs no current
    // folder: ZIP writes a skip as FileSystemInfo.FullName, so "C:/w/a" or "C:\w\a." must still hold it.
    // Safe only while every creation skip is a link or an empty root (both deleters keep links): a
    // future skip that stands for lost data must keep this attribution exact or become an error.
    internal static IReadOnlyList<SourceResult> ClassifyShared(
        IReadOnlyList<string> sourcePaths, bool produced, bool errorsOrFilters, IReadOnlyList<SkippedFile> skips)
    {
        if (!produced || errorsOrFilters || skips.Count == 0)
            return [.. sourcePaths.Select(p => Classify(p, produced, clean: !errorsOrFilters && skips.Count == 0))];

        var kept = new HashSet<string>(StringComparer.Ordinal);
        foreach (SkippedFile skip in skips)
        {
            string[] holders = IsLiteralFullPath(skip.Path) && sourcePaths.All(IsLiteralFullPath)
                ? [.. sourcePaths.Where(source => IsSameOrUnderLiteral(Path.GetFullPath(skip.Path), Path.GetFullPath(source)))]
                : [];
            if (holders.Length == 0)
                return [.. sourcePaths.Select(p => Classify(p, produced: true, clean: false))];
            kept.UnionWith(holders);
        }
        return [.. sourcePaths.Select(p => Classify(p, produced: true, clean: !kept.Contains(p)))];
    }

    // Fully qualified and with no "." or ".." part: compared as written, "C:\w\b\..\a\x" would sit
    // under b while it names a file of a.
    private static bool IsLiteralFullPath(string path) =>
        Path.IsPathFullyQualified(path) && !path.Split('\\', '/').Any(part => part is "." or "..");

    // As IsSameOrUnder, but on the paths as written: nothing is resolved against the current folder.
    private static bool IsSameOrUnderLiteral(string candidate, string root)
    {
        string rootTrimmed = Path.TrimEndingDirectorySeparator(root);
        string candidateTrimmed = Path.TrimEndingDirectorySeparator(candidate);
        string prefix = Path.EndsInDirectorySeparator(rootTrimmed) ? rootTrimmed : rootTrimmed + Path.DirectorySeparatorChar;
        return candidateTrimmed.Equals(rootTrimmed, StringComparison.OrdinalIgnoreCase)
            || candidateTrimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

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
        return [.. sources.Select(s => s.Outcome != SourceOutcome.Completed ? s
            : IsRoot(s.Path) ? s with { Outcome = SourceOutcome.Partial, Cause = PartialCause.DriveRoot }
            : createdFiles.Any(c => IsSameOrUnder(c, s.Path)) || HoldsAnOutput(s.Path, holders)
                ? s with { Outcome = SourceOutcome.Partial, Cause = PartialCause.HoldsAnOutput }
            : s)];
    }

    // T-F420: an extraction's archives; one whose entry a later entry replaced is not
    // wholly on disk (ExtractionNameRegistry), so deleting it would lose that entry.
    internal static List<SourceResult> DowngradeNotWhollyOnDisk(List<SourceResult> sources, ExtractionNameRegistry names) =>
        [.. sources.Select(s => s.Outcome == SourceOutcome.Completed && names.NotWhollyOnDisk(s.Path)
            ? s with { Outcome = SourceOutcome.Partial, Cause = PartialCause.EntryReplaced }
            : s)];

    private static bool HoldsAnOutput(string source, List<HashSet<PathIdentity.Identity>?> holders)
    {
        if (holders.Count == 0 || (!File.Exists(source) && !Directory.Exists(source)))
            return false;
        if (PathIdentity.Resolve(source) is not { } resolved)
            return true;
        return holders.Exists(h => h is null || h.Contains(resolved.Id));
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
