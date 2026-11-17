namespace Archiver.Core.IO;

/// <summary>One file of a <see cref="SourceTree"/>, with what tells a changed file apart.</summary>
public readonly record struct SourceFileStamp(string Path, long Length, DateTime ModifiedUtc, DateTime CreatedUtc);

/// <summary>What <see cref="SourceTree.List"/> found for one source.</summary>
/// <param name="Exists">Whether the path was there at all (an empty or unparsable one is not).</param>
/// <param name="IsLink">The path itself is a link (junction, symbolic link); nothing below it is listed.</param>
/// <param name="Files">Every file below (or the path itself, for a file).</param>
/// <param name="Folders">Every folder, the path itself first.</param>
/// <param name="Links">Every link below the path, by its own path.</param>
/// <param name="Unreadable">A folder could not be listed: what the source holds is not known.</param>
public sealed record SourceListing(
    bool Exists, bool IsLink, IReadOnlyList<SourceFileStamp> Files, IReadOnlyList<string> Folders,
    IReadOnlyList<string> Links, bool Unreadable)
{
    /// <summary>T-F509/T-F531: the source itself and the names below it that Windows trims ("a.txt "),
    /// by their <c>\\?\</c> paths, as <see cref="Files"/> and <see cref="Folders"/> list them. ZIP
    /// archives them, but Pakko's own extraction cannot give such a name back, so the archive would be
    /// the only copy: a source holding one is never deleted.</summary>
    public IReadOnlyList<string> TrimmedNames { get; init; } = [];
}

/// <summary>
/// T-F420: one source as it is on disk now - the one listing "delete the sources afterwards" works
/// from, in the CLI (<c>-sdel</c> deletes its unchanged files) and in the App (a source is recycled
/// only when its listing is unchanged). Over <see cref="DirectoryWalker"/>: an explicit stack, a link
/// listed and never entered, an unreadable folder marked rather than thrown.
/// </summary>
public static class SourceTree
{
    private static readonly SourceListing Missing = new(false, false, [], [], [], false);

    /// <summary>Lists <paramref name="fullPath"/>; never throws for an I/O failure, which is
    /// <see cref="SourceListing.Unreadable"/> instead. A drive root's own Hidden+System entries are left out, as in
    /// every walk (<see cref="DirectoryWalker"/>, T-F345). A name Windows trims is listed by its
    /// <c>\\?\</c> path, as ZIP reads it (T-F531); written so, the source may be one itself.</summary>
    public static SourceListing List(string fullPath)
    {
        // T-F406: a path Windows cannot parse is checked at the entry, never caught as an ArgumentException.
        if (Archiver.Core.Services.InputPaths.SpelledProblem(fullPath) is not null)
            return Missing;
        List<string> trimmed = Archiver.Core.Services.InputPaths.NameTrimmed(fullPath) ? [fullPath] : [];
        try
        {
            var file = new FileInfo(fullPath);
            if (!file.Exists && !Directory.Exists(fullPath))
                return Missing;
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                return new SourceListing(true, true, [], [], [], false);
            if ((file.Attributes & FileAttributes.Directory) == 0)
                return new SourceListing(true, false, [StampOf(file)], [], [], false) { TrimmedNames = trimmed };

            var files = new List<SourceFileStamp>();
            var folders = new List<string>();
            var links = new List<string>();
            bool unreadable = false;
            bool below = false;
            foreach (WalkEntry entry in DirectoryWalker.Walk(fullPath, spelledBelow: true))
            {
                // The source's own entry comes first; it was judged by its whole path above.
                if (below && Archiver.Core.Services.InputPaths.OwnNameTrimmed(entry.Info.Name))
                    trimmed.Add(entry.Info.FullName);
                below = true;
                switch (entry.Kind)
                {
                    case WalkEntryKind.Directory:
                        folders.Add(entry.Info.FullName);
                        break;
                    case WalkEntryKind.File:
                        files.Add(StampOf((FileInfo)entry.Info));
                        break;
                    case WalkEntryKind.ReparsePoint:
                        links.Add(entry.Info.FullName);
                        break;
                    case WalkEntryKind.UnreadableDirectory:
                        unreadable = true;
                        break;
                    case WalkEntryKind.TrimmedName:
                    case WalkEntryKind.LeftOut:
                        break; // never in a spelled walk; no filter is given here
                }
            }
            return new SourceListing(true, false, files, folders, links, unreadable) { TrimmedNames = trimmed };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SourceListing(true, false, [], [], [], Unreadable: true); // its attributes cannot be read
        }
    }

    private static SourceFileStamp StampOf(FileInfo file) =>
        new(file.FullName, file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc);
}
