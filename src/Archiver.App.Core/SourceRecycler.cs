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

/// <summary>"Delete after operation" (T-F207): recycles sources on a fixed local volume, asks
/// before permanently deleting anything else, and reports what is still on disk.</summary>
public sealed class SourceRecycler(ISourceDeleteOperations ops)
{
    /// <summary>Deletes <paramref name="sources"/>; returns the ones still on disk afterwards (by
    /// the path given). <paramref name="confirmPermanentDeleteAsync"/> gets the sources that cannot
    /// go to the Recycle Bin and returns whether to delete them permanently; it is awaited on the
    /// caller's context, so it may show UI.</summary>
    public async Task<IReadOnlyList<string>> DeleteAsync(
        IEnumerable<string> sources, Func<IReadOnlyList<string>, Task<bool>> confirmPermanentDeleteAsync)
    {
        var notDeleted = new List<string>();
        var recycle = new List<(string Source, string Final)>();
        var permanent = new List<(string Source, string Final)>();

        await Task.Run(() =>
        {
            foreach (string source in sources.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // T-F207 spike: the shell's own "permanently delete?" warning never fires for a
                // UNC or SUBST path — it deletes silently. So the decision is made here, on the
                // resolved path, and an unresolvable path is never deleted at all.
                string? final = ops.ResolveFinalPath(source);
                if (final is null)
                    notDeleted.Add(source);
                else if (ops.IsOnFixedLocalVolume(final))
                    recycle.Add((source, final));
                else
                    permanent.Add((source, final));
            }

            if (recycle.Count > 0)
            {
                try { ops.MoveToRecycleBin([.. recycle.Select(r => r.Final)]); }
                catch { /* reported below: whatever is still on disk */ }
                notDeleted.AddRange(recycle.Where(r => ops.Exists(r.Final)).Select(r => r.Source));
            }
        });

        if (permanent.Count > 0)
        {
            if (!await confirmPermanentDeleteAsync([.. permanent.Select(p => p.Source)]))
            {
                notDeleted.AddRange(permanent.Select(p => p.Source));
            }
            else
            {
                await Task.Run(() =>
                {
                    foreach (var (source, final) in permanent)
                    {
                        try { ops.DeletePermanently(final); }
                        catch { /* reported below: still on disk */ }
                        if (ops.Exists(final))
                            notDeleted.Add(source);
                    }
                });
            }
        }

        return notDeleted;
    }
}
