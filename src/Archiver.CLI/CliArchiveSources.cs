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

    private const FileAttributes OsOwned = FileAttributes.Hidden | FileAttributes.System;

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
        string archiveFull = Path.GetFullPath(archivePath, baseFolder);
        var sources = new List<CliArchiveSource>();
        var problems = new List<CliSourceProblem>();
        var searches = new Dictionary<(string Prefix, string Literal), List<(CliNameMask Mask, string Under)>>(PrefixComparer.Instance);

        foreach (CliNameMask mask in includes)
        {
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
            Search(new SearchRoot(prefix, literal, archiveFull), masks, excludes, caseSensitive, sources, problems);

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
    private sealed record SearchRoot(string Prefix, string Literal, string ArchiveFull);

    private static void Search(
        SearchRoot root, List<(CliNameMask Mask, string Under)> masks, CliNameFilter? excludes, bool caseSensitive,
        List<CliArchiveSource> sources, List<CliSourceProblem> problems)
    {
        CliNameFilter includes = CliNameFilter.Create(masks.Select(m => m.Mask with { Path = m.Under }), caseSensitive, startFromAll: false);
        bool anyRecursive = masks.Exists(m => m.Mask.Recursion == CliRecursion.Recursive
            || (m.Mask.Recursion == CliRecursion.WildcardOnly && m.Mask.WildcardMatching && CliNameFilter.ContainsWildcard(m.Under)));
        int deepest = masks.Max(m => m.Under.Split('/').Length);
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false };

        int literalDepth = root.Literal.Length == 0 ? 0 : root.Literal.Split('/').Length;
        var pending = new Stack<(DirectoryInfo Folder, string Name, int Depth)>();
        pending.Push((new DirectoryInfo(Path.Combine(root.Prefix, root.Literal)), root.Literal, literalDepth));
        while (pending.Count > 0)
        {
            (DirectoryInfo folder, string folderName, int depth) = pending.Pop();
            FileSystemInfo[] children;
            try
            {
                children = [.. folder.EnumerateFileSystemInfos("*", options)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add(new CliSourceProblem(folder.FullName, ex.Message));
                continue;
            }

            // T-F285/T-F324: in a drive root the system's own entries (Hidden and System) are left
            // out, as Core leaves them out of a drive root given whole.
            bool driveRoot = folder.Parent is null;
            foreach (FileSystemInfo child in children)
            {
                if (string.Equals(child.FullName, root.ArchiveFull, StringComparison.OrdinalIgnoreCase)
                    || (driveRoot && (child.Attributes & OsOwned) == OsOwned))
                    continue;
                string name = folderName.Length == 0 ? child.Name : folderName + "/" + child.Name;
                bool isFolder = child is DirectoryInfo;
                if (excludes is not null && !excludes.Includes(name, isFolder))
                    continue;
                if (includes.Includes(name, isFolder))
                    sources.Add(new CliArchiveSource(child.FullName, depth + 1));
                else if (isFolder && (anyRecursive || depth + 1 < deepest) && (child.Attributes & FileAttributes.ReparsePoint) == 0)
                    pending.Push(((DirectoryInfo)child, name, depth + 1));
            }
        }
    }

    // A source -x leaves out goes (Core would leave it out anyway, and it takes nothing below it
    // with it); a path given twice is taken once; one under a folder taken whole is already in it,
    // unless -x leaves it out there: then it stays a source of its own, as 7-Zip archives it.
    private static List<CliArchiveSource> WithoutDuplicates(List<CliArchiveSource> sources, CliNameFilter? excludes)
    {
        var taken = new Dictionary<string, CliArchiveSource>(StringComparer.OrdinalIgnoreCase);
        foreach (CliArchiveSource source in sources)
        {
            if (excludes is null || excludes.Includes(NameOf(source), Directory.Exists(source.FullPath)))
                taken.TryAdd(source.FullPath, source);
        }

        var kept = new List<CliArchiveSource>(taken.Count);
        foreach (CliArchiveSource source in taken.Values)
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

    private static CliArchiveSource? TakenAncestor(string path, Dictionary<string, CliArchiveSource> taken)
    {
        for (string? folder = Path.GetDirectoryName(path); folder is not null; folder = Path.GetDirectoryName(folder))
        {
            if (taken.TryGetValue(folder, out CliArchiveSource? ancestor))
                return ancestor;
        }
        return null;
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
