namespace Archiver.Core.Services;

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
    private readonly HashSet<string> _keptApart = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _owner = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _notWhollyOnDisk = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The archive being extracted; setting it starts <see cref="ClaimedInThisArchive"/> afresh.</summary>
    public string CurrentArchive
    {
        get;
        set
        {
            field = value;
            ClaimedInThisArchive.Clear();
        }
    } = string.Empty;

    /// <summary>Every final path written so far; also what a new name must avoid
    /// (<see cref="ArchiveNaming.GetUniqueFilePath"/>, <see cref="ExtractionStaging.RenameExistingOnCommit"/>).</summary>
    public HashSet<string> Claimed { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The paths <see cref="CurrentArchive"/> wrote. -aot asks this: an earlier archive's file
    /// is on disk, and it is that file that is renamed (<see cref="ExtractionStaging.PlanRenameOfExisting"/>).</summary>
    public HashSet<string> ClaimedInThisArchive { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="finalPath"/> meets an earlier entry that must not be
    /// replaced: either side reached it under another name.</summary>
    public bool MustKeepApart(string finalPath, bool namedDifferently) =>
        Claimed.Contains(finalPath) && (namedDifferently || _keptApart.Contains(finalPath));

    /// <summary>A path where twins meet, known before the first is written (tar's listing).</summary>
    public void KeepApart(string finalPath) => _keptApart.Add(finalPath);

    /// <summary>Records the path an entry of <see cref="CurrentArchive"/> is written to; a path
    /// already claimed means the archive that claimed it lost that file.</summary>
    public void Claim(string finalPath, bool namedDifferently)
    {
        if (!Claimed.Add(finalPath))
            _notWhollyOnDisk.Add(_owner[finalPath]);
        _owner[finalPath] = CurrentArchive;
        ClaimedInThisArchive.Add(finalPath);
        if (namedDifferently)
            _keptApart.Add(finalPath);
    }

    /// <summary>Whether an entry of <paramref name="archivePath"/> was replaced by a later one, so
    /// what is on disk is not all of it.</summary>
    public bool NotWhollyOnDisk(string archivePath) => _notWhollyOnDisk.Contains(archivePath);
}
