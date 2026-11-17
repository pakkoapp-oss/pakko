namespace Archiver.Core.IO;

/// <summary>What one step of <see cref="DirectoryWalker.Walk"/> found.</summary>
public enum WalkEntryKind
{
    /// <summary>A directory that was listed (the root included), reported before its contents.</summary>
    Directory,

    /// <summary>A regular file.</summary>
    File,

    /// <summary>A symlink, junction or other reparse point — reported, never followed (T-F23/T-F37).</summary>
    ReparsePoint,

    /// <summary>A directory whose listing failed; <see cref="WalkEntry.Error"/> says why.</summary>
    UnreadableDirectory,

    /// <summary>T-F410: an entry the caller's filter left out — never entered, and not counted
    /// when its folder is asked whether it is empty.</summary>
    LeftOut,

    /// <summary>T-F509: a file or folder whose name Windows changes when it opens the plain path
    /// ("a.txt ", "d."), which then names a twin ("a.txt", "d") or nothing. Never entered: listing
    /// "d." lists "d". A filter's <see cref="LeftOut"/> and a <see cref="ReparsePoint"/> come first.</summary>
    TrimmedName,
}

/// <summary>One step of <see cref="DirectoryWalker.Walk"/>.</summary>
/// <param name="Kind">What was found.</param>
/// <param name="Info">A <see cref="FileInfo"/> for a file (or a file-like reparse point), a
/// <see cref="DirectoryInfo"/> otherwise. Its size and attributes come from the directory listing
/// itself, so reading them costs no extra stat call.</param>
/// <param name="IsEmptyDirectory">For <see cref="WalkEntryKind.Directory"/>: the listing held no
/// entries at all (a folder holding only a reparse point is not empty).</param>
/// <param name="Error">For <see cref="WalkEntryKind.UnreadableDirectory"/>: why the listing failed.</param>
public readonly record struct WalkEntry(
    WalkEntryKind Kind, FileSystemInfo Info, bool IsEmptyDirectory = false, Exception? Error = null);

/// <summary>
/// T-F236/T-F237/T-F251: the one walk over a user-supplied folder tree. An explicit stack, not
/// recursion, so depth is bounded by memory rather than the thread stack (a 2,000-deep tree used
/// to kill the process with an uncatchable stack overflow). Each directory is listed once, inside
/// one catch: an unreadable folder becomes a single <see cref="WalkEntryKind.UnreadableDirectory"/>
/// entry and the walk goes on. Reparse points are reported and never entered, so a junction loop
/// cannot recurse and a junction to an outside folder adds nothing.
/// <para>
/// Order is deterministic (T-F31/T-F32): depth-first pre-order; within a directory, its files by
/// full path (ordinal, case-insensitive), then its subdirectories the same way, each subtree
/// complete before the next sibling. The sequence is lazy, so a consumer can start working on the
/// first files while the rest of the tree is still unlisted.
/// </para>
/// <para>
/// T-F345: when <c>root</c> is a drive or share root, its own entries that are both Hidden and
/// System are left out ("System Volume Information", "$Recycle.Bin", "pagefile.sys": the OS's,
/// mostly unreadable, and on every NTFS volume). The same rule as the App's browser (T-F324) and
/// TAR creation (T-F285). Deeper down such an entry is the user's and is walked.
/// </para>
/// </summary>
public static class DirectoryWalker
{
    private const FileAttributes OsOwned = FileAttributes.Hidden | FileAttributes.System;

    /// <summary>The entries of the tree under <paramref name="root"/>, as the class describes. A root
    /// <see cref="Archiver.Core.Services.InputPaths"/> refuses (empty, unparsable, a name Windows trims,
    /// which would list its twin) gives no entry at all (T-F511).</summary>
    /// <param name="root">The folder to walk; its own entry comes first.</param>
    /// <param name="leaveOut">T-F410: true for an entry below the root to leave out. Called once per
    /// entry, in the folder's listing order, before the entry is entered (T-F511: the CLI's searches
    /// take a match there).</param>
    /// <param name="spelledBelow">T-F531: a name Windows trims below the root is given by its literal
    /// <c>\\?\</c> path (as is everything below it) and walked as any other, never
    /// <see cref="WalkEntryKind.TrimmedName"/>; <paramref name="leaveOut"/> gets it so too. A root written
    /// as <c>\\?\</c> may then itself be such a name. Name the entries with <see cref="RelativePath"/>.</param>
    public static IEnumerable<WalkEntry> Walk(string root, Func<FileSystemInfo, bool>? leaveOut = null, bool spelledBelow = false) =>
        (spelledBelow ? Archiver.Core.Services.InputPaths.SpelledProblem(root) : Archiver.Core.Services.InputPaths.Problem(root)) is null
            ? WalkFrom(root, leaveOut, spelled: spelledBelow)
            : [];

    /// <summary>T-F531: the path of a walk's entry relative to <paramref name="root"/>, as spelled. A
    /// literal <c>\\?\</c> entry below a plain root is compared with the root's literal form:
    /// <see cref="Path.GetRelativePath(string, string)"/> on a plain path drops a trailing space or dot
    /// ("a.txt " would be named as its twin "a.txt"), on two literal ones it changes nothing.</summary>
    public static string RelativePath(string root, string fullName)
    {
        string fullRoot = Path.GetFullPath(root);
        if (IsLiteral(fullName) && !IsLiteral(fullRoot))
            fullRoot = Archiver.Core.Services.InputPaths.Literal(fullRoot);
        return Path.GetRelativePath(fullRoot, fullName);
    }

