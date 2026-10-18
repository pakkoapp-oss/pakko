namespace Archiver.CLI;

/// <summary>T-F414: one source of 'a', and how many of its last path parts name it in the archive.</summary>
public sealed record CliArchiveSource(string FullPath, int NameParts);

/// <summary>T-F414: a name given to 'a' that adds nothing, and why (it exists nowhere, or its folder cannot be read).</summary>
public sealed record CliSourceProblem(string Name, string Reason);

/// <summary>T-F414: what the names given to 'a' come to.</summary>
public sealed record CliArchiveSourceList(IReadOnlyList<CliArchiveSource> Sources, IReadOnlyList<CliSourceProblem> Problems);

/// <summary>
/// T-F414: 7-Zip's rule for the name a source takes (7-Zip 26.02, Common/Wildcard.cpp,
/// <c>CCensor::AddItem</c> in its default relative-path mode): a relative path is stored as typed
/// (<c>dir\sub</c> gives <c>dir/sub/...</c>); an absolute path, or one with a <c>.</c> or <c>..</c>
/// part, by its last part only; a path ending in <c>.</c> or <c>..</c> is that folder's contents,
/// each at the top of the archive. A name that adds nothing is a warning, as in 7-Zip.
/// </summary>
public static class CliArchiveSources
{
    public const string NotFound = "The system cannot find the file specified.";

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <param name="typed">The names as given, relative to <paramref name="baseFolder"/>.</param>
    /// <param name="archivePath">The archive being written: never one of a folder's contents.</param>
    public static CliArchiveSourceList Resolve(IEnumerable<string> typed, string baseFolder, string archivePath)
    {
        string archiveFull = Path.GetFullPath(archivePath, baseFolder);
        var sources = new List<CliArchiveSource>();
        var problems = new List<CliSourceProblem>();
        foreach (string name in typed)
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(name, baseFolder));
            string[] parts = name.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            bool contents = parts.Length > 0 && parts[^1] is "." or "..";
            if (contents ? !Directory.Exists(full) : !File.Exists(full) && !Directory.Exists(full))
                problems.Add(new CliSourceProblem(name, NotFound));
            else if (contents && Path.GetDirectoryName(full) is not null)
                AddContents(name, full, archiveFull, sources, problems);
            else
                sources.Add(new CliArchiveSource(full, NamePartsOf(name, parts)));
        }
        return new CliArchiveSourceList(sources, problems);
    }

    private static int NamePartsOf(string typed, string[] parts) =>
        Path.IsPathRooted(typed) || parts.Length == 0 || Array.Exists(parts, part => part is "." or "..") ? 1 : parts.Length;

    // Every child, hidden ones too, as 7-Zip takes them; a link is passed on for Core to skip and
    // report. A folder that cannot be listed is a warning, as 7-Zip's scan warning is.
    private static void AddContents(string name, string folder, string archiveFull, List<CliArchiveSource> sources, List<CliSourceProblem> problems)
    {
        try
        {
            sources.AddRange(new DirectoryInfo(folder)
                .EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false })
                .Where(child => !string.Equals(child.FullName, archiveFull, StringComparison.OrdinalIgnoreCase))
                .Select(child => new CliArchiveSource(child.FullName, 1))
                .ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add(new CliSourceProblem(name, ex.Message));
        }
    }
}
