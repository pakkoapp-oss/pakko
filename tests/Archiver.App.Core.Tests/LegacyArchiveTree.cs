using Archiver.Core.Models;

namespace Archiver.App.Core.Tests;

/// <summary>
/// T-F535: the folder tree as it was before the compact index, kept verbatim as the oracle the
/// new one is compared with (ArchiveTreeOracleTests): a node and a dictionary per folder.
/// </summary>
internal sealed class LegacyArchiveTree
{
    private readonly Node _root = new(string.Empty, 0);

    public LegacyArchiveTree(IReadOnlyList<ArchiveEntryInfo> flatEntries)
    {
        foreach (ArchiveEntryInfo entry in flatEntries)
            Insert(entry);
    }

    /// <summary>
    /// The rows of <paramref name="folderPath"/> ('/'-separated, empty for the root), folders first,
    /// then files, both ordinal — File Explorer's order. False for a folder with no children.
    /// </summary>
    public bool TryGetChildren(string folderPath, out IReadOnlyList<ArchiveEntryViewModel> children)
    {
        Node? node = Find(folderPath);
        if (node?.Children is null)
        {
            children = [];
            return false;
        }
        children = node.Rows ??= BuildRows(node.Children);
        return true;
    }

    private void Insert(ArchiveEntryInfo entry)
    {
        string path = entry.Path;
        Node node = _root;
        int start = FirstSegmentStart(path);
        while (true)
        {
            int slash = path.IndexOf('/', start);
            int end = slash < 0 ? path.Length : slash;
            node.Children ??= new Dictionary<string, Node>(StringComparer.Ordinal);
            string name = path[start..end];
            if (!node.Children.TryGetValue(name, out Node? child))
            {
                child = new Node(path, end) { Entry = slash < 0 ? entry : null };
                node.Children[name] = child;
            }
            if (slash < 0)
                return;
            node = child;
            start = slash + 1;
        }
    }

    private Node? Find(string folderPath)
    {
        if (folderPath.Length == 0)
            return _root;
        Node? node = _root;
        int start = FirstSegmentStart(folderPath);
        while (node?.Children is not null)
        {
            int slash = folderPath.IndexOf('/', start);
            int end = slash < 0 ? folderPath.Length : slash;
            if (!node.Children.TryGetValue(folderPath[start..end], out node))
                return null;
            if (slash < 0)
                return node;
            start = slash + 1;
        }
        return null;
    }

    // A leading '/' belongs to the root — its empty first segment would otherwise be a folder
    // whose path, "", is the root's own.
    private static int FirstSegmentStart(string path) => path.Length > 1 && path[0] == '/' ? 1 : 0;

    private static List<ArchiveEntryViewModel> BuildRows(Dictionary<string, Node> children)
    {
        var rows = new List<ArchiveEntryViewModel>(children.Count);
        foreach ((string name, Node child) in children)
            rows.Add(child.ToRow(name));
        rows.Sort((a, b) =>
        {
            if (a.IsFolder != b.IsFolder)
                return a.IsFolder ? -1 : 1;
            return string.CompareOrdinal(a.Name, b.Name);
        });
        return rows;
    }

    // A node's own path is PathSource[..PathLength] — a prefix of the first entry path that
    // reached it, kept as a reference rather than copied.
    private sealed class Node(string pathSource, int pathLength)
    {
        public Dictionary<string, Node>? Children { get; set; }

        public ArchiveEntryInfo? Entry { get; init; }

        public List<ArchiveEntryViewModel>? Rows { get; set; }

        public ArchiveEntryViewModel ToRow(string name) => Entry is { } entry
            ? new ArchiveEntryViewModel
            {
                FullPath = entry.Path,
                Name = name,
                IsFolder = entry.IsDirectory,
                Size = entry.Size,
                CompressedSize = entry.CompressedSize,
                Crc32 = entry.Crc32,
                Modified = entry.Modified,
                ModifiedHasTime = entry.ModifiedHasTime,
                Encryption = entry.Encryption,
            }
            : new ArchiveEntryViewModel { FullPath = pathSource[..pathLength], Name = name, IsFolder = true };
    }
}
