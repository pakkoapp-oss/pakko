using Archiver.Core.IO;

namespace Archiver.Core.Services;

/// <summary>
/// T-F392: two names of one file among the files one tar.exe run packs. tar.exe stores the second
/// as a hard-link entry, which Pakko refuses to extract, and has no switch to store the content
/// instead. A shared identity in the folder listings only nominates a pair; both names are opened
/// to confirm it, so a volume whose listing reports no usable identity refuses nothing.
/// </summary>
internal static class TarHardLinkPairs
{
    /// <summary>The first two names of one file, in <paramref name="files"/>' order; null when none.</summary>
    public static (string First, string Second)? Find(IEnumerable<string> files) =>
        Find(files, PathIdentity.FilesIn, SameFile);

    internal static (string First, string Second)? Find(
        IEnumerable<string> files,
        Func<string, IReadOnlyDictionary<string, PathIdentity.Identity>?> listFolder,
        Func<string, string, bool> sameFile)
    {
        var listings = new Dictionary<string, IReadOnlyDictionary<string, PathIdentity.Identity>?>(StringComparer.OrdinalIgnoreCase);
        var seen = new Dictionary<PathIdentity.Identity, string>();
        foreach (string file in files)
        {
            if (Path.GetDirectoryName(file) is not { } folder)
                continue;
            if (!listings.TryGetValue(folder, out IReadOnlyDictionary<string, PathIdentity.Identity>? listing))
            {
                listing = listFolder(folder);
                listings[folder] = listing;
            }
            if (listing is null || !listing.TryGetValue(Path.GetFileName(file), out PathIdentity.Identity id)
                || id is { Low: 0, High: 0 })
                continue;
            if (!seen.TryAdd(id, file) && sameFile(seen[id], file))
                return (seen[id], file);
        }
        return null;
    }

    private static bool SameFile(string first, string second) =>
        PathIdentity.Resolve(first) is { } a && PathIdentity.Resolve(second) is { } b
        && a.Id == b.Id && !string.Equals(a.FinalPath, b.FinalPath, StringComparison.OrdinalIgnoreCase);
}
