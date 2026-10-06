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

    public static IEnumerable<WalkEntry> Walk(string root)
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

            FileSystemInfo[]? children = null;
            Exception? listingError = null;
            try
            {
                // Compatible options (hidden and system entries included), same as the per-type
                // DirectoryInfo overloads this replaces.
                children = dir.EnumerateFileSystemInfos().ToArray();
                if (isDriveRoot && ReferenceEquals(dir, rootDir))
                    children = [.. children.Where(child => (child.Attributes & OsOwned) != OsOwned)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                listingError = ex;
            }

            if (children is null)
            {
                yield return new WalkEntry(WalkEntryKind.UnreadableDirectory, dir, Error: listingError);
                continue;
            }

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

    private static bool IsReparsePoint(FileSystemInfo info) =>
        (info.Attributes & FileAttributes.ReparsePoint) != 0;
}
