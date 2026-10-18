namespace Archiver.Core.IO;

internal enum WalkEntryKind
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
}

/// <summary>One step of <see cref="DirectoryWalker.Walk"/>.</summary>
/// <param name="Kind">What was found.</param>
/// <param name="Info">A <see cref="FileInfo"/> for a file (or a file-like reparse point), a
/// <see cref="DirectoryInfo"/> otherwise. Its size and attributes come from the directory listing
/// itself, so reading them costs no extra stat call.</param>
/// <param name="IsEmptyDirectory">For <see cref="WalkEntryKind.Directory"/>: the listing held no
/// entries at all (a folder holding only a reparse point is not empty).</param>
/// <param name="Error">For <see cref="WalkEntryKind.UnreadableDirectory"/>: why the listing failed.</param>
internal readonly record struct WalkEntry(
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
internal static class DirectoryWalker
{
    private const FileAttributes OsOwned = FileAttributes.Hidden | FileAttributes.System;

    /// <param name="root">The folder to walk; its own entry comes first.</param>
    /// <param name="leaveOut">T-F410: true for an entry below the root to leave out.</param>
    public static IEnumerable<WalkEntry> Walk(string root, Func<FileSystemInfo, bool>? leaveOut = null)
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

            children = WithoutLeftOut(children, leaveOut, out FileSystemInfo[] leftOut);
            foreach (FileSystemInfo child in leftOut)
                yield return new WalkEntry(WalkEntryKind.LeftOut, child);

            yield return new WalkEntry(WalkEntryKind.Directory, dir, IsEmptyDirectory: children.Length == 0);

            foreach (FileInfo file in children.OfType<FileInfo>().OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase))
            {
                yield return IsReparsePoint(file)
                    ? new WalkEntry(WalkEntryKind.ReparsePoint, file)
                    : new WalkEntry(WalkEntryKind.File, file);
            }

            DirectoryInfo[] subDirs = [.. children.OfType<DirectoryInfo>().OrderBy(d => d.FullName, StringComparer.OrdinalIgnoreCase)];
            for (int i = subDirs.Length - 1; i >= 0; i--)
                pending.Push((subDirs[i], IsReparsePoint(subDirs[i])));
        }
    }

    // T-F410: the children leaveOut keeps; leftOut gets the others, in the walk's order.
    private static FileSystemInfo[] WithoutLeftOut(FileSystemInfo[] children, Func<FileSystemInfo, bool>? leaveOut, out FileSystemInfo[] leftOut)
    {
        if (leaveOut is null)
        {
            leftOut = [];
            return children;
        }
        leftOut = [.. children.Where(leaveOut).OrderBy(c => c.FullName, StringComparer.OrdinalIgnoreCase)];
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
