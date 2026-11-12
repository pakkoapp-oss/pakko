using Archiver.Core.IO;
using Archiver.Core.Services;

namespace Archiver.CLI;

/// <summary>T-F414: one source of 'a', and how many of its last path parts name it in the archive.</summary>
public sealed record CliArchiveSource(string FullPath, int NameParts);

/// <summary>T-F414: a name given to 'a' that adds nothing, and why (it exists nowhere, or its folder cannot be read).</summary>
public sealed record CliSourceProblem(string Name, string Reason);

/// <summary>T-F414: what the names given to 'a' come to.</summary>
public sealed record CliArchiveSourceList(IReadOnlyList<CliArchiveSource> Sources, IReadOnlyList<CliSourceProblem> Problems);

/// <summary>
/// T-F414/T-F410: the sources of 'a', by 7-Zip's rules (7-Zip 26.02, Common/Wildcard.cpp,
/// <c>CCensor::AddItem</c> in its default relative-path mode, and UI/Common/EnumDirItems.cpp).
/// Each name splits into a prefix (a folder on disk) and a mask under it: a relative name has no
/// prefix and is stored as typed (<c>dir\sub</c> gives <c>dir/sub/...</c>); an absolute one, or one
/// with a <c>.</c> or <c>..</c> part, keeps only its last part, or with a wildcard the parts from
/// the first wildcard on; a name ending in <c>.</c> or <c>..</c> is <c>*</c> under that folder.
/// A mask with no wildcard and no recursion is looked up as typed (missing: a warning); any other
/// is searched for, one walk per prefix and literal folder with all its masks at once; a folder
/// taken is taken whole (what it holds is left to the entry filter), and an item found twice, or
/// under a folder taken, is taken once. The walk uses an explicit stack, enters no link, and
/// never takes the archive being written.
/// </summary>
public static class CliArchiveSources
{
    public const string NotFound = "The system cannot find the file specified.";
    public const string NotAFolder = "A name ending in a path separator names a folder, and this is a file.";

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>Plain names, none recursing: the names after the archive with no switch.</summary>
    public static CliArchiveSourceList Resolve(IEnumerable<string> typed, string baseFolder, string archivePath) =>
        Resolve([.. typed.Select(name => new CliNameMask(name, Include: true, CliRecursion.None))], null, false, baseFolder, archivePath);

    /// <param name="includes">The names to archive (after the archive, from list files, -i).</param>
    /// <param name="excludes">-x's filter over names in the archive: a searched item it leaves out is not taken.</param>
    /// <param name="caseSensitive">-ssc.</param>
    /// <param name="baseFolder">What a relative name is relative to.</param>
    /// <param name="archivePath">The archive being written: never taken.</param>
    public static CliArchiveSourceList Resolve(
        IReadOnlyList<CliNameMask> includes, CliNameFilter? excludes, bool caseSensitive, string baseFolder, string archivePath)
    {
        var archive = new WrittenArchive(Path.GetFullPath(archivePath, baseFolder));
        var sources = new List<CliArchiveSource>();
        var problems = new List<CliSourceProblem>();
        var searches = new Dictionary<(string Prefix, string Literal), List<(CliNameMask Mask, string Under)>>(PrefixComparer.Instance);

        foreach (CliNameMask mask in includes)
        {
            // T-F512 (c): a name Windows trims ("a.txt.", "d.\f.txt") goes to Core as typed. Made a
            // full path first, it named its twin, which was archived and then deleted by -sdel;
            // Core refuses it (T-F509).
            string typed = Path.Combine(baseFolder, mask.Path);
            if (InputPaths.NameTrimmed(typed))
            {
                sources.Add(new CliArchiveSource(typed, 1));
                continue;
            }
            (string prefix, string[] rest, bool everything) = Split(mask, baseFolder);
            // A drive root stays one source: Core lists it, leaving out the system's own entries (T-F285).
            if (everything && Path.GetDirectoryName(prefix) is null)
            {
                AddLiteral(mask.Path, prefix, 1, sources, problems);
                continue;
            }
            // 7-Zip's universal item for "." is a wildcard that does not recurse, whatever -spd and -r say.
            CliNameMask searched = everything ? mask with { WildcardMatching = true, Recursion = CliRecursion.None } : mask;
            string under = string.Join('/', rest);
            if (!Searches(searched, under))
            {
                AddLiteral(mask.Path, Path.Combine(prefix, under), rest.Length, sources, problems);
                continue;
            }
            string literal = string.Join('/', rest[..LiteralFolderCount(rest, searched.WildcardMatching)]);
            if (!Directory.Exists(Path.Combine(prefix, literal)))
            {
                problems.Add(new CliSourceProblem(mask.Path, NotFound));
                continue;
            }
            if (!searches.TryGetValue((prefix, literal), out List<(CliNameMask, string)>? masks))
                searches[(prefix, literal)] = masks = [];
            masks.Add((searched, under));
        }

        foreach (((string prefix, string literal), List<(CliNameMask Mask, string Under)> masks) in searches)
            Search(new SearchRoot(prefix, literal, archive), masks, excludes, caseSensitive, sources, problems);

        return new CliArchiveSourceList(WithoutDuplicates(sources, excludes), problems);
    }

