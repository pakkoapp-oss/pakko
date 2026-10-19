using Archiver.Core.IO;

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
/// nothing to delete "except the new part" as 7-Zip's -sdel does). The listing is Core's
/// <c>SourceTree</c>, which the CLI's per-file <c>CliSourceSnapshot</c> uses too (T-F420).
/// </summary>
public sealed class SourceSnapshot : ISourceSnapshot
{
    private sealed record Listing(HashSet<string> Entries, bool HoldsLink, bool Unreadable);

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

    // One line per entry of Core's SourceTree (the listing -sdel works from too, T-F420): a file
    // with its size and times, a folder, a link by its path alone.
    private static Listing List(string root)
    {
        SourceListing tree = SourceTree.List(root);
        if (!tree.Exists)
            return new Listing([], HoldsLink: false, Unreadable: true);
        if (tree.IsLink)
            return new Listing(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "l|" + root }, HoldsLink: false, Unreadable: false);
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SourceFileStamp file in tree.Files)
            entries.Add($"f|{file.Path}|{file.Length}|{file.ModifiedUtc.Ticks}|{file.CreatedUtc.Ticks}");
        foreach (string folder in tree.Folders)
            entries.Add("d|" + folder);
        foreach (string link in tree.Links)
            entries.Add("l|" + link);
        return new Listing(entries, HoldsLink: tree.Links.Count > 0, tree.Unreadable);
    }
}
