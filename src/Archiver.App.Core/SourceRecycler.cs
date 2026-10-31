using Archiver.Core.Models;

namespace Archiver.App.Core;

/// <summary>The platform operations <see cref="SourceRecycler"/> needs, behind an interface so
/// its decisions are testable without touching the real Recycle Bin.</summary>
public interface ISourceDeleteOperations
{
    /// <summary>The path with SUBST drives, mapped drives and intermediate links resolved, or null
    /// when it cannot be opened.</summary>
    string? ResolveFinalPath(string path);

    /// <summary>True only when a resolved path lies on a fixed local volume (one with a Recycle
    /// Bin).</summary>
    bool IsOnFixedLocalVolume(string finalPath);

    /// <summary>Moves resolved paths to the Recycle Bin.</summary>
    void MoveToRecycleBin(IReadOnlyList<string> finalPaths);

    /// <summary>Deletes a resolved path permanently; throws on failure.</summary>
    void DeletePermanently(string finalPath);

    /// <summary>Whether a file or folder exists at the path.</summary>
    bool Exists(string path);
}

/// <summary>Why a source "Delete after operation" should have removed is still on disk (T-F435).</summary>
public enum KeepReason
{
    /// <summary>Core did not process all of it: something in it was skipped or failed, or only part was asked for.</summary>
    NotFullyProcessed,

    /// <summary>It holds one of the archives just created.</summary>
    HoldsAnOutput,

    /// <summary>A drive or share root, never deleted.</summary>
    DriveRoot,

    /// <summary>An archive one of whose entries was replaced on disk by another entry of the same name.</summary>
    EntryReplaced,

    /// <summary>Changed since the operation started (T-F419).</summary>
    ChangedSinceStart,

    /// <summary>One of the operation's outputs is no longer on disk, so nothing was deleted (T-F420).</summary>
    OutputMissing,

    /// <summary>Its path could not be resolved, or it holds a link and its drive has no Recycle Bin.</summary>
    NotSafeToDelete,

    /// <summary>Deleting it was tried and it is still there: in use or protected.</summary>
    StillOnDisk,
}

/// <summary>A source left on disk, by the path given, and why.</summary>
public sealed record KeptSource(string Path, KeepReason Reason);

/// <summary>What <see cref="SourceRecycler.DeleteAsync"/> did, by the paths given (T-F302).</summary>
/// <param name="Deleted">Recycled or permanently deleted — gone from disk.</param>
/// <param name="Kept">Should have been deleted but are still on disk; a source the user chose
/// to keep is in neither list.</param>
public sealed record RecycleResult(IReadOnlyList<string> Deleted, IReadOnlyList<KeptSource> Kept)
{
    /// <summary>The paths of <see cref="Kept"/>.</summary>
    public IReadOnlyList<string> NotDeleted => [.. Kept.Select(k => k.Path)];

    /// <summary>Nothing deleted: an output is gone since the run (T-F420 review).</summary>
    public static RecycleResult OutputsMissing(IEnumerable<string> sources) =>
        new([], [.. sources.Select(s => new KeptSource(s, KeepReason.OutputMissing))]);
}

/// <summary>"Delete after operation" (T-F207): recycles sources on a fixed local volume, asks
/// before permanently deleting anything else, and reports what is still on disk.</summary>
public sealed class SourceRecycler(ISourceDeleteOperations ops)
{
    /// <summary>Deletes <paramref name="sources"/>; see <see cref="RecycleResult"/>.
    /// <paramref name="snapshot"/> is what the sources were when the operation started (T-F419): a
    /// source that differs now is kept and reported.
    /// <paramref name="confirmPermanentDeleteAsync"/> gets the sources that cannot go to the
    /// Recycle Bin and returns whether to delete them permanently; it is awaited on the caller's
    /// context, so it may show UI.</summary>
    public async Task<RecycleResult> DeleteAsync(
        IEnumerable<string> sources, ISourceSnapshot snapshot, Func<IReadOnlyList<string>, Task<bool>> confirmPermanentDeleteAsync)
    {
        var deleted = new List<string>();
        (List<KeptSource>? notDeleted, List<(string Source, string Final)>? permanent) = await Task.Run(() => RecycleLocalSources(sources, snapshot, deleted));

        // Declined items are the user's own choice, not a failure — they are not reported.
        if (permanent.Count > 0 && await confirmPermanentDeleteAsync([.. permanent.Select(p => p.Source)]))
            notDeleted.AddRange(await Task.Run(() => DeletePermanently(permanent, deleted)));

        return new RecycleResult(deleted, notDeleted);
    }

