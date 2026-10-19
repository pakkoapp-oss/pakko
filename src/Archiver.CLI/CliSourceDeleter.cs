using Archiver.Core.IO;

namespace Archiver.CLI;

/// <summary>T-F411: what -sdel left on disk, and why.</summary>
/// <param name="Kept">Files changed since they were listed (size, modification or creation time):
/// what is in the archive is not what is on disk.</param>
/// <param name="Failed">Files and folders whose deletion failed, with the reason.</param>
/// <param name="Links">Sources that are links: never entered, never deleted.</param>
/// <param name="Unlisted">Sources with a folder that could not be listed: what they hold is unknown,
/// so nothing of them is deleted.</param>
public sealed record CliDeleteOutcome(
    IReadOnlyList<string> Kept,
    IReadOnlyList<(string Path, string Reason)> Failed,
    IReadOnlyList<string> Links,
    IReadOnlyList<string> Unlisted);

/// <summary>
/// T-F411: 7-Zip's -sdel (Update.cpp 26.02, the DeleteAfterCompressing pass of UpdateArchive): the
/// files listed before archiving are deleted when their size, modification and creation time are
/// still the same; then the folders, deepest first, each only once it is empty. Unlike 7-Zip a link
/// is never entered or deleted (7-Zip follows a junction and deletes the files behind it), and a
/// volume root or the current folder (or one holding it) is refused before anything is written.
/// </summary>
public static class CliSourceDeleter
{
    /// <summary>Why -sdel must not run on these sources, or null.</summary>
    public static string? Refusal(IEnumerable<string> sources, string currentFolder)
    {
        // T-F419: compared where the paths really lead - "\\?\C:\w\src" or a junction to the
        // current folder's parent is the same folder as "C:\w\src". A source that exists is also
        // compared by file identity, which every spelling of it shares (\\?\Volume{...}\,
        // \\?\GLOBALROOT\, \\.\C:\, \\localhost\C$\).
        string current = RealPath(currentFolder) ?? Normalize(currentFolder);
        // T-F420: a current folder that exists but cannot be resolved is refused, not compared by
        // its spelling alone (a fictitious one, as in a unit test, has no identity to compare).
        HashSet<PathIdentity.Identity>? holdingCurrent = PathIdentity.SelfAndAncestors(currentFolder);
        if (holdingCurrent is null && Directory.Exists(currentFolder))
            return $"-sdel cannot tell where the current folder leads: {currentFolder}";
        holdingCurrent ??= [];
        foreach (string source in sources)
        {
            if (RealPath(source) is not { } full)
                return $"-sdel cannot tell where this source leads: {source}";
            // Resolved first: Directory.Exists says no to the volume root \\?\GLOBALROOT\Device\HarddiskVolumeN\.
            PathIdentity.Resolved? opened = PathIdentity.Resolve(source);
            if (opened is null && (File.Exists(source) || Directory.Exists(source)))
                return $"-sdel cannot tell where this source leads: {source}";
            if (IsVolumeRoot(full) || (opened is not null && IsVolumeRoot(Normalize(opened.FinalPath))))
                return $"-sdel would delete a volume root: {source}";
            if (IsSameOrUnder(current, full) || (opened is not null && holdingCurrent.Contains(opened.Identity)))
                return $"-sdel would delete the current folder or a folder holding it: {source}";
        }
        return null;
    }

    private static bool IsVolumeRoot(string full) =>
        Path.GetPathRoot(full) is { } root && string.Equals(Normalize(root), full, StringComparison.OrdinalIgnoreCase);

