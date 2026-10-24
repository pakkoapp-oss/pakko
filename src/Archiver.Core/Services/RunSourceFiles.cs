using Archiver.Core.IO;

namespace Archiver.Core.Services;

/// <summary>
/// T-F421: the files a creation run was given as sources - every one passed in, a source a filter
/// leaves out or a link that is skipped included. An archive the run writes never replaces one of
/// them, under any conflict choice: the engines treat such a name as a same-run collision
/// (<see cref="DestinationConflictResolver"/>), numbered under Overwrite and Rename and skipped under Skip.
/// Asked only in a run's sequential planning, never from parallel workers.
/// </summary>
internal sealed class RunSourceFiles(IReadOnlyList<string> sourcePaths)
{
    private HashSet<string>? _fullPaths;
    private HashSet<PathIdentity.Identity>? _identities;

    /// <summary>Whether <paramref name="destPath"/> is an existing file that is one of the sources,
    /// by any spelling (a link, SUBST, \\?\ - compared by file identity, T-F419). An existing file
    /// whose identity cannot be read counts as one: where it leads is unknown, so it is kept.</summary>
    public bool Contains(string destPath)
    {
        if (!File.Exists(destPath))
            return false;
        // A source Windows cannot resolve is reported per item by the engine, and is no file here.
        _fullPaths ??= new HashSet<string>(
            sourcePaths.Where(p => InputPaths.Problem(p) is null).Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        if (_fullPaths.Contains(Path.GetFullPath(destPath)))
            return true;
        if (PathIdentity.Resolve(destPath) is not { } dest)
            return true;
        // A source whose identity cannot be read is compared by its spelling only, above.
        _identities ??= [.. sourcePaths.Where(File.Exists).Select(PathIdentity.Resolve).OfType<PathIdentity.Resolved>().Select(r => r.Id)];
        return _identities.Contains(dest.Id);
    }
}
