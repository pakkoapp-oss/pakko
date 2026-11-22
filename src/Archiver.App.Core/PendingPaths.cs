using Archiver.Core.Services;

namespace Archiver.App.Core;

/// <summary>
/// T-F278: which dropped or picked paths join the pending list. Windows paths are not
/// case-sensitive, so <c>C:\a\File.txt</c> and <c>c:\a\file.txt</c> are the same item.
/// </summary>
public static class PendingPaths
{
    /// <summary>
    /// Splits <paramref name="incoming"/> into the paths to add, in their order, and the count of
    /// those already listed (or repeated within <paramref name="incoming"/>).
    /// </summary>
    public static (IReadOnlyList<string> Added, int AlreadyListed) Split(IEnumerable<string> listed, IEnumerable<string> incoming)
    {
        var seen = new HashSet<string>(listed, StringComparer.OrdinalIgnoreCase);
        var added = new List<string>();
        int alreadyListed = 0;
        // T-F556: every source of the list is a listing (a drop, a picker, Explorer's selection), which
        // names "b.txt " as spelled; by its plain path it is the twin.
        foreach (string path in incoming.Select(InputPaths.Listed))
        {
            if (seen.Add(path))
                added.Add(path);
            else
                alreadyListed++;
        }
        return (added, alreadyListed);
    }
}