    // CCensor::AddItem: the folder on disk the mask hangs under, the mask's parts below it, and
    // whether every part went into the prefix ("." or "dir\." is everything directly in it).
    private static (string Prefix, string[] Below, bool Everything) Split(CliNameMask mask, string baseFolder)
    {
        string root = Path.GetPathRoot(mask.Path) ?? "";
        string[] parts = mask.Path[root.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        int lastDots = Array.FindLastIndex(parts, part => part is "." or "..");
        int skip;
        if (lastDots == parts.Length - 1)
            skip = parts.Length;
        else if (root.Length > 0 || lastDots >= 0)
            skip = parts.Length - 1;
        else
            skip = 0;
        int firstWildcard = mask.WildcardMatching ? Array.FindIndex(parts, CliNameFilter.ContainsWildcard) : -1;
        if (firstWildcard > lastDots)
            skip = Math.Min(skip, firstWildcard);

        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine([baseFolder, root, .. parts[..skip]])));
        string[] rest = parts[skip..];
        return rest.Length == 0 ? (prefix, ["*"], true) : (prefix, rest, false);
    }

    private static bool Searches(CliNameMask mask, string under) =>
        mask.Recursion == CliRecursion.Recursive || (mask.WildcardMatching && CliNameFilter.ContainsWildcard(under));

    // The mask's leading folders up to the first one with a wildcard (never its last part).
    private static int LiteralFolderCount(string[] rest, bool wildcardMatching)
    {
        int literal = 0;
        while (literal < rest.Length - 1 && !(wildcardMatching && CliNameFilter.ContainsWildcard(rest[literal])))
            literal++;
        return literal;
    }

    // A name ending in a separator names a folder only (AddItem's forFile = false); 7-Zip warns on a file.
    private static void AddLiteral(string typed, string fullPath, int nameParts, List<CliArchiveSource> sources, List<CliSourceProblem> problems)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));
        if (Directory.Exists(full) || (File.Exists(full) && !Path.EndsInDirectorySeparator(typed)))
            sources.Add(new CliArchiveSource(full, nameParts));
        else
            problems.Add(new CliSourceProblem(typed, File.Exists(full) ? NotAFolder : NotFound));
    }

    // Where one walk starts: Prefix names the entries, Literal ('/' between parts) is the folder
    // under it the masks' literal parts already reach.
    private sealed record SearchRoot(string Prefix, string Literal, WrittenArchive Archive);

    // T-F511 PR 3: the archive being written, by what it is (PathIdentity), resolved once. Its
    // spelling may differ from the walk's (\\?\, 8.3, another case), and in a case-sensitive folder
    // another case is another file. Only a file of its size is opened to ask.
    private sealed class WrittenArchive
    {
        private readonly PathIdentity.Identity? _id;
        private readonly long _length;

        public WrittenArchive(string fullPath)
        {
            var file = new FileInfo(fullPath);
            if (!file.Exists)
                return;
            _length = file.Length;
            _id = PathIdentity.Resolve(fullPath)?.Id;
        }

        public bool Is(FileSystemInfo entry) =>
            _id is { } id && entry is FileInfo file && file.Length == _length && PathIdentity.Resolve(file.FullName)?.Id == id;
    }

    private static void Search(
        SearchRoot root, List<(CliNameMask Mask, string Under)> masks, CliNameFilter? excludes, bool caseSensitive,
        List<CliArchiveSource> sources, List<CliSourceProblem> problems) =>
        new MaskSearch(root, masks, excludes, caseSensitive, sources, problems).Run();

    // One walk of Search through DirectoryWalker (T-F511): a match is a source and is not entered;
    // a folder is entered while a mask can still match below it, never through a link, never by a
    // name Windows trims (listing "d." lists its twin "d"); a folder that cannot be listed is a
    // problem. The walk leaves the system's own entries of a drive root out (T-F285/T-F324).
    private sealed class MaskSearch
    {
        private readonly SearchRoot _root;
        private readonly CliNameFilter _includes;
        private readonly CliNameFilter? _excludes;
        private readonly bool _anyRecursive;
        private readonly int _deepest;
        private readonly List<CliArchiveSource> _sources;
        private readonly List<CliSourceProblem> _problems;

        public MaskSearch(
            SearchRoot root, List<(CliNameMask Mask, string Under)> masks, CliNameFilter? excludes, bool caseSensitive,
            List<CliArchiveSource> sources, List<CliSourceProblem> problems)
        {
            _root = root;
            _includes = CliNameFilter.Create(masks.Select(m => m.Mask with { Path = m.Under }), caseSensitive, startFromAll: false);
            _excludes = excludes;
            _anyRecursive = masks.Exists(m => m.Mask.Recursion == CliRecursion.Recursive
                || (m.Mask.Recursion == CliRecursion.WildcardOnly && m.Mask.WildcardMatching && CliNameFilter.ContainsWildcard(m.Under)));
            _deepest = masks.Max(m => m.Under.Split('/').Length);
            _sources = sources;
            _problems = problems;
        }

        public void Run()
        {
            string start = Path.Combine(_root.Prefix, _root.Literal.Replace('/', Path.DirectorySeparatorChar));
            foreach (WalkEntry entry in DirectoryWalker.Walk(start, LeaveOut))
            {
                switch (entry.Kind)
                {
                    case WalkEntryKind.UnreadableDirectory:
                        _problems.Add(new CliSourceProblem(entry.Info.FullName, entry.Error?.Message ?? ""));
                        break;
                    // Taken or skipped in LeaveOut; a link or a name Windows trims is not entered.
                    case WalkEntryKind.Directory:
                    case WalkEntryKind.File:
                    case WalkEntryKind.LeftOut:
                    case WalkEntryKind.ReparsePoint:
                    case WalkEntryKind.TrimmedName:
                        break;
                }
            }
        }

        // Once per entry below the start, before it is entered: the archive and what -x leaves out
        // are skipped, a match is taken, a folder no mask reaches below is not entered.
        private bool LeaveOut(FileSystemInfo child)
        {
            string name = child.FullName[_root.Prefix.Length..].TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/');
            int depth = name.Split('/').Length;
            bool isFolder = child is DirectoryInfo;
            if (_root.Archive.Is(child) || (_excludes is not null && !_excludes.Includes(name, isFolder)))
                return true;
            if (_includes.Includes(name, isFolder))
            {
                _sources.Add(new CliArchiveSource(child.FullName, depth));
                return true;
            }
            return !isFolder || !(_anyRecursive || depth < _deepest);
        }
    }

    // A source -x leaves out goes (Core would leave it out anyway, and it takes nothing below it
    // with it); a path given twice is taken once; one under a folder taken whole is already in it,
    // unless -x leaves it out there: then it stays a source of its own, as 7-Zip archives it.
    private static List<CliArchiveSource> WithoutDuplicates(List<CliArchiveSource> sources, CliNameFilter? excludes)
    {
        var taken = new TakenSources();
        foreach (CliArchiveSource source in sources.Where(s => excludes is null || excludes.Includes(NameOf(s), Directory.Exists(s.FullPath))))
            taken.TryAdd(source);

        var kept = new List<CliArchiveSource>();
        foreach (CliArchiveSource source in taken.All)
        {
            if (TakenAncestor(source.FullPath, taken) is not { } ancestor)
            {
                kept.Add(source);
                continue;
            }
            string inside = NameOf(ancestor) + "/" + Path.GetRelativePath(ancestor.FullPath, source.FullPath).Replace(Path.DirectorySeparatorChar, '/');
            if (excludes is not null && !excludes.Includes(inside, Directory.Exists(source.FullPath)))
                kept.Add(source);
        }
        return kept;
    }

    private static CliArchiveSource? TakenAncestor(string path, TakenSources taken)
    {
        for (string? folder = Path.GetDirectoryName(path); folder is not null; folder = Path.GetDirectoryName(folder))
        {
            if (taken.Find(folder) is { } ancestor)
                return ancestor;
        }
        return null;
    }

    // T-F517: the sources taken, each once. A path compared without case named one source twice in a
    // case-sensitive folder, where A.TXT and a.txt are two files, and the second was dropped. Two
    // spellings that differ only in case are one source only when they are one file (PathIdentity),
    // asked only on such a collision.
    private sealed class TakenSources
    {
        private readonly Dictionary<string, CliArchiveSource> _exact = [with(StringComparer.Ordinal)];
        private readonly Dictionary<string, List<string>> _byFoldedCase = [with(StringComparer.OrdinalIgnoreCase)];

        public IEnumerable<CliArchiveSource> All => _exact.Values;

        public CliArchiveSource? Find(string path)
        {
            if (_exact.TryGetValue(path, out CliArchiveSource? source))
                return source;
            if (!_byFoldedCase.TryGetValue(path, out List<string>? spellings))
                return null;
            string? same = spellings.Find(other => SameFile(other, path));
            return same is null ? null : _exact[same];
        }

        public void TryAdd(CliArchiveSource source)
        {
            if (Find(source.FullPath) is not null)
                return;
            _exact.Add(source.FullPath, source);
            if (_byFoldedCase.TryGetValue(source.FullPath, out List<string>? spellings))
                spellings.Add(source.FullPath);
            else
                _byFoldedCase.Add(source.FullPath, [source.FullPath]);
        }

        private static bool SameFile(string a, string b) =>
            PathIdentity.Resolve(a) is { } first && PathIdentity.Resolve(b) is { } second && first.Id == second.Id;
    }

    // The source's name in the archive: its last NameParts parts.
    private static string NameOf(CliArchiveSource source)
    {
        string[] parts = source.FullPath[(Path.GetPathRoot(source.FullPath) ?? "").Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        return string.Join('/', parts[Math.Max(0, parts.Length - source.NameParts)..]);
    }

    private sealed class PrefixComparer : IEqualityComparer<(string Prefix, string Literal)>
    {
        public static readonly PrefixComparer Instance = new();

        public bool Equals((string Prefix, string Literal) x, (string Prefix, string Literal) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Prefix, y.Prefix) && StringComparer.OrdinalIgnoreCase.Equals(x.Literal, y.Literal);

        public int GetHashCode((string Prefix, string Literal) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Prefix), StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Literal));
    }
}
