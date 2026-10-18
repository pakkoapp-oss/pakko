using System.IO.Enumeration;
using System.Text;

namespace Archiver.CLI;

/// <summary>
/// T-F410: turns the parsed name switches into what x, l and t run on — the archives on disk
/// (7-Zip's arcCensor: the archive name, -ai, -ax, wildcards) and the filter for the entries
/// inside them (7-Zip's Censor: -i, -x, the names after the archive, list files). The parser
/// reads no file; this does, so its errors (a missing list file, no archive found) are exit 7
/// here, as 7-Zip's CArcCmdLineException.
/// </summary>
public static class CliNameSelection
{
    // 7-Zip's ReadNamesFromListFile2 refuses a list of 2 GiB or more; Pakko keeps it in memory.
    private const long MaxListFileBytes = 64L * 1024 * 1024;
    private static readonly char[] PathSeparators = ['\\', '/'];
    private static readonly char[] LineBreaks = ['\n', '\r'];

    /// <summary>The entry filter for x and l, or null when nothing filters (every entry).</summary>
    public static (CliNameFilter? Filter, string? Error) BuildEntryFilter(ParsedCliCommand command)
    {
        CliNameSwitches switches = command.NameSwitches;
        if (!switches.FiltersNames && command.FilterNames.Count == 0)
            return (null, null);

        var masks = new List<CliNameMask>();
        foreach (CliWildcardSwitch item in switches.Names)
        {
            CliRecursion recursion = item.Recursion ?? switches.Recursion;
            bool wildcardMatching = item.WildcardMatching ?? switches.WildcardMatching;
            if (NamesOf(item.Text, item.IsListFile, switches.ListFileCharset) is not { } names)
                return (null, ListFileError(item.Text));
            masks.AddRange(names.Select(n => new CliNameMask(n, item.Include, recursion, wildcardMatching)));
        }
        foreach (CliFilterName name in command.FilterNames)
        {
            if (name.Text.Length == 0 || (name.IsListFile && name.Text.Length == 1))
                return (null, "Empty file path");
            string text = name.IsListFile ? name.Text[1..] : name.Text;
            if (NamesOf(text, name.IsListFile, switches.ListFileCharset) is not { } names)
                return (null, ListFileError(text));
            masks.AddRange(names.Select(n => new CliNameMask(n, Include: true, switches.Recursion, switches.WildcardMatching)));
        }

        bool commandLineIncludes = command.FilterNames.Count > 0 || switches.Names.Any(n => n.Include);
        return (CliNameFilter.Create(masks, switches.CaseSensitive, switches.ExcludeFolders, switches.ExcludeFiles,
            startFromAll: !commandLineIncludes), null);
    }

    /// <summary>
    /// T-F410: what 'a' archives: the names after the archive, their list files and -i's names as
    /// masks to look for (<see cref="CliArchiveSources"/>), and -x's names as the filter over names
    /// in the archive. 7-Zip's ExtendExclude carries only an exclude without a prefix to every
    /// source, so an absolute -x name, or one with a '.' or '..' part, leaves nothing out (measured).
    /// </summary>
    public static (IReadOnlyList<CliNameMask> Includes, CliNameFilter? Excludes, string? Error) BuildArchiveMasks(ParsedCliCommand command)
    {
        CliNameSwitches switches = command.NameSwitches;
        var includes = new List<CliNameMask>();
        if (AddSourceNames(command, includes) is { } sourceError)
            return ([], null, sourceError);

        var excludes = new List<CliNameMask>();
        foreach (CliWildcardSwitch item in switches.Names)
        {
            if (NamesOf(item.Text, item.IsListFile, switches.ListFileCharset) is not { } names)
                return ([], null, ListFileError(item.Text));
            (item.Include ? includes : excludes).AddRange(names.Select(n => new CliNameMask(
                n, item.Include, item.Recursion ?? switches.Recursion, item.WildcardMatching ?? switches.WildcardMatching)));
        }

        excludes.RemoveAll(m => Path.IsPathRooted(m.Path) || Array.Exists(m.Path.Split(PathSeparators), part => part is "." or ".."));
        CliNameFilter? filter = excludes.Count == 0 ? null : CliNameFilter.Create(excludes, switches.CaseSensitive, startFromAll: true);
        return (includes, filter, null);
    }

