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
        // current folder's parent is the same folder as "C:\w\src".
        string current = RealPath(currentFolder) ?? Normalize(currentFolder);
        foreach (string source in sources)
        {
            if (RealPath(source) is not { } full)
                return $"-sdel cannot tell where this source leads: {source}";
            if (Path.GetPathRoot(full) is { } root && string.Equals(Normalize(root), full, StringComparison.OrdinalIgnoreCase))
                return $"-sdel would delete a volume root: {source}";
            if (IsSameOrUnder(current, full))
                return $"-sdel would delete the current folder or a folder holding it: {source}";
        }
        return null;
    }

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

    private static string WithoutLongPathPrefix(string path) =>
        path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + path[8..]
        : path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..]
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
    private sealed record Stamp(string Path, long Length, DateTime ModifiedUtc, DateTime CreatedUtc);

    private sealed class Source
    {
        public List<Stamp> Files { get; } = [];
        public List<string> Folders { get; } = [];
        public bool IsLink { get; set; }
        public bool Unlisted { get; set; }
    }

    private static readonly EnumerationOptions ListOptions = new() { AttributesToSkip = 0, IgnoreInaccessible = false };

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
        var source = new Source();
        try
        {
            var info = new FileInfo(full);
            if (!info.Exists && !Directory.Exists(full))
                return source;
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || CliSourceDeleter.LeadsElsewhere(full))
                source.IsLink = true;
            else if ((info.Attributes & FileAttributes.Directory) != 0)
                ListFolder(full, source);
            else
                source.Files.Add(StampOf(info));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            source.Unlisted = true;
        }
        return source;
    }

    private static void ListFolder(string root, Source source)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        source.Folders.Add(root);
        while (pending.Count > 0)
        {
            string folder = pending.Pop();
            foreach (FileSystemInfo entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", ListOptions))
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                if (entry is DirectoryInfo)
                {
                    source.Folders.Add(entry.FullName);
                    pending.Push(entry.FullName);
                }
                else
                {
                    source.Files.Add(StampOf((FileInfo)entry));
                }
            }
        }
    }

    private static Stamp StampOf(FileInfo file) => new(file.FullName, file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc);

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
        foreach (Stamp file in source.Files)
            DeleteFileIfUnchanged(file, kept, failed);
        // Deepest first: a folder is tried only after everything below it.
        foreach (string folder in source.Folders.OrderByDescending(f => f.Count(c => c == Path.DirectorySeparatorChar)))
            DeleteFolderIfEmpty(folder, failed);
    }

    private static void DeleteFileIfUnchanged(Stamp stamp, List<string> kept, List<(string, string)> failed)
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
