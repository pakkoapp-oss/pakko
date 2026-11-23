using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>T-F570: one item to place. <paramref name="NamedDifferently"/>: the item reached
/// <paramref name="FinalPath"/> under another name (a part Windows trims, an 8.3 alias), so an
/// earlier item there is never replaced by it; <paramref name="NamesAnother"/>: its name is not the
/// name of what is on disk there, so a file already there is not replaced either.
/// <paramref name="Incoming"/> describes it for the conflict question, asked only when one is.</summary>
internal sealed record PlaceRequest(string FinalPath, bool NamedDifferently, bool NamesAnother,
    Func<(long? Size, DateTimeOffset? Modified)> Incoming);

/// <summary>T-F570: where an item goes (null: skipped), and why it was skipped.
/// <paramref name="EarlierRenamedAway"/>: -aot renames what is there - in the commit, or, with no
/// staging, to <paramref name="MovedAsideTo"/> before the item is written.</summary>
internal sealed record Placement(string? Path, bool EarlierRenamedAway = false, bool SkippedByUser = false, bool SkippedInRun = false,
    string? MovedAsideTo = null);

/// <summary>
/// T-F420: every path one extraction writes, over all its archives and both engines (the router
/// hands ZIP and tar archives to two calls, sharing one of these), and what happens when two
/// entries meet on one path - the one bookkeeping both engines share. Claimed is T-F30's set (a
/// duplicate name that File.Exists alone cannot see); a meeting of two different names that
/// Windows writes as one (T-F419: "a.txt." and "a.txt", "a?b" and "a_b", an 8.3 alias) is kept
/// apart as Rename, Overwrite or not; an entry that replaced an earlier one marks the archive the
/// earlier came from as not wholly on disk, so it is not deletable - in one archive (a.txt twice)
/// and across archives into one folder (x *.zip, Explorer's "Extract here").
/// </summary>
internal sealed class ExtractionNameRegistry
{
    private readonly HashSet<string> _keptApart = [with(StringComparer.OrdinalIgnoreCase)];
    private readonly Dictionary<string, string> _owner = [with(StringComparer.OrdinalIgnoreCase)];
    private readonly HashSet<string> _notWhollyOnDisk = [with(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The archive being extracted; setting it starts the paths it wrote afresh.</summary>
    public string CurrentArchive
    {
        get;
        set
        {
            field = value;
            _claimedInThisArchive.Clear();
        }
    } = string.Empty;

    // Every file path written so far (T-F30: a duplicate name File.Exists alone cannot see).
    private readonly HashSet<string> _claimed = [with(StringComparer.OrdinalIgnoreCase)];

    // The paths CurrentArchive wrote. -aot asks this: an earlier archive's file is on disk, and it is
    // that file that is renamed.
    private readonly HashSet<string> _claimedInThisArchive = [with(StringComparer.OrdinalIgnoreCase)];

    // T-F570: every folder written so far. Only a new name avoids them; a file meeting one is not a
    // conflict here (the commit reports a folder in the way, as before).
    private readonly HashSet<string> _folders = [with(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Whether <paramref name="finalPath"/> meets an earlier entry that must not be
    /// replaced: either side reached it under another name.</summary>
    internal bool MustKeepApart(string finalPath, bool namedDifferently) =>
        _claimed.Contains(finalPath) && (namedDifferently || _keptApart.Contains(finalPath));

    /// <summary>Whether <see cref="CurrentArchive"/> wrote <paramref name="finalPath"/>: -aot then
    /// renames the new file, as nothing of it is on disk yet.</summary>
    internal bool WrittenByCurrentArchive(string finalPath) => _claimedInThisArchive.Contains(finalPath);

    /// <summary>A path where twins meet, known before the first is written (tar's listing).</summary>
    public void KeepApart(string finalPath) => _keptApart.Add(finalPath);

    /// <summary>T-F570: whether a new name must avoid <paramref name="fullPath"/>: this run writes
    /// there.</summary>
    public bool IsTaken(string fullPath) => _claimed.Contains(fullPath) || _folders.Contains(fullPath);

    /// <summary>T-F570: a folder this run writes, by its final path.</summary>
    public void ClaimFolder(string folderPath) => _folders.Add(Path.TrimEndingDirectorySeparator(folderPath));

    /// <summary>T-F570: the folders above <paramref name="filePath"/>, up to <paramref name="root"/>.</summary>
    public void ClaimParents(string filePath, string root)
    {
        string top = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        for (string? folder = Path.GetDirectoryName(filePath);
             folder is not null && folder.Length > top.Length && _folders.Add(folder);
             folder = Path.GetDirectoryName(folder))
        {
            // each parent claimed by the condition; one already claimed has its own parents claimed
        }
    }

    /// <summary>
    /// T-F496, T-F570: the name of a kept damaged file, placed after every entry of its archive:
    /// "name.damaged" (<see cref="EntryDiskName.Damaged"/>), numbered past any file or folder there,
    /// any path this run writes, and any name <paramref name="stagedThere"/> holds. Never asked about
    /// and never over anything: it is claimed as named apart, so a later item of its name is renamed.
    /// </summary>
    public string PlaceKept(string finalPath, Func<string, bool> stagedThere)
    {
        string folder = Path.GetDirectoryName(finalPath)!;
        string name = ArchiveNaming.GetUniqueName(EntryDiskName.Damaged(Path.GetFileName(finalPath)), candidate =>
        {
            string full = Path.Combine(folder, candidate);
            return File.Exists(full) || Directory.Exists(full) || IsTaken(full) || stagedThere(candidate);
        });
        Claim(Path.Combine(folder, name), namedDifferently: true);
        return name;
    }

    /// <summary>
    /// T-F570: the one decision of where an extracted file goes, for every engine: the path it was
    /// meant for when nothing is there; a numbered one when it must be kept apart (T-F419, T-F420);
    /// else what the conflict mode or the user says (T-F06), -aot through <paramref name="staging"/>
    /// (T-F411), or, with none (the bare gzip engine writes straight to the destination), to a free
    /// name the caller moves it to. The path is claimed before it is returned.
    /// </summary>
    public async Task<Placement> PlaceAsync(PlaceRequest request, ConflictResolver resolver, ExtractionStaging? staging)
    {
        string finalPath = request.FinalPath;
        bool inRun = _claimed.Contains(finalPath);
        ConflictBehavior decision = ConflictBehavior.Overwrite;
        if (!File.Exists(finalPath) && !inRun && !(resolver.RenamesExisting && Directory.Exists(finalPath)))
        {
            // nothing there: the path as meant
        }
        else if (MustKeepApart(finalPath, request.NamedDifferently) || (request.NamesAnother && !inRun))
        {
            decision = ConflictBehavior.Rename;
        }
        else
        {
            (long? size, DateTimeOffset? modified) = request.Incoming();
            int userSkipsSoFar = resolver.UserSkipCount;
            decision = await resolver.ResolveAsync(finalPath, size, modified).ConfigureAwait(false);
            // T-F216: whether the user said so; T-F415: a path this run wrote is not one that was there.
            if (decision == ConflictBehavior.Skip)
                return new Placement(null, SkippedByUser: resolver.UserSkipCount != userSkipsSoFar, SkippedInRun: inRun);
            if (decision == ConflictBehavior.RenameExisting)
                decision = PlanRenameOfExisting(finalPath, staging);
        }
        string path = decision == ConflictBehavior.Rename ? UniquePath(finalPath) : finalPath;
        bool earlierRenamedAway = decision == ConflictBehavior.RenameExisting;
        string? movedAsideTo = null;
        if (earlierRenamedAway && staging is null)
        {
            movedAsideTo = UniquePath(finalPath);
            Claim(movedAsideTo, namedDifferently: false);
        }
        Claim(path, request.NamedDifferently, earlierRenamedAway);
        return new Placement(path, earlierRenamedAway, MovedAsideTo: movedAsideTo);
    }

    // T-F411: against an earlier entry of this archive (nothing on disk to rename yet) the new file
    // is renamed instead; against a file on disk - an earlier archive's of the same extraction too
    // (T-F420) - it is planned for the commit and the entry keeps its name. Never an overwrite.
    private ConflictBehavior PlanRenameOfExisting(string finalPath, ExtractionStaging? staging)
    {
        if (_claimedInThisArchive.Contains(finalPath))
            return ConflictBehavior.Rename;
        staging?.RenameExistingOnCommit(finalPath, this);
        return ConflictBehavior.RenameExisting;
    }

    // Always numbered, even when the name itself is free: only a conflict comes here.
    private string UniquePath(string finalPath)
    {
        string folder = Path.GetDirectoryName(finalPath)!;
        string name = Path.GetFileName(finalPath);
        return Path.Combine(folder, ArchiveNaming.GetUniqueName(name, candidate =>
        {
            string full = Path.Combine(folder, candidate);
            return candidate == name || File.Exists(full) || IsTaken(full);
        }));
    }

    /// <summary>Records the path an entry of <see cref="CurrentArchive"/> is written to; a path
    /// already claimed means the archive that claimed it lost that file - unless -aot renames that
    /// file away (<paramref name="earlierRenamedAway"/>), which keeps it on disk.</summary>
    public void Claim(string finalPath, bool namedDifferently, bool earlierRenamedAway = false)
    {
        if (!_claimed.Add(finalPath) && !earlierRenamedAway)
            _notWhollyOnDisk.Add(_owner[finalPath]);
        _owner[finalPath] = CurrentArchive;
        _claimedInThisArchive.Add(finalPath);
        if (namedDifferently)
            _keptApart.Add(finalPath);
    }

    /// <summary>Whether an entry of <paramref name="archivePath"/> was replaced by a later one, so
    /// what is on disk is not all of it.</summary>
    public bool NotWhollyOnDisk(string archivePath) => _notWhollyOnDisk.Contains(archivePath);
}
