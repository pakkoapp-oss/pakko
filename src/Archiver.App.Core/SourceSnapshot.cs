namespace Archiver.App.Core;

/// <summary>What "Delete after operation" may still delete, against what the sources were when
/// the operation started (T-F419).</summary>
public interface ISourceSnapshot
{
    /// <summary>True only when <paramref name="source"/> was listed and is on disk as it was then:
    /// the same files with the same size, modification and creation time, nothing added, every
    /// folder readable.</summary>
    bool Unchanged(string source);

    /// <summary>Whether <paramref name="source"/> holds a link (junction, symbolic link) below it;
    /// such a folder is recycled whole, never deleted permanently.</summary>
    bool HoldsLink(string source);
}

/// <summary>
/// T-F419: every file, folder and link of the sources, taken before the operation runs. The App
/// awaits the summary dialog before its cleanup and the user keeps working meanwhile, so a file
/// written into a source after it was archived, or edited, would go with it. A source that differs
/// in any way is kept whole and reported (the Recycle Bin takes folders, not files, so there is
/// nothing to delete "except the new part" as 7-Zip's -sdel does). Walked with an explicit stack;
/// a link is listed, never entered. The CLI's <c>CliSourceSnapshot</c> is the per-file sibling;
/// T-F420 is to share them.
/// </summary>
public sealed class SourceSnapshot : ISourceSnapshot
{
    private sealed record Listing(HashSet<string> Entries, bool HoldsLink, bool Unreadable);

    private static readonly EnumerationOptions ListOptions = new() { AttributesToSkip = 0, IgnoreInaccessible = false };

    private readonly Dictionary<string, Listing> _sources = new(StringComparer.OrdinalIgnoreCase);

    private SourceSnapshot() { }

    /// <summary>Lists <paramref name="sources"/> as they are now, before the operation runs.</summary>
    public static SourceSnapshot Take(IEnumerable<string> sources)
    {
        var snapshot = new SourceSnapshot();
        foreach (string source in sources)
        {
            if (Key(source) is { } key && !snapshot._sources.ContainsKey(key))
                snapshot._sources[key] = List(key);
        }
        return snapshot;
    }

    /// <inheritdoc/>
    public bool Unchanged(string source)
    {
        if (Key(source) is not { } key || !_sources.TryGetValue(key, out Listing? then) || then.Unreadable)
            return false;
        Listing now = List(key);
        return !now.Unreadable && now.Entries.SetEquals(then.Entries);
    }

    /// <inheritdoc/>
    public bool HoldsLink(string source) =>
        Key(source) is not { } key || !_sources.TryGetValue(key, out Listing? then) || then.HoldsLink;

    // The same spelling for Take and the lookups: Core reports a source as it processed it, with
    // its trailing separators trimmed.
    private static string? Key(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null; // a path Windows cannot parse was never listed, so it is never deleted
        }
    }

    // One line per entry: its path, and for a file its size and times; a link by its path alone.
    private static Listing List(string root)
    {
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool holdsLink = false;
        try
        {
            var rootInfo = new FileInfo(root);
            if (!rootInfo.Exists)
            {
                if (!Directory.Exists(root))
                    return new Listing(entries, HoldsLink: false, Unreadable: true);
                var pending = new Stack<DirectoryInfo>();
                pending.Push(new DirectoryInfo(root));
                entries.Add("d|" + root);
                while (pending.Count > 0)
                {
                    foreach (FileSystemInfo entry in pending.Pop().EnumerateFileSystemInfos("*", ListOptions))
                    {
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            holdsLink = true;
                            entries.Add("l|" + entry.FullName);
                        }
                        else if (entry is DirectoryInfo folder)
                        {
                            entries.Add("d|" + folder.FullName);
                            pending.Push(folder);
                        }
                        else
                        {
                            entries.Add(Stamp((FileInfo)entry));
                        }
                    }
                }
            }
            else
            {
                entries.Add(Stamp(rootInfo));
            }
            return new Listing(entries, holdsLink, Unreadable: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Listing(entries, holdsLink, Unreadable: true); // what it holds is unknown: never deleted
        }
    }

    private static string Stamp(FileInfo file) =>
        $"f|{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|{file.CreationTimeUtc.Ticks}";
}