    /// <summary>T-F547: Pakko's own folder, walked by its literal <c>\\?\</c> path: every path below
    /// stays literal, so a name Windows trims names that entry and no twin, and is entered as any
    /// other (never <see cref="WalkEntryKind.TrimmedName"/>). Links are still never entered. Only for
    /// the removal of Pakko's own folders (<see cref="TempOwner"/>); a root not written as
    /// <c>\\?\</c>, or holding a NUL, walks nothing.</summary>
    internal static IEnumerable<WalkEntry> WalkAsSpelled(string literalRoot) =>
        literalRoot.StartsWith(@"\\?\", StringComparison.Ordinal) && !literalRoot.Contains('\0')
            ? WalkFrom(literalRoot, null, spelled: true)
            : [];

    private static bool IsLiteral(string path) => path.StartsWith(@"\\?\", StringComparison.Ordinal);

    // spelled: a name Windows trims is reached by its literal path; below a literal root every path already is.
    private static IEnumerable<WalkEntry> WalkFrom(string root, Func<FileSystemInfo, bool>? leaveOut, bool spelled)
    {
        var rootDir = new DirectoryInfo(root);
        bool isDriveRoot = rootDir.Parent is null;
        var pending = new Stack<(DirectoryInfo Dir, bool IsReparsePoint)>();
        pending.Push((rootDir, false));

        while (pending.Count > 0)
        {
            (DirectoryInfo dir, bool isReparsePoint) = pending.Pop();
            if (isReparsePoint)
            {
                yield return new WalkEntry(WalkEntryKind.ReparsePoint, dir);
                continue;
            }

            FileSystemInfo[]? children = List(dir, leaveOutOsOwned: isDriveRoot && ReferenceEquals(dir, rootDir), out Exception? listingError);
            if (children is null)
            {
                yield return new WalkEntry(WalkEntryKind.UnreadableDirectory, dir, Error: listingError);
                continue;
            }
            if (spelled)
                children = AsSpelled(children);

            children = WithoutLeftOut(children, leaveOut, out FileSystemInfo[] leftOut);
            foreach (FileSystemInfo child in leftOut)
                yield return new WalkEntry(WalkEntryKind.LeftOut, child);

            yield return new WalkEntry(WalkEntryKind.Directory, dir, IsEmptyDirectory: children.Length == 0);

            // By name: the children of one folder share its path, and a literal one (T-F531) must not sort apart.
            foreach (FileInfo file in children.OfType<FileInfo>().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                yield return new WalkEntry(KindOf(file, WalkEntryKind.File, spelled), file);

            foreach (WalkEntry trimmed in PushSubfolders(children, pending, spelled))
                yield return trimmed;
        }
    }

    // T-F531: each child whose name Windows trims, by its literal path; by the plain one it is its twin.
    private static FileSystemInfo[] AsSpelled(FileSystemInfo[] children)
    {
        for (int i = 0; i < children.Length; i++)
        {
            FileSystemInfo child = children[i];
            if (IsLiteral(child.FullName) || !Archiver.Core.Services.InputPaths.OwnNameTrimmed(child.Name))
                continue;
            string literal = Archiver.Core.Services.InputPaths.Literal(child.FullName);
            children[i] = child is DirectoryInfo ? new DirectoryInfo(literal) : new FileInfo(literal);
        }
        return children;
    }

    // The subfolders to walk go on the stack, first one on top; those with a name Windows trims are
    // returned instead, in order, and never entered.
    private static List<WalkEntry> PushSubfolders(FileSystemInfo[] children, Stack<(DirectoryInfo Dir, bool IsReparsePoint)> pending, bool asSpelled)
    {
        DirectoryInfo[] subDirs = [.. children.OfType<DirectoryInfo>().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)];
        var trimmed = new List<WalkEntry>();
        for (int i = subDirs.Length - 1; i >= 0; i--)
        {
            WalkEntryKind kind = KindOf(subDirs[i], WalkEntryKind.Directory, asSpelled);
            if (kind == WalkEntryKind.TrimmedName)
                trimmed.Add(new WalkEntry(kind, subDirs[i]));
            else
                pending.Push((subDirs[i], kind == WalkEntryKind.ReparsePoint));
        }
        trimmed.Reverse();
        return trimmed;
    }

    // A link first: it is never entered or opened as what it points to, whatever its name.
    private static WalkEntryKind KindOf(FileSystemInfo child, WalkEntryKind plain, bool asSpelled)
    {
        if (IsReparsePoint(child))
            return WalkEntryKind.ReparsePoint;
        return !asSpelled && Archiver.Core.Services.InputPaths.OwnNameTrimmed(child.Name) ? WalkEntryKind.TrimmedName : plain;
    }

    // T-F410: the children leaveOut keeps; leftOut gets the others, in the walk's order.
    private static FileSystemInfo[] WithoutLeftOut(FileSystemInfo[] children, Func<FileSystemInfo, bool>? leaveOut, out FileSystemInfo[] leftOut)
    {
        if (leaveOut is null)
        {
            leftOut = [];
            return children;
        }
        leftOut = [.. children.Where(leaveOut).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)];
        return [.. children.Except(leftOut)];
    }

    // Null when the listing failed; listingError then says why.
    private static FileSystemInfo[]? List(DirectoryInfo dir, bool leaveOutOsOwned, out Exception? listingError)
    {
        listingError = null;
        try
        {
            // Compatible options (hidden and system entries included), same as the per-type
            // DirectoryInfo overloads this replaces.
            IEnumerable<FileSystemInfo> children = dir.EnumerateFileSystemInfos();
            return leaveOutOsOwned
                ? [.. children.Where(child => (child.Attributes & OsOwned) != OsOwned)]
                : [.. children];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            listingError = ex;
            return null;
        }
    }

    private static bool IsReparsePoint(FileSystemInfo info) =>
        (info.Attributes & FileAttributes.ReparsePoint) != 0;
}
