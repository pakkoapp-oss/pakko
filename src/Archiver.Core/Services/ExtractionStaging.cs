using Archiver.Core.IO;
using Archiver.Core.Services.Sandbox;

namespace Archiver.Core.Services;

/// <summary>
/// A staging folder owned by one extraction run (T-F263, T-F227): created fresh under a unique
/// name, never reused, and on <see cref="Dispose"/> removes only itself. The old fixed
/// "&lt;dest&gt;_tmp" name reused — and then deleted — a user's own folder of that name. T-F528: it is
/// a role of the run's <see cref="WorkRoot"/>, which goes with it.
/// </summary>
internal sealed class ExtractionStaging : IDisposable
{
    private readonly WorkRoot _work;

    private ExtractionStaging(WorkRoot work, string path)
    {
        _work = work;
        Path = path;
        FullPathWithSeparator = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path))
            + System.IO.Path.DirectorySeparatorChar;
    }

    /// <summary>The staging folder.</summary>
    public string Path { get; }

    /// <summary>The full staging path with a trailing separator, for prefix checks.</summary>
    public string FullPathWithSeparator { get; }

    private readonly Dictionary<string, DateTime> _folderTimes = [with(StringComparer.OrdinalIgnoreCase)];

    private readonly HashSet<string> _renameExisting = [with(StringComparer.OrdinalIgnoreCase)];
    private ExtractionNameRegistry? _names;
    private readonly List<string> _leftOutThroughLink = [];

    /// <summary>T-F419: what <see cref="CommitInto"/> left out because its path in the destination
    /// passes through a link already there, relative to the destination (a folder ends with a separator).</summary>
    public IReadOnlyList<string> LeftOutThroughLink => _leftOutThroughLink;

    private readonly List<string> _leftOutUnreadable = [];

    private readonly List<string> _permissionsNotApplied = [];

    /// <summary>T-F532: files that arrived in a folder that passes down other entries than staging's,
    /// and whose inheritance could not be written again, relative to the destination.</summary>
    public IReadOnlyList<string> PermissionsNotApplied => _permissionsNotApplied;

    /// <summary>T-F522: what <see cref="CommitInto"/> left out because a folder on its path in the
    /// destination could not be told from a link (its attributes could not be read), as
    /// <see cref="LeftOutThroughLink"/> names it.</summary>
    public IReadOnlyList<string> LeftOutUnreadable => _leftOutUnreadable;

    private readonly List<string> _blockedByFolder = [];

    /// <summary>T-F419: files <see cref="CommitInto"/> could not move because a folder of that
    /// name is at the destination, relative to it.</summary>
    public IReadOnlyList<string> BlockedByFolder => _blockedByFolder;

    /// <summary>T-F411: <see cref="Models.ConflictBehavior.RenameExisting"/> - the file at
    /// <paramref name="finalPath"/> is renamed in the commit, just before the staged one takes its
    /// name. <paramref name="names"/> holds every path this run writes, so the new name is never one
    /// the archive is still to write (T-F570: its <see cref="ExtractionNameRegistry.IsTaken"/>).</summary>
    public void RenameExistingOnCommit(string finalPath, ExtractionNameRegistry names)
    {
        _renameExisting.Add(System.IO.Path.GetFullPath(finalPath));
        _names = names;
    }

    /// <summary>T-F570: moves <paramref name="stagedFile"/> out of the staging tree into the work root
    /// (role "k", the same volume), where no entry's name can meet it; returns where it is. It goes
    /// with the work root when the run ends before it is placed.</summary>
    public string HoldAside(string stagedFile)
    {
        string held = _work.NewFile("k", string.Empty);
        File.Move(stagedFile, held);
        return held;
    }

    /// <summary>T-F298: the time a folder entry gets at the destination, by its path relative to
    /// the destination ("" is the destination itself). Set by <see cref="CommitInto"/> after every
    /// file has moved (moving a file into a folder changes the folder's time), and only on folders
    /// the commit created — a folder the user already had keeps its own time.</summary>
    public void RecordFolderTime(string relativePath, DateTime utc) => _folderTimes[relativePath] = utc;

    /// <summary>Creates a new staging folder in the work root next to <paramref name="destination"/>
    /// (<see cref="WorkRoot.Near"/>), on its volume, so the commit renames instead of copying.</summary>
    public static ExtractionStaging Create(string destination)
    {
        var work = WorkRoot.Near(destination);
        try
        {
            return new ExtractionStaging(work, work.Folder("x"));
        }
        catch
        {
            work.Dispose();
            throw;
        }
    }

    // T-F161: fast-path atomic rename if actualDest doesn't exist yet, otherwise a merge so
    // pre-existing files (e.g. skipped/renamed) are preserved: T-F565, a folder not in actualDest
    // yet moves whole by one rename where its permissions allow, everything else file by file.
    // moveOverride exists purely for test injection (defaults to the real Directory.Move), and
    // serves the whole-folder renames of both.
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
            try
            {
                // The work root may be above the destination's folder (T-F528): none of it exists yet.
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(actualDest))!);
                (moveOverride ?? Directory.Move)(Path, actualDest);
                _work.TakeParentEntriesOfFolder(actualDest);
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

        HashSet<string> movedWhole = CommitFoldersNamingMoved(actualDest, folders, files, newFolderTimes, moveOverride, committed);
        List<string> lockedRelativePaths = MergeFiles(
            actualDest, movedWhole.Count == 0 ? files ?? StagedFiles() : [.. (files ?? StagedFiles()).Where(f => !BelowAny(movedWhole, f))], committed);

        // A file left behind above failed to move (locked); Dispose removes it with the rest of
        // the staging folder — it cannot reach its destination, and must not be left behind.
        ApplyFolderTimes(newFolderTimes);
        return lockedRelativePaths;
    }

    private HashSet<string> CommitFoldersNamingMoved(
        string actualDest, List<string>? folders, List<string>? files, List<(string Folder, DateTime Utc)> newFolderTimes,
        Action<string, string>? moveOverride, Action<string>? committed)
    {
        var movedWhole = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            CommitFolders(actualDest, folders ?? StagedFolders(), moveOverride, committed, movedWhole);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // T-F565: a folder that cannot be created ends the commit, as before; what moved whole
            // ahead of it has arrived, so it keeps its folder times and is named below.
            ApplyFolderTimes(newFolderTimes);
            throw;
        }
        finally
        {
            if (committed is not null && folders is not null && files is not null && movedWhole.Count > 0)
                NameMovedWhole(actualDest, movedWhole, folders, files, committed);
        }
        return movedWhole;
    }

    // T-F197: folders first, so an empty one arrives too; listed parents before children. T-F565: a
    // folder not in the destination yet moves whole, as the fast path moves staging. Returns the staged
    // folders moved whole into movedWhole as they go; what is below them has arrived with them. No file
    // to rename away (T-F411) is ever below a folder not in the destination: such a file is there.
    private void CommitFolders(
        string actualDest, List<string> folders, Action<string, string>? moveOverride, Action<string>? committed, HashSet<string> movedWhole)
    {
        var passesDown = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in folders)
        {
            if (BelowAny(movedWhole, folder))
                continue;
            string finalFolder = Destination(actualDest, folder);
            if (LeftOut(finalFolder, actualDest, FolderName(System.IO.Path.GetRelativePath(Path, folder))))
                continue;
            if (!Directory.Exists(finalFolder) && MayMoveWhole(folder, finalFolder, passesDown) && TryMoveWhole(folder, finalFolder, moveOverride))
                movedWhole.Add(System.IO.Path.TrimEndingDirectorySeparator(folder));
            else
                Directory.CreateDirectory(finalFolder);
            committed?.Invoke(FolderName(finalFolder));
        }
    }

    // Whether a staged path is below a folder moved whole; walks up to staging, so bounded by the path's depth.
    private bool BelowAny(HashSet<string> movedWhole, string staged)
    {
        if (movedWhole.Count == 0)
            return false;
        for (string? current = System.IO.Path.GetDirectoryName(staged);
             current is not null && current.Length >= Path.Length;
             current = System.IO.Path.GetDirectoryName(current))
        {
            if (movedWhole.Contains(current))
                return true;
        }
        return false;
    }

    // T-F565: a rename keeps the DACLs of everything moved (T-F532), so a folder moves whole only where
    // its folder in the destination passes down what its folder in staging did. Each parent is read once.
    private static bool MayMoveWhole(string stagedFolder, string finalFolder, Dictionary<string, string?> passesDown)
    {
        string finalFull = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(finalFolder));
        if (System.IO.Path.GetDirectoryName(stagedFolder) is not { } stagedParent || System.IO.Path.GetDirectoryName(finalFull) is not { } finalParent)
            return false;
        string? stagedGives = PassesDown(stagedParent, passesDown);
        return stagedGives is not null && stagedGives == PassesDown(finalParent, passesDown);
    }

    // What a new child folder takes from its parent: the inheritable entries and integrity label, and
    // the Encrypted and Compressed state. Null when any of it cannot be read; audit entries need a
    // privilege Pakko does not hold and are not compared (a rename keeps a file's own in either path).
    private static string? PassesDown(string folder, Dictionary<string, string?> known)
    {
        if (known.TryGetValue(folder, out string? cached))
            return cached;
        string literal = InputPaths.Literal(folder);
        string? result = null;
        if (QuarantineAcl.InheritableEntries(literal) is { } entries)
        {
            try
            {
                result = entries + "|" + (File.GetAttributes(literal) & (FileAttributes.Encrypted | FileAttributes.Compressed));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // unreadable: the folder is merged file by file, which needs none of it
            }
        }
        known[folder] = result;
        return result;
    }

    // T-F161: a file held open anywhere below fails the whole rename; the folder is then merged.
    private static bool TryMoveWhole(string stagedFolder, string finalFolder, Action<string, string>? moveOverride)
    {
        try
        {
            (moveOverride ?? Directory.Move)(stagedFolder, finalFolder);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // T-F408: what arrived inside the folders moved whole, each once; the folders themselves are named
    // as they move.
    private void NameMovedWhole(string actualDest, HashSet<string> movedWhole, List<string> folders, List<string> files, Action<string> committed)
    {
        foreach (string folder in folders.Where(f => BelowAny(movedWhole, f)))
            committed(FolderName(Destination(actualDest, folder)));
        foreach (string file in files.Where(f => BelowAny(movedWhole, f)))
            committed(Destination(actualDest, file));
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
    // T-F532: a rename keeps the file's DACL, which staging's folder gave it; a file whose folder here
    // passes down other entries has its inheritance written again. Decided once a folder: the walk
    // yields a folder's files one after another, so the last folder's verdict is all that is kept.
    private List<string> MergeFiles(string actualDest, List<string> files, Action<string>? committed)
    {
        var lockedRelativePaths = new List<string>();
        var entries = new FolderEntries(Path);
        foreach (string file in files)
        {
            string relative = System.IO.Path.GetRelativePath(Path, file);
            string finalFile = System.IO.Path.Combine(actualDest, relative);
            if (LeftOut(System.IO.Path.GetDirectoryName(finalFile)!, actualDest, relative))
                continue;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(finalFile)!);
            if (_renameExisting.Contains(System.IO.Path.GetFullPath(finalFile)))
            {
                if (!TryMoveInRenamingExisting(file, finalFile))
                {
                    lockedRelativePaths.Add(relative);
                    continue;
                }
            }
            else if (!TryMoveOverwriting(file, finalFile))
            {
                // T-F419: a folder of that name is in the way, which is not a lock.
                (Directory.Exists(finalFile) ? _blockedByFolder : lockedRelativePaths).Add(relative);
                continue;
            }
            if (entries.Differ(System.IO.Path.GetDirectoryName(finalFile)!) && !TryInheritOnly(InputPaths.Literal(finalFile), entries.GivesNothing))
                _permissionsNotApplied.Add(relative);
            committed?.Invoke(finalFile);
        }
        return lockedRelativePaths;
    }

    // T-F532: whether a folder passes down other entries to its files than staging does; the last
    // folder's verdict is kept, staging's entries are read once.
    private sealed class FolderEntries(string staging)
    {
        private string? _stagingGives;
        private string? _lastFolder;
        private bool _lastDiffers;

        /// <summary>T-F555: the last folder passes no entry to its files (read, and empty).</summary>
        public bool GivesNothing { get; private set; }

        public bool Differ(string folder)
        {
            if (string.Equals(folder, _lastFolder, StringComparison.OrdinalIgnoreCase))
                return _lastDiffers;
            _lastFolder = folder;
            _stagingGives ??= QuarantineAcl.FileInheritableEntries(InputPaths.Literal(staging)) ?? "";
            // Unread is not the same: the file then takes its folder's entries all the same.
            string? folderGives = QuarantineAcl.FileInheritableEntries(InputPaths.Literal(folder));
            GivesNothing = folderGives is { Length: 0 };
            _lastDiffers = folderGives is null || folderGives != _stagingGives;
            return _lastDiffers;
        }
    }

    // T-F532: not swallowed - the file would keep staging's entries in a folder that may give fewer.
    private static bool TryInheritOnly(string file, bool folderGivesNothing)
    {
        try
        {
            QuarantineAcl.InheritOnly(file, folderGivesNothing);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    // T-F419: the conflict was decided as an overwrite; 7-Zip replaces a read-only file too. On a
    // failure the file that stays keeps the attribute it had.
    private static bool TryMoveOverwriting(string staged, string finalFile)
    {
        FileAttributes? clearedReadOnly = null;
        try
        {
            if (File.Exists(finalFile) && File.GetAttributes(finalFile) is var attributes && attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(finalFile, attributes & ~FileAttributes.ReadOnly);
                clearedReadOnly = attributes;
            }
            File.Move(staged, finalFile, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (clearedReadOnly is { } original)
                RestoreAttributesBestEffort(finalFile, original);
            return false;
        }
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
                || (_names?.IsTaken(path) ?? false);
        });
        return System.IO.Path.Combine(folder, name);
    }

    // Whether an item whose folder in the destination is folder stays out, and on which list.
    private bool LeftOut(string folder, string actualDest, string relative)
    {
        bool? link = ReachesThroughLink(folder, actualDest);
        if (link == false)
            return false;
        (link == true ? _leftOutThroughLink : _leftOutUnreadable).Add(relative);
        return true;
    }

    // T-F419: T-F37's check covers the staging folder only; a junction or symlink already inside
    // the destination would carry the commit outside it (7-Zip follows one too). The destination
    // itself may be a link: the user chose it. T-F522: null when a folder on the way may be one.
    private static bool? ReachesThroughLink(string folder, string actualDest)
    {
        int rootLength = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(actualDest)).Length;
        for (string? current = System.IO.Path.GetFullPath(folder);
             current is not null && current.Length > rootLength;
             current = System.IO.Path.GetDirectoryName(current))
        {
            bool? link = ArchiveEntrySecurity.IsReparsePointOrUnknown(current);
            if (link != false)
                return link;
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

    /// <summary>Removes the staging folder with its work root (best-effort).</summary>
    public void Dispose() => _work.Dispose();
}
