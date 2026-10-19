using Archiver.Core.IO;

namespace Archiver.Core.Services;

/// <summary>
/// A staging folder owned by one extraction run (T-F263, T-F227): created fresh under a unique
/// name, never reused, and on <see cref="Dispose"/> removes only itself. The old fixed
/// "&lt;dest&gt;_tmp" name reused — and then deleted — a user's own folder of that name.
/// </summary>
internal sealed class ExtractionStaging : IDisposable
{
    private const string NamePrefix = ".pakko-x-";

    private ExtractionStaging(string path)
    {
        Path = path;
        FullPathWithSeparator = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path))
            + System.IO.Path.DirectorySeparatorChar;
    }

    /// <summary>The staging folder.</summary>
    public string Path { get; }

    /// <summary>The full staging path with a trailing separator, for prefix checks.</summary>
    public string FullPathWithSeparator { get; }

    private readonly Dictionary<string, DateTime> _folderTimes = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _renameExisting = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string>? _claimedFinalPaths;
    private readonly List<string> _leftOutThroughLink = [];

    /// <summary>T-F419: what <see cref="CommitInto"/> left out because its path in the destination
    /// passes through a link already there, relative to the destination (a folder ends with a separator).</summary>
    public IReadOnlyList<string> LeftOutThroughLink => _leftOutThroughLink;

    private readonly List<string> _blockedByFolder = [];

    /// <summary>T-F419: files <see cref="CommitInto"/> could not move because a folder of that
    /// name is at the destination, relative to it.</summary>
    public IReadOnlyList<string> BlockedByFolder => _blockedByFolder;

    /// <summary>T-F411: <see cref="Models.ConflictBehavior.RenameExisting"/> - the file at
    /// <paramref name="finalPath"/> is renamed in the commit, just before the staged one takes its
    /// name. <paramref name="claimedFinalPaths"/> is every path this run writes, so the new name is
    /// never one the archive is still to write.</summary>
    public void RenameExistingOnCommit(string finalPath, HashSet<string> claimedFinalPaths)
    {
        _renameExisting.Add(System.IO.Path.GetFullPath(finalPath));
        _claimedFinalPaths = claimedFinalPaths;
    }

    /// <summary>T-F411: both engines' -aot rule for one conflict. Against an earlier entry of this
    /// archive (nothing on disk to rename yet) the new file is renamed instead - <see
    /// cref="Models.ConflictBehavior.Rename"/>; against a file on disk - an earlier archive's of the
    /// same extraction too (T-F420) - it is planned for the commit and the entry keeps its name.
    /// Never the overwrite every other value of the callers' chain falls into.</summary>
    internal Models.ConflictBehavior PlanRenameOfExisting(string finalPath, ExtractionNameRegistry names)
    {
        if (names.ClaimedInThisArchive.Contains(finalPath))
            return Models.ConflictBehavior.Rename;
        RenameExistingOnCommit(finalPath, names.Claimed);
        return Models.ConflictBehavior.RenameExisting;
    }

    /// <summary>T-F298: the time a folder entry gets at the destination, by its path relative to
    /// the destination ("" is the destination itself). Set by <see cref="CommitInto"/> after every
    /// file has moved (moving a file into a folder changes the folder's time), and only on folders
    /// the commit created — a folder the user already had keeps its own time.</summary>
    public void RecordFolderTime(string relativePath, DateTime utc) => _folderTimes[relativePath] = utc;

    /// <summary>Creates a new staging folder inside <paramref name="stagingRoot"/>, which must be
    /// on the same volume as the destination so the commit can rename instead of copy. Staging
    /// folders a killed run left there go first (T-F263).</summary>
    public static ExtractionStaging Create(string stagingRoot)
    {
        TempOwner.SweepStale(stagingRoot, NamePrefix, "", TempScope.Destination);
        string path;
        do
        {
            path = System.IO.Path.Combine(stagingRoot, TempOwner.NewName(NamePrefix));
        } while (Directory.Exists(path) || File.Exists(path));

        DirectoryInfo info = Directory.CreateDirectory(path);
        info.Attributes |= FileAttributes.Hidden;
        return new ExtractionStaging(path);
    }

    // T-F161: fast-path atomic rename if actualDest doesn't exist yet, otherwise a per-file merge
    // so pre-existing files (e.g. skipped/renamed) are preserved. moveOverride exists purely for
    // test injection (defaults to the real Directory.Move).
    //
    // The fast path's Directory.Move fails the WHOLE source tree with IOException — naming only
    // the staging folder, never the specific offending file — whenever ANY file anywhere inside
    // it is transiently held open by another process (real-time antivirus scan, cloud-sync
    // filter, Search Indexer). Confirmed empirically — see DECISIONS.md's T-F161 entry. Falling
    // back to the per-file merge tolerates one still-locked file instead of losing the whole
    // extraction to it.
    //
    // T-F170: a single locked destination file in the merge loop is a per-item failure — returns
    // the relative paths that could not be moved so the caller can record an ArchiveError for
    // each. Catches both IOException AND UnauthorizedAccessException: a real locked-file
    // File.Move(overwrite: true) was confirmed to throw the latter.
    /// <summary>Moves everything staged into <paramref name="actualDest"/>; returns the relative
    /// paths of files that could not be moved. T-F408: <paramref name="committed"/> is called with
    /// the full path of each folder (ending with a separator) and file that arrived, once each —
    /// <paramref name="actualDest"/> itself only when this commit created it.</summary>
    public IReadOnlyList<string> CommitInto(
        string actualDest, Action<string, string>? moveOverride = null, Action<string>? committed = null)
    {
        // T-F298: before anything below creates a folder, or every folder would look pre-existing.
        List<(string Folder, DateTime Utc)> newFolderTimes = [.. _folderTimes
            .Select(t => (Folder: t.Key.Length == 0 ? actualDest : System.IO.Path.Combine(actualDest, t.Key), t.Value))
            .Where(t => !Directory.Exists(t.Folder))
            .OrderByDescending(t => t.Folder.Length)];

        // Listed before the fast path's rename only when someone is to be told; the merge needs them anyway.
        List<string>? folders = committed is null ? null : StagedFolders();
        List<string>? files = committed is null ? null : StagedFiles();

        // A file to rename exists only in a destination that does too; the second test keeps the
        // fast path's whole-folder rename from ever skipping one.
        if (!Directory.Exists(actualDest) && _renameExisting.Count == 0)
        {
            // The rename carries the folder's attributes with it.
            var info = new DirectoryInfo(Path);
            info.Attributes &= ~FileAttributes.Hidden;
            try
            {
                (moveOverride ?? Directory.Move)(Path, actualDest);
                ApplyFolderTimes(newFolderTimes);
                // Named only once the move succeeded: a failed one goes on to the merge below, which names them.
                if (committed is not null && folders is not null && files is not null)
                    NameRenamedTree(actualDest, folders, files, committed);
                return [];
            }
            catch (IOException)
            {
                Directory.CreateDirectory(actualDest);
                committed?.Invoke(FolderName(actualDest));
            }
        }

        // T-F197: folders first, so an empty one arrives too.
        foreach (string folder in folders ?? StagedFolders())
        {
            string finalFolder = Destination(actualDest, folder);
            if (ReachesThroughLink(finalFolder, actualDest))
            {
                _leftOutThroughLink.Add(FolderName(System.IO.Path.GetRelativePath(Path, folder)));
                continue;
            }
            Directory.CreateDirectory(finalFolder);
            committed?.Invoke(FolderName(finalFolder));
        }

        List<string> lockedRelativePaths = MergeFiles(actualDest, files ?? StagedFiles(), committed);

        // A file left behind above failed to move (locked); Dispose removes it with the rest of
        // the staging folder — it cannot reach its destination, and must not be left behind.
        ApplyFolderTimes(newFolderTimes);
        return lockedRelativePaths;
    }

    private void NameRenamedTree(string actualDest, List<string> folders, List<string> files, Action<string> committed)
    {
        committed(FolderName(actualDest));
        foreach (string folder in folders)
            committed(FolderName(Destination(actualDest, folder)));
        foreach (string file in files)
            committed(Destination(actualDest, file));
    }

    // Each file on its own: a locked target fails only itself and is returned, not named.
    private List<string> MergeFiles(string actualDest, List<string> files, Action<string>? committed)
    {
        var lockedRelativePaths = new List<string>();
        foreach (string file in files)
        {
            string relative = System.IO.Path.GetRelativePath(Path, file);
            string finalFile = System.IO.Path.Combine(actualDest, relative);
            if (ReachesThroughLink(System.IO.Path.GetDirectoryName(finalFile)!, actualDest))
            {
                _leftOutThroughLink.Add(relative);
                continue;
            }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(finalFile)!);
            if (_renameExisting.Contains(System.IO.Path.GetFullPath(finalFile)))
            {
                if (!TryMoveInRenamingExisting(file, finalFile))
                {
                    lockedRelativePaths.Add(relative);
                    continue;
                }
            }
            else
            {
                FileAttributes? clearedReadOnly = null;
                try
                {
                    // T-F419: the conflict was decided as an overwrite; 7-Zip replaces a read-only file too.
                    if (File.Exists(finalFile) && File.GetAttributes(finalFile) is var attributes && attributes.HasFlag(FileAttributes.ReadOnly))
                    {
                        File.SetAttributes(finalFile, attributes & ~FileAttributes.ReadOnly);
                        clearedReadOnly = attributes;
                    }
                    File.Move(file, finalFile, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // T-F419: the file that stays keeps the attribute it had.
                    if (clearedReadOnly is { } original)
                        RestoreAttributesBestEffort(finalFile, original);
                    // T-F419: a folder of that name is in the way, which is not a lock.
                    (Directory.Exists(finalFile) ? _blockedByFolder : lockedRelativePaths).Add(relative);
                    continue;
                }
            }
            committed?.Invoke(finalFile);
        }
        return lockedRelativePaths;
    }

    private static void RestoreAttributesBestEffort(string path, FileAttributes attributes)
    {
        try
        {
            File.SetAttributes(path, attributes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            /* best-effort: the move's own failure is what gets reported */
        }
    }

    // T-F411: never an overwrite - the existing file is renamed first, and gets its name back when
    // the new one cannot follow. Should that fail too, it stays under the numbered name, which is
    // what -aot gives it anyway; the new file is reported as not moved in.
    private bool TryMoveInRenamingExisting(string staged, string finalFile)
    {
        string? movedAway = null;
        bool movedFolder = false;
        try
        {
            if (File.Exists(finalFile))
            {
                movedAway = FreeNameBeside(finalFile);
                File.Move(finalFile, movedAway);
            }
            else if (Directory.Exists(finalFile))
            {
                // T-F419: a folder of the entry's name is renamed as a file would be (7-Zip's -aot).
                movedAway = FreeNameBeside(finalFile);
                Directory.Move(finalFile, movedAway);
                movedFolder = true;
            }
            File.Move(staged, finalFile);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (movedAway is not null && !File.Exists(finalFile) && !Directory.Exists(finalFile))
            {
                try
                {
                    if (movedFolder)
                        Directory.Move(movedAway, finalFile);
                    else
                        File.Move(movedAway, finalFile);
                }
                catch (Exception backEx) when (backEx is IOException or UnauthorizedAccessException)
                { /* best-effort: the existing file keeps -aot's numbered name, nothing is lost */ }
            }
            return false;
        }
    }

    // The claimed paths are full paths, while the destination may come as the caller typed it
    // (a relative -o): compare full paths, or a name the archive still writes looks free and the
    // archive's own file later overwrites the one moved there. A folder of that name is taken too.
    private string FreeNameBeside(string finalFile)
    {
        string full = System.IO.Path.GetFullPath(finalFile);
        string folder = System.IO.Path.GetDirectoryName(full)!;
        string name = ArchiveNaming.GetUniqueName(System.IO.Path.GetFileName(full), candidate =>
        {
            string path = System.IO.Path.Combine(folder, candidate);
            return path.Equals(full, StringComparison.OrdinalIgnoreCase) || File.Exists(path) || Directory.Exists(path)
                || (_claimedFinalPaths?.Contains(path) ?? false);
        });
        return System.IO.Path.Combine(folder, name);
    }

    // T-F419: T-F37's check covers the staging folder only; a junction or symlink already inside
    // the destination would carry the commit outside it (7-Zip follows one too). The destination
    // itself may be a link: the user chose it.
    private static bool ReachesThroughLink(string folder, string actualDest)
    {
        int rootLength = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(actualDest)).Length;
        for (string? current = System.IO.Path.GetFullPath(folder);
             current is not null && current.Length > rootLength;
             current = System.IO.Path.GetDirectoryName(current))
        {
            if (ArchiveEntrySecurity.IsReparsePoint(current))
                return true;
        }
        return false;
    }

    private List<string> StagedFolders() => [.. Directory.EnumerateDirectories(Path, "*", SearchOption.AllDirectories)];

    private List<string> StagedFiles() => [.. Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories)];

    private string Destination(string actualDest, string staged) =>
        System.IO.Path.Combine(actualDest, System.IO.Path.GetRelativePath(Path, staged));

    private static string FolderName(string folder) =>
        System.IO.Path.TrimEndingDirectorySeparator(folder) + System.IO.Path.DirectorySeparatorChar;

    // Deepest first: setting a child's time does not touch its parent's, but it keeps the order
    // obviously safe if that ever changes.
    private static void ApplyFolderTimes(List<(string Folder, DateTime Utc)> folderTimes)
    {
        foreach ((string folder, DateTime utc) in folderTimes)
            FileTimes.TrySetDirectory(folder, utc);
    }

    /// <summary>Removes the staging folder (best-effort).</summary>
    public void Dispose()
    {
        try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); } catch { /* best-effort */ }
    }
}
