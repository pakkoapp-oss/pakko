namespace Archiver.CLI;

/// <summary>T-F410: how a name given to -i, -x or after the archive recurses (7-Zip's -r, -r-, -r0).</summary>
public enum CliRecursion
{
    None = 0,
    Recursive = 1,
    WildcardOnly = 2,
}

/// <summary>T-F410: one name a filter was given: after the archive, in -i/-x, or read from a list file.</summary>
public sealed record CliNameMask(string Path, bool Include, CliRecursion Recursion, bool WildcardMatching = true);

/// <summary>
/// T-F410: which archive entries 7-Zip's name filters take — a port of 7-Zip 26.02's
/// NWildcard::CCensor for x and l (Common/Wildcard.cpp; censor built with k_AbsPath, so a name
/// is never split into a prefix). Each literal leading folder of a mask becomes a sub-node, and
/// recursion floats only below it: "-xr!dir\*.nsis" excludes dir\sub\d.nsis, not Other\dir\x.nsis.
/// Excludes win over includes. Built from command-line names; matched against archive entry names,
/// which are untrusted, so nothing here recurses.
/// </summary>
public sealed class CliNameFilter
{
    private readonly Node _root = new("");
    private readonly bool _caseSensitive;
    private readonly bool _excludeFolders;
    private readonly bool _excludeFiles;

    private CliNameFilter(bool caseSensitive, bool excludeFolders, bool excludeFiles)
    {
        _caseSensitive = caseSensitive;
        _excludeFolders = excludeFolders;
        _excludeFiles = excludeFiles;
    }

    /// <summary>
    /// Builds the filter. With no include mask at all, every entry is included first, as 7-Zip's
    /// AddToCensorFromNonSwitchesStrings adds "*" when there is no name after the archive and no -i.
    /// </summary>
    public static CliNameFilter Create(
        IEnumerable<CliNameMask> masks, bool caseSensitive = false, bool excludeFolders = false, bool excludeFiles = false)
    {
        var filter = new CliNameFilter(caseSensitive, excludeFolders, excludeFiles);
        List<CliNameMask> all = [.. masks];
        if (!all.Exists(m => m.Include))
            filter.Add(new CliNameMask("*", Include: true, CliRecursion.None));
        foreach (CliNameMask mask in all)
            filter.Add(mask);
        return filter;
    }

    /// <summary>True when 7-Zip would extract or list the entry: <paramref name="entryPath"/> as the
    /// archive names it ('/' or '\' between parts), <paramref name="isFolder"/> for a folder entry.</summary>
    public bool Includes(string entryPath, bool isFolder)
    {
        if (isFolder ? _excludeFolders : _excludeFiles)
            return false;
        string[] parts = SplitPath(entryPath.TrimEnd('/', '\\'));
        bool isFile = !isFolder;

        // CCensorNode::CheckPathVect, unrolled: the excludes of each node on the way down first,
        // then the includes from the deepest node back up.
        var visited = new List<(Node Node, int From)>();
        Node node = _root;
        int from = 0;
        while (true)
        {
            if (node.Matches(include: false, parts, from, isFile, this))
                return false;
            visited.Add((node, from));
            if (parts.Length - from <= 1 || node.FindSubNode(parts[from], this) is not { } sub)
                break;
            node = sub;
            from++;
        }
        for (int i = visited.Count - 1; i >= 0; i--)
        {
            if (visited[i].Node.Matches(include: true, parts, visited[i].From, isFile, this))
                return true;
        }
        return false;
    }