    // The names after the archive, a list file's names in its place; the error when one cannot be read.
    private static string? AddSourceNames(ParsedCliCommand command, List<CliNameMask> includes)
    {
        CliNameSwitches switches = command.NameSwitches;
        foreach (CliFilterName name in command.FilterNames)
        {
            if (name.Text.Length == 0 || (name.IsListFile && name.Text.Length == 1))
                return "Empty file path";
            string text = name.IsListFile ? name.Text[1..] : name.Text;
            if (NamesOf(text, name.IsListFile, switches.ListFileCharset) is not { } names)
                return ListFileError(text);
            includes.AddRange(names.Select(n => new CliNameMask(n, Include: true, switches.Recursion, switches.WildcardMatching)));
        }
        return null;
    }

    /// <summary>
    /// The archives to open: the archive name and -ai's names as typed when none has a wildcard or
    /// recurses (a missing one is the command's own error, exit 2, as in 7-Zip); otherwise every
    /// file they match under <paramref name="currentDirectory"/>, without -ax's, sorted. A
    /// wildcard that matches no file is "Cannot find archive".
    /// </summary>
    public static (IReadOnlyList<string> Paths, string? Error) ResolveArchives(ParsedCliCommand command, string currentDirectory)
    {
        CliNameSwitches switches = command.NameSwitches;
        if (command.ReadFromStdin)
            return (command.ArchivePaths, null);

        var includes = new List<CliNameMask>();
        var excludes = new List<CliNameMask>();
        foreach (string path in command.ArchivePaths)
            includes.Add(new CliNameMask(path, Include: true, CliRecursion.None, switches.WildcardMatching));
        foreach (CliWildcardSwitch item in switches.Archives)
        {
            if (NamesOf(item.Text, item.IsListFile, switches.ListFileCharset) is not { } names)
                return ([], ListFileError(item.Text));
            // 7-Zip's nopArc: an archive name never recurses unless its own switch says r.
            (item.Include ? includes : excludes).AddRange(names.Select(n => new CliNameMask(
                n, item.Include, item.Recursion ?? CliRecursion.None, item.WildcardMatching ?? switches.WildcardMatching)));
        }

        if (includes.Count == 0)
            return ([], "Cannot find archive");
        if (includes.TrueForAll(m => !Searches(m)) && excludes.Count == 0)
            return ([.. includes.Select(m => m.Path)], null);

        List<CliNameMask> full = FullPathMasks(includes, excludes, currentDirectory);
        var filter = CliNameFilter.Create(full, switches.CaseSensitive, startFromAll: false);
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CliNameMask mask in full.Where(m => m.Include))
            found.UnionWith(Searches(mask) ? ((IEnumerable<string>?)Candidates(mask) ?? []) : [mask.Path]);
        found.RemoveWhere(file => !filter.Includes(file, isFolder: false));

