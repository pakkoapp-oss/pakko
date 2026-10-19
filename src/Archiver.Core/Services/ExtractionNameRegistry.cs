namespace Archiver.Core.Services;

/// <summary>
/// T-F420: every path an extraction writes, and what happens when two entries meet on one path -
/// the one bookkeeping both engines share. Claimed is T-F30's set (a duplicate name inside the
/// archive that File.Exists alone cannot see); a meeting of two different names that Windows folds
/// into one (T-F419: "a.txt." and "a.txt", an 8.3 alias) is kept apart as Rename, Overwrite or not;
/// a later entry that replaced an earlier one of the run is recorded, so the archive is not
/// deletable (what reached the disk is not all of it).
/// </summary>
internal sealed class ExtractionNameRegistry(List<string>? replacedInRun)
{
    private readonly HashSet<string> _keptApart = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every final path written so far; also what a new name must avoid
    /// (<see cref="ArchiveNaming.GetUniqueFilePath"/>,
    /// <see cref="ExtractionStaging.PlanRenameOfExisting"/>).</summary>
    public HashSet<string> Claimed { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="finalPath"/> meets an earlier entry that must not be
    /// replaced: either side reached it under another name.</summary>
    public bool MustKeepApart(string finalPath, bool namedDifferently) =>
        Claimed.Contains(finalPath) && (namedDifferently || _keptApart.Contains(finalPath));

    /// <summary>A path where twins meet, known before the first is written (tar's listing).</summary>
    public void KeepApart(string finalPath) => _keptApart.Add(finalPath);

    /// <summary>Records the path an entry is written to; a path already claimed means this entry
    /// replaces an earlier one of the run.</summary>
    public void Claim(string finalPath, bool namedDifferently, string relativePath)
    {
        if (!Claimed.Add(finalPath))
            replacedInRun?.Add(relativePath);
        if (namedDifferently)
            _keptApart.Add(finalPath);
    }
}