    internal static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>T-F419: the full path with the long-path prefix dropped and every junction or
    /// symbolic link on the way resolved to its final target; null when that cannot be read.
    /// One step per path part (a link's own chain is resolved by the system), so the loop is bounded.</summary>
    internal static string? RealPath(string path)
    {
        try
        {
            string full = Normalize(WithoutLongPathPrefix(path));
            string? root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root))
                return null;
            string real = root;
            foreach (string part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                string next = Path.Combine(real, part);
                var info = new FileInfo(next);
                if (info.Exists || Directory.Exists(next))
                {
                    FileSystemInfo entry = info.Exists ? info : new DirectoryInfo(next);
                    if (entry.LinkTarget is not null)
                        next = entry.ResolveLinkTarget(returnFinalTarget: true) is { } target ? Normalize(target.FullName) : next;
                }
                real = next;
            }
            return Normalize(real);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>T-F419: whether <paramref name="path"/> passes a link on its way (j\t.txt with j a
    /// junction), or where it leads cannot be read: such a source is not deleted.</summary>
    internal static bool LeadsElsewhere(string path) =>
        RealPath(path) is not { } real
        || !real.Equals(Normalize(WithoutLongPathPrefix(path)), StringComparison.OrdinalIgnoreCase);

    // Only a drive letter or UNC after the prefix: \\?\Volume{...}\ and \\?\GLOBALROOT\ stay as
    // they are, or GetFullPath would read what follows as relative to the current folder.
    private static string WithoutLongPathPrefix(string path) =>
        path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + path[8..]
        : path.Length >= 6 && path.StartsWith(@"\\?\", StringComparison.Ordinal) && char.IsAsciiLetter(path[4]) && path[5] == ':' ? path[4..]
        : path;

    private static bool IsSameOrUnder(string path, string folder) =>
        path.Equals(folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// T-F411: every file and folder of -sdel's sources as they were before archiving - the list
/// 7-Zip deletes from (its dirItems). Walked with an explicit stack; a link is not entered.
/// </summary>
public sealed class CliSourceSnapshot
{
    // T-F420: the listing is Core's SourceTree, which the App's snapshot uses too.
    private sealed record Source(IReadOnlyList<SourceFileStamp> Files, IReadOnlyList<string> Folders, bool IsLink, bool Unlisted);

    private readonly Dictionary<string, Source> _sources = new(StringComparer.OrdinalIgnoreCase);

    private CliSourceSnapshot() { }

    /// <summary>The sources listed, as full paths.</summary>
    public IEnumerable<string> SourcePaths => _sources.Keys;

    public static CliSourceSnapshot Take(IEnumerable<string> sources)
    {
        var snapshot = new CliSourceSnapshot();
        foreach (string source in sources)
        {
            string full = CliSourceDeleter.Normalize(source);
            if (!snapshot._sources.ContainsKey(full))
                snapshot._sources[full] = List(full);
        }
        return snapshot;
    }

    private static Source List(string full)
    {
        SourceListing tree = SourceTree.List(full);
        if (!tree.Exists)
            return new Source([], [], IsLink: false, Unlisted: false);
        if (tree.IsLink || CliSourceDeleter.LeadsElsewhere(full))
            return new Source([], [], IsLink: true, Unlisted: false);
        return new Source(tree.Files, tree.Folders, IsLink: false, Unlisted: tree.Unreadable);
    }

    /// <summary>Deletes what is unchanged of <paramref name="completedSources"/> (the sources the
    /// archive holds in full); a source not given, or not listed, is not touched.</summary>
    public CliDeleteOutcome DeleteUnchanged(IEnumerable<string> completedSources)
    {
        var kept = new List<string>();
        var failed = new List<(string, string)>();
        var links = new List<string>();
        var unlisted = new List<string>();
        foreach (string completed in completedSources)
        {
            string full = CliSourceDeleter.Normalize(completed);
            if (!_sources.TryGetValue(full, out Source? source))
                continue;
            if (source.IsLink)
                links.Add(full);
            else if (source.Unlisted)
                unlisted.Add(full);
            else
                DeleteSource(source, kept, failed);
        }
        return new CliDeleteOutcome(kept, failed, links, unlisted);
    }

    private static void DeleteSource(Source source, List<string> kept, List<(string, string)> failed)
    {
        foreach (SourceFileStamp file in source.Files)
            DeleteFileIfUnchanged(file, kept, failed);
        // Deepest first: a folder is tried only after everything below it.
        foreach (string folder in source.Folders.OrderByDescending(f => f.Count(c => c == Path.DirectorySeparatorChar)))
            DeleteFolderIfEmpty(folder, failed);
    }

    private static void DeleteFileIfUnchanged(SourceFileStamp stamp, List<string> kept, List<(string, string)> failed)
    {
        try
        {
            var now = new FileInfo(stamp.Path);
            if (!now.Exists)
                return;
            if (now.Length != stamp.Length || now.LastWriteTimeUtc != stamp.ModifiedUtc || now.CreationTimeUtc != stamp.CreatedUtc
                || (now.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                kept.Add(stamp.Path);
                return;
            }
            // 7-Zip's DeleteFileAlways: a read-only file is deleted too.
            if ((now.Attributes & FileAttributes.ReadOnly) != 0)
                now.Attributes &= ~FileAttributes.ReadOnly;
            now.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failed.Add((stamp.Path, ex.Message));
        }
    }

    private static readonly EnumerationOptions ListOptions = new() { AttributesToSkip = 0, IgnoreInaccessible = false };

    // 7-Zip's RemoveDirAlways_if_Empty: a folder that still holds anything (a file created or
    // changed meanwhile, a link) stays, and that is not a failure.
    private static void DeleteFolderIfEmpty(string folder, List<(string, string)> failed)
    {
        try
        {
            var info = new DirectoryInfo(folder);
            if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.EnumerateFileSystemInfos("*", ListOptions).Any())
                return;
            if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                info.Attributes &= ~FileAttributes.ReadOnly;
            info.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failed.Add((folder, ex.Message));
        }
    }
}
