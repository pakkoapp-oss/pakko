using Archiver.Core.Models;

namespace Archiver.App.Core;

/// <summary>
/// Builds a parent-path -&gt; children index from a flat ArchiveEntryInfo list, once per archive
/// open. Folder navigation afterward is an O(1) dictionary lookup — there is no per-navigation
/// re-scan of the flat list, which matters at the 65,000+-entry scale this app's archives can
/// reach (T-F20's Zip64 tests). Lives in Archiver.App.Core (not Archiver.Core) because
/// Archiver.Core has zero WinUI/UI-model references — a folder hierarchy is an App-layer concern.
/// </summary>
public static class ArchiveTreeIndex
{
    public static IReadOnlyDictionary<string, IReadOnlyList<ArchiveEntryViewModel>> Build(
        IReadOnlyList<ArchiveEntryInfo> flatEntries)
    {
        // Many ZIPs have no explicit directory entries — folders are implied purely by '/' in
        // file paths (confirmed for this project's own fixtures: ZipArchiveService.ListEntriesAsync
        // reports IsDirectory=false for every entry of valid_nested_folders.zip). tar-family
        // listings do carry explicit directory entries. Either input shape must produce the same
        // tree, so every node — explicit or implied — is deduplicated by path before grouping.
        var nodesByPath = new Dictionary<string, ArchiveEntryViewModel>(StringComparer.Ordinal);

        foreach (var entry in flatEntries)
        {
            string path = entry.Path;
            if (!nodesByPath.ContainsKey(path))
            {
                nodesByPath[path] = new ArchiveEntryViewModel
                {
                    FullPath = path,
                    Name = path[(path.LastIndexOf('/') + 1)..],
                    IsFolder = entry.IsDirectory,
                    Size = entry.Size,
                    CompressedSize = entry.CompressedSize,
                    Modified = entry.Modified,
                };
            }

            // Synthesize every ancestor folder implied by this entry's path, even if no explicit
            // directory entry for it exists in flatEntries.
            int slash = path.LastIndexOf('/');
            while (slash >= 0)
            {
                string folderPath = path[..slash];
                if (!nodesByPath.ContainsKey(folderPath))
                {
                    nodesByPath[folderPath] = new ArchiveEntryViewModel
                    {
                        FullPath = folderPath,
                        Name = folderPath[(folderPath.LastIndexOf('/') + 1)..],
                        IsFolder = true,
                    };
                }
                slash = folderPath.LastIndexOf('/');
            }
        }

        var childrenByParent = new Dictionary<string, List<ArchiveEntryViewModel>>(StringComparer.Ordinal);
        foreach (var node in nodesByPath.Values)
        {
            int slash = node.FullPath.LastIndexOf('/');
            string parentPath = slash >= 0 ? node.FullPath[..slash] : string.Empty;
            if (!childrenByParent.TryGetValue(parentPath, out var siblings))
                childrenByParent[parentPath] = siblings = [];
            siblings.Add(node);
        }

        // Folders first, then files, both alphabetical — matches File Explorer's own ordering.
        var result = new Dictionary<string, IReadOnlyList<ArchiveEntryViewModel>>(StringComparer.Ordinal);
        foreach (var (parentPath, children) in childrenByParent)
        {
            children.Sort((a, b) =>
            {
                if (a.IsFolder != b.IsFolder)
                    return a.IsFolder ? -1 : 1;
                return string.CompareOrdinal(a.Name, b.Name);
            });
            result[parentPath] = children;
        }

        return result;
    }
}
