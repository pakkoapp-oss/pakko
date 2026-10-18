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

    /// <summary>T-F411: <see cref="Models.ConflictBehavior.RenameExisting"/> - the file at
    /// <paramref name="finalPath"/> is renamed in the commit, just before the staged one takes its
    /// name. <paramref name="claimedFinalPaths"/> is every path this run writes, so the new name is
    /// never one the archive is still to write.</summary>
    public void RenameExistingOnCommit(string finalPath, HashSet<string> claimedFinalPaths)
    {
        _renameExisting.Add(System.IO.Path.GetFullPath(finalPath));
        _claimedFinalPaths = claimedFinalPaths;
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
                try
                {
                    File.Move(file, finalFile, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    lockedRelativePaths.Add(relative);
                    continue;
                }
            }
            committed?.Invoke(finalFile);
        }
        return lockedRelativePaths;
    }

    // T-F411: never an overwrite - the existing file is renamed first, and gets its name back when
    // the new one cannot follow. Should that fail too, it stays under the numbered name, which is
    // what -aot gives it anyway; the new file is reported as not moved in.
    private bool TryMoveInRenamingExisting(string staged, string finalFile)
    {
        string? movedAway = null;
        try
        {
            if (File.Exists(finalFile))
            {
                movedAway = ArchiveNaming.GetUniqueFilePath(finalFile, _claimedFinalPaths);
                File.Move(finalFile, movedAway);
            }
            File.Move(staged, finalFile);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (movedAway is not null && !File.Exists(finalFile))
            {
                try { File.Move(movedAway, finalFile); }
                catch (Exception backEx) when (backEx is IOException or UnauthorizedAccessException)
                { /* best-effort: the existing file keeps -aot's numbered name, nothing is lost */ }
            }
            return false;
        }
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
