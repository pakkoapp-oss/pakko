using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// T-F414: the name each source of an archive operation takes in the archive, from
/// <see cref="ArchiveOptions.SourceNameParts"/>. Always a tail of the source's own path, so a name
/// can never climb out of it, and tar.exe can be given the same name under the folder above it.
/// </summary>
internal sealed class SourceEntryNames
{
    private static readonly char[] Separators = ['\\', '/'];

    private readonly Dictionary<string, string>? _names;

    private SourceEntryNames(Dictionary<string, string>? names) => _names = names;

    internal static SourceEntryNames OwnNames { get; } = new(null);

    /// <summary>
    /// Builds the names for <paramref name="options"/>, whose source paths are already normalized
    /// (<see cref="SourcePathNormalizer"/>). An error when a count is out of range or a name lands
    /// inside another source's name.
    /// </summary>
    internal static (SourceEntryNames Names, ArchiveError? Error) Create(ArchiveOptions options)
    {
        if (options.SourceNameParts is not { } parts)
            return (OwnNames, null);

        IReadOnlyList<string> sources = options.SourcePaths;
        if (parts.Count != sources.Count)
            return (OwnNames, Invalid(ArchiveNaming.RefusedArchivePath(options)));

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < sources.Count; i++)
        {
            if (TailName(sources[i], parts[i]) is not { } name
                || (names.TryGetValue(sources[i], out string? earlier) && earlier != name))
                return (OwnNames, Invalid(sources[i]));
            names[sources[i]] = name;
        }

        return OverlapError(names) is { } overlap ? (OwnNames, overlap) : (new SourceEntryNames(names), null);
    }

    /// <summary>The name of <paramref name="sourcePath"/> (one of the options' sources) in the archive, '/' between parts.</summary>
    internal string For(string sourcePath) =>
        _names is not null && _names.TryGetValue(sourcePath, out string? name) ? name : Path.GetFileName(sourcePath);

    /// <summary>
    /// The folder <paramref name="fullSource"/>'s name hangs under: the path with the name's parts
    /// taken off. <paramref name="fullSource"/> may differ from the option's path in its drive
    /// (a resolved subst drive), never in its tail.
    /// </summary>
    internal static string ParentOf(string fullSource, string name)
    {
        string folder = fullSource;
        for (int i = name.Split('/').Length; i > 0; i--)
            folder = Path.GetDirectoryName(folder)!;
        return folder;
    }

    private static string? TailName(string sourcePath, int count)
    {
        if (count < 1)
            return null;
        string root = Path.GetPathRoot(sourcePath) ?? "";
        string[] all = sourcePath[root.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        // A drive root has no name of its own; both engines name its entries as before (T-F99, T-F285).
        if (all.Length == 0)
            return count == 1 ? Path.GetFileName(sourcePath) : null;
        if (count > all.Length)
            return null;
        string[] tail = all[^count..];
        return Array.Exists(tail, part => part is "." or "..") ? null : string.Join('/', tail);
    }

    // Every folder a name passes through is looked up among the names; an equal name is not an
    // overlap (it is renamed, as two sources with one name always were).
    private static ArchiveError? OverlapError(Dictionary<string, string> names)
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string source, string name) in names.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            owners.TryAdd(name, source);

        foreach ((string innerSource, string innerName) in names.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            for (int cut = innerName.LastIndexOf('/'); cut > 0; cut = innerName.LastIndexOf('/', cut - 1))
            {
                if (owners.TryGetValue(innerName[..cut], out string? outerSource))
                    return CoreMessages.Error(innerSource, MessageCode.SourceNamesOverlap, innerSource, outerSource, innerName);
            }
        }
        return null;
    }

    private static ArchiveError Invalid(string sourcePath) =>
        CoreMessages.Error(sourcePath, MessageCode.SourceNamePartsInvalid, sourcePath);
}