        return found.Count == 0 ? ([], "Cannot find archive") : ([.. found], null);
    }

    // Every mask as a full path, so a mask's literal folders become the filter's sub-nodes. A
    // relative exclude also holds under every folder an include searches (7-Zip's
    // arcCensor.ExtendExclude): -ax!b.zip leaves out C:\d\b.zip found by -ai!C:\d\*.zip.
    private static List<CliNameMask> FullPathMasks(List<CliNameMask> includes, List<CliNameMask> excludes, string currentDirectory)
    {
        var full = new List<CliNameMask>(includes.Count + excludes.Count);
        full.AddRange(includes.Concat(excludes).Select(m => m with { Path = Path.GetFullPath(m.Path, currentDirectory) }));
        string[] searched = [.. full.Where(m => m.Include && Searches(m)).Select(LiteralRoot).Distinct(StringComparer.OrdinalIgnoreCase)];
        foreach (CliNameMask exclude in excludes.Where(m => !Path.IsPathRooted(m.Path)))
            full.AddRange(searched.Select(folder => exclude with { Path = Path.Combine(folder, exclude.Path) }));
        return full;
    }

    // A mask that has to be looked for on disk rather than opened as typed.
    private static bool Searches(CliNameMask mask) =>
        mask.Recursion != CliRecursion.None || (mask.WildcardMatching && CliNameFilter.ContainsWildcard(WithoutLongPathPrefix(mask.Path)));

    // T-F419: the '?' of "\\?\C:\..." is the long-path prefix, not a wildcard (7-Zip's IsSuperPath).
    private static string WithoutLongPathPrefix(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;

    // The files under the mask's literal leading folders; deeper only when it recurses or a
    // folder part holds a wildcard. Linked folders are not followed, so a junction loop ends the walk;
    // null when the folder does not exist.
    private static FileSystemEnumerable<string>? Candidates(CliNameMask mask)
    {
        string root = LiteralRoot(mask);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = mask.Recursion != CliRecursion.None || LiteralFolderCount(mask) < mask.Path.Split(PathSeparators).Length - 1,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
        };
        if (!Directory.Exists(root))
            return null;
        // A linked file stays (a OneDrive placeholder carries the reparse attribute too); only a
        // linked folder is not walked into.
        return new FileSystemEnumerable<string>(root, static (ref FileSystemEntry entry) => entry.ToFullPath(), options)
        {
            ShouldIncludePredicate = static (ref FileSystemEntry entry) => !entry.IsDirectory,
            ShouldRecursePredicate = static (ref FileSystemEntry entry) => (entry.Attributes & FileAttributes.ReparsePoint) == 0,
        };
    }

    // The mask's leading folders up to the first one with a wildcard (never its last part).
    private static int LiteralFolderCount(CliNameMask mask)
    {
        string[] parts = mask.Path.Split(PathSeparators);
        int literal = 0;
        while (literal < parts.Length - 1 && !(mask.WildcardMatching && CliNameFilter.ContainsWildcard(parts[literal])))
            literal++;
        return literal;
    }

    private static string LiteralRoot(CliNameMask mask) =>
        string.Join(Path.DirectorySeparatorChar, mask.Path.Split(PathSeparators)[..LiteralFolderCount(mask)]) + Path.DirectorySeparatorChar;

    private static string ListFileError(string path) =>
        File.Exists(path) ? $"Incorrect item in listfile: {path}" : $"The file operation error for listfile: cannot open {path}";

    // One name, or a list file's names; null when the list file cannot be read or decoded.
    private static IReadOnlyList<string>? NamesOf(string text, bool isListFile, string? charset) =>
        isListFile ? ReadListFile(text, charset) : [text];

    /// <summary>
    /// 7-Zip's ReadNamesFromListFile2 (ListFileUtils.cpp): UTF-8 unless <paramref name="charset"/>
    /// names another (-scs), byte order marks skipped, one name per line, each trimmed and
    /// stripped of one pair of surrounding quotes, empty lines dropped. Null when the file cannot
    /// be read, or is not valid in that charset.
    /// </summary>
    public static IReadOnlyList<string>? ReadListFile(string path, string? charset)
    {
        string text;
        try
        {
            if (new FileInfo(path).Length >= MaxListFileBytes)
                return null;
            text = Decode(File.ReadAllBytes(path), charset ?? "UTF-8");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException or ArgumentException)
        {
            return null;
        }
        if (text.Contains('\0'))
            return null;

        var names = new List<string>();
        foreach (string line in text.TrimStart('\uFEFF').Split(LineBreaks))
        {
            string name = line.Trim();
            if (name.Length >= 2 && name[0] == '"' && name[^1] == '"')
                name = name[1..^1];
            if (name.Length > 0)
                names.Add(name);
        }
        return names;
    }

    private static string Decode(byte[] bytes, string charset) => charset.ToUpperInvariant() switch
    {
        "UTF-16LE" when bytes.Length % 2 == 0 => new UnicodeEncoding(false, false, throwOnInvalidBytes: true).GetString(bytes),
        "UTF-16BE" when bytes.Length % 2 == 0 => new UnicodeEncoding(true, false, throwOnInvalidBytes: true).GetString(bytes),
        "UTF-16LE" or "UTF-16BE" => throw new ArgumentException("odd length", nameof(bytes)),
        "WIN" => CliConsoleCharset.CreateEncoding(CliConsoleCharset.Ansi).GetString(bytes),
        "DOS" => CliConsoleCharset.CreateEncoding(CliConsoleCharset.Oem).GetString(bytes),
        _ => new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes),
    };
}