    /// <summary>The <paramref name="candidates"/> Core left out of its fully processed sources - with
    /// no error or skip when it holds its own archive or a later entry replaced one of its files
    /// (T-F419). They stay on disk and are reported, as CLI's -sdel reports them, with Core's cause
    /// where it gave one (T-F435).</summary>
    public static IReadOnlyList<KeptSource> NotFullyProcessed(IEnumerable<string> candidates, IReadOnlyList<SourceResult> sources)
    {
        var byPath = new Dictionary<string, SourceResult>(StringComparer.OrdinalIgnoreCase);
        foreach (SourceResult source in sources)
            byPath.TryAdd(Path.TrimEndingDirectorySeparator(source.Path), source);
        var kept = new List<KeptSource>();
        foreach (string path in candidates)
        {
            SourceResult? source = byPath.GetValueOrDefault(Path.TrimEndingDirectorySeparator(path));
            if (source?.Outcome != SourceOutcome.Completed)
                kept.Add(new KeptSource(path, source?.Cause switch
                {
                    PartialCause.HoldsAnOutput => KeepReason.HoldsAnOutput,
                    PartialCause.DriveRoot => KeepReason.DriveRoot,
                    PartialCause.EntryReplaced => KeepReason.EntryReplaced,
                    null => KeepReason.NotFullyProcessed,
                    _ => throw new ArgumentOutOfRangeException(nameof(sources), source.Cause, "A PartialCause with no KeepReason."),
                }));
        }
        return kept;
    }

    /// <summary>The <paramref name="outputs"/> no longer on disk (T-F420 review): the cleanup runs
    /// after the summary, while the user keeps working, and an archive deleted or moved meanwhile
    /// leaves the sources its only copy - nothing is deleted then.</summary>
    public IReadOnlyList<string> MissingOutputs(IEnumerable<string> outputs) => [.. outputs.Where(path => !ops.Exists(path))];

    // Recycles what can be recycled; returns what is still on disk plus what needs a permanent
    // delete.
    private (List<KeptSource> NotDeleted, List<(string Source, string Final)> Permanent) RecycleLocalSources(
        IEnumerable<string> sources, ISourceSnapshot snapshot, List<string> deleted)
    {
        var notDeleted = new List<KeptSource>();
        var recycle = new List<(string Source, string Final)>();
        var permanent = new List<(string Source, string Final)>();

        foreach (string source in sources.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // T-F419: a file written into the source or edited since it was archived is not in
            // the archive; the whole source stays.
            if (!snapshot.Unchanged(source))
            {
                notDeleted.Add(new KeptSource(source, KeepReason.ChangedSinceStart));
                continue;
            }
            // T-F207 spike: the shell's own "permanently delete?" warning never fires for a
            // UNC or SUBST path — it deletes silently. So the decision is made here, on the
            // resolved path, and an unresolvable path is never deleted at all; nor is a volume or
            // share root (T-F419: a SUBST or mapped drive root resolves to one).
            string? final = ops.ResolveFinalPath(source);
            if (final is null)
                notDeleted.Add(new KeptSource(source, KeepReason.NotSafeToDelete));
            else if (IsRoot(final))
                notDeleted.Add(new KeptSource(source, KeepReason.DriveRoot));
            else if (ops.IsOnFixedLocalVolume(final))
                recycle.Add((source, final));
            // T-F419: a permanent recursive delete stops at a link it cannot remove, after the
            // files before it are gone; such a folder only ever goes to the Recycle Bin whole.
            else if (snapshot.HoldsLink(source))
                notDeleted.Add(new KeptSource(source, KeepReason.NotSafeToDelete));
            else
                permanent.Add((source, final));
        }

        if (recycle.Count > 0)
        {
            // T-F419: two spellings of one item are one entry in the delete list.
            try { ops.MoveToRecycleBin([.. recycle.Select(r => r.Final).Distinct(StringComparer.OrdinalIgnoreCase)]); }
            catch { /* reported below: whatever is still on disk */ }
            foreach ((string source, string final) in recycle)
            {
                if (ops.Exists(final))
                    notDeleted.Add(new KeptSource(source, KeepReason.StillOnDisk));
                else
                    deleted.Add(source);
            }
        }

        return (notDeleted, permanent);
    }

    private List<KeptSource> DeletePermanently(List<(string Source, string Final)> permanent, List<string> deleted)
    {
        var notDeleted = new List<KeptSource>();
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string? source, string? final) in permanent)
        {
            if (tried.Add(final))
            {
                try { ops.DeletePermanently(final); }
                catch { /* reported below: still on disk */ }
            }
            if (ops.Exists(final))
                notDeleted.Add(new KeptSource(source, KeepReason.StillOnDisk));
            else
                deleted.Add(source);
        }
        return notDeleted;
    }

    private static bool IsRoot(string finalPath)
    {
        string trimmed = Path.TrimEndingDirectorySeparator(finalPath);
        return Path.GetPathRoot(finalPath) is { } root && string.Equals(Path.TrimEndingDirectorySeparator(root), trimmed, StringComparison.OrdinalIgnoreCase);
    }
}