    // CCensor::AddItem (k_AbsPath) and CCensorNode::AddItem.
    private void Add(CliNameMask mask)
    {
        string[] parts = SplitPath(mask.Path);
        bool forFile = true;
        bool forDir = true;
        if (parts.Length > 1 && parts[^1].Length == 0)
        {
            // A trailing separator names a folder only.
            forFile = false;
            parts = parts[..^1];
        }
        bool recursive = mask.Recursion switch
        {
            CliRecursion.Recursive => true,
            CliRecursion.WildcardOnly => ContainsWildcard(mask.Path),
            _ => false,
        };
        bool wildcard = mask.WildcardMatching;

        Node node = _root;
        while (parts.Length > 1 && !(wildcard && ContainsWildcard(parts[0])))
        {
            node = node.FindOrAddSubNode(parts[0], this);
            parts = parts[1..];
        }
        if (parts.Length == 1 && wildcard && !ContainsWildcard(parts[0]))
            wildcard = false;
        (mask.Include ? node.IncludeItems : node.ExcludeItems).Add(new Item(parts, forFile, forDir, recursive, wildcard));
    }

    // CItem::CheckPath: the mask's parts against the path's parts from offset start..finish.
    private bool ItemMatches(Item item, string[] path, int from, bool isFile)
    {
        if (!isFile && !item.ForDir)
            return false;
        int delta = path.Length - from - item.Parts.Length;
        if (delta < 0)
            return false;
        int start = 0;
        int finish = 0;
        if (isFile)
        {
            if (!item.ForDir)
            {
                if (item.Recursive)
                    start = delta;
                else if (delta != 0)
                    return false;
            }
            if (!item.ForFile && delta == 0)
                return false;
        }
        if (item.Recursive)
        {
            finish = delta;
            if (isFile && !item.ForFile)
                finish = delta - 1;
        }
        for (int d = start; d <= finish; d++)
        {
            int i = 0;
            while (i < item.Parts.Length && PartMatches(item, i, path[from + d + i]))
                i++;
            if (i == item.Parts.Length)
                return true;
        }
        return false;
    }

    private bool PartMatches(Item item, int index, string name) =>
        item.WildcardMatching
            ? MaskMatches(item.Parts[index], name, _caseSensitive)
            : NamesEqual(item.Parts[index], name);

    private bool NamesEqual(string a, string b) =>
        string.Equals(a, b, _caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 7-Zip's EnhancedMaskTest ('*' any run, '?' one character), without its recursion: on a
    /// mismatch it goes back to the last '*' and lets it take one more character, O(mask × name).
    /// </summary>
    public static bool MaskMatches(string mask, string name, bool caseSensitive)
    {
        int m = 0;
        int n = 0;
        int starMask = -1;
        int starName = 0;
        while (n < name.Length)
        {
            if (m < mask.Length && mask[m] == '*')
            {
                starMask = m++;
                starName = n;
            }
            else if (m < mask.Length && (mask[m] == '?' || SameChar(mask[m], name[n], caseSensitive)))
            {
                m++;
                n++;
            }
            else if (starMask >= 0)
            {
                m = starMask + 1;
                n = ++starName;
            }
            else
            {
                return false;
            }
        }
        while (m < mask.Length && mask[m] == '*')
            m++;
        return m == mask.Length;
    }

    private static bool SameChar(char a, char b, bool caseSensitive) =>
        a == b || (!caseSensitive && char.ToUpperInvariant(a) == char.ToUpperInvariant(b));

    public static bool ContainsWildcard(string value) => value.AsSpan().IndexOfAny('*', '?') >= 0;

    private static string[] SplitPath(string path) => path.Split('/', '\\');

    private sealed record Item(string[] Parts, bool ForFile, bool ForDir, bool Recursive, bool WildcardMatching);

    private sealed class Node(string name)
    {
        private readonly List<Node> _subNodes = [];

        public string Name { get; } = name;
        public List<Item> IncludeItems { get; } = [];
        public List<Item> ExcludeItems { get; } = [];

        public Node? FindSubNode(string name, CliNameFilter filter) => _subNodes.Find(n => filter.NamesEqual(n.Name, name));

        public Node FindOrAddSubNode(string name, CliNameFilter filter)
        {
            if (FindSubNode(name, filter) is { } existing)
                return existing;
            var added = new Node(name);
            _subNodes.Add(added);
            return added;
        }

        public bool Matches(bool include, string[] path, int from, bool isFile, CliNameFilter filter) =>
            (include ? IncludeItems : ExcludeItems).Exists(item => filter.ItemMatches(item, path, from, isFile));
    }
}
