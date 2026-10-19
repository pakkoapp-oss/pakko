namespace Archiver.Core.Services;

/// <summary>
/// T-F420: how an archive entry's name lands on disk - the one place both engines, and the App's
/// browser, ask. Each part on its own, as 7-Zip's <c>Correct_FsPath</c> (ExtractingFilePath.cpp)
/// and libarchive's <c>cleanup_pathname</c> both do it: a character Windows refuses in a name
/// (<c>* ? " &lt; &gt; |</c>, from <see cref="Path.GetInvalidFileNameChars"/>) becomes '_'; empty and
/// "." parts are dropped. Then the trailing dots and spaces: Windows drops them from every part
/// Pakko writes; tar.exe creates a folder through <c>GetFullPathNameW</c>, which drops them, but
/// opens a file through the <c>\\?\</c> prefix, which keeps them (measured on build 26300). A part
/// left empty becomes '_' (7-Zip's <c>k_EmptyReplaceName</c>). ':' and control characters never
/// get here: the entry is refused first (T-F38/T-F39). Names use '/' between parts.
/// </summary>
public static class EntryDiskName
{
    private static readonly char[] Separators = ['/', '\\'];

    // What Path.GetInvalidFileNameChars() holds but a name check refuses (':', control
    // characters) or a path splits on ('/', '\').
    private static readonly HashSet<char> Replaced =
        [.. Path.GetInvalidFileNameChars().Where(c => c >= 0x20 && c != ':' && c != '/' && c != '\\')];

    /// <summary>The name as Pakko writes it.</summary>
    public static string AsPakkoWrites(string name) => Written(name, trimFileName: true);

    /// <summary>The name as tar.exe writes it inside the quarantine: the file name keeps its
    /// trailing dots and spaces.</summary>
    public static string AsTarExeWrites(string name) => Written(name, trimFileName: false);

    /// <summary>Whether Pakko writes <paramref name="name"/> under another name than its own parts
    /// ("a?b.txt" as "a_b.txt", "d./x.txt" as "d\x.txt"); a doubled separator or a "./" step is
    /// the same name.</summary>
    public static bool Renames(string name) => !string.Equals(AsPakkoWrites(name), string.Join('/', Steps(name)), StringComparison.Ordinal);

    /// <summary>Whether Windows drops a trailing dot or space from a part of <paramref name="name"/>:
    /// the file it lands on is then not the one it names (7-Zip writes "a.txt." as "a.txt_"), so it
    /// never replaces a file already there. A refused character turned into '_' is not this: 7-Zip
    /// writes that name too.</summary>
    public static bool Trims(string name) =>
        Steps(name).Any(part => part != ".." && part.TrimEnd(' ', '.').Length != part.Length);

    /// <summary>The first name with a part shaped as a Windows 8.3 alias ("LONGFI~1.TXT",
    /// "LO1A2B~1.TXT") beside another part of the same folder that starts as it does, or null.
    /// tar.exe would write that entry through the other's short name, over it.</summary>
    public static string? ShortNameAliasOfAnother(IReadOnlyList<string> names)
    {
        var children = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            string[] parts = AsTarExeWrites(StripLeadingDotSlash(name)).Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                string folder = string.Join('/', parts[..i]);
                if (!children.TryGetValue(folder, out List<string>? list))
                    children[folder] = list = [];
                list.Add(parts[i]);
            }
        }
        foreach (string name in names)
        {
            string[] parts = AsTarExeWrites(StripLeadingDotSlash(name)).Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                if (!IsAliasShaped(parts[i]))
                    continue;
                string start = parts[i][..Math.Min(2, parts[i].IndexOf('~'))];
                if (children[string.Join('/', parts[..i])].Any(other =>
                        !string.Equals(other, parts[i], StringComparison.OrdinalIgnoreCase)
                        && other.StartsWith(start, StringComparison.OrdinalIgnoreCase)))
                    return name;
            }
        }
        return null;
    }

    // An 8.3 name with "~" and digits ending its base: "LONGFI~1.TXT", "LO1A2B~12".
    private static bool IsAliasShaped(string part)
    {
        int dot = part.LastIndexOf('.');
        string stem = dot < 0 ? part : part[..dot];
        int tilde = stem.IndexOf('~');
        return stem.Length <= 8 && (dot < 0 || part.Length - dot - 1 <= 3)
            && tilde > 0 && tilde < stem.Length - 1 && stem[(tilde + 1)..].All(char.IsAsciiDigit);
    }

    /// <summary>T-F30's "the same name": equal but for case, a "./" step or a doubled separator - a
    /// later copy may replace an earlier one under Overwrite, as in 7-Zip. Two different names that
    /// only land on one path are twins, and are kept apart.</summary>
    public static bool SameName(string first, string second) =>
        string.Equals(string.Join('/', Steps(first)), string.Join('/', Steps(second)), StringComparison.OrdinalIgnoreCase);

    /// <summary>The names, as Pakko writes them, that two different entries share only because
    /// Pakko writes one of them under another name ("a.txt." and "a.txt", "a?b" and "a_b").</summary>
    public static IReadOnlyList<string> TrimmedTwins(IEnumerable<string> names) =>
        [.. names.GroupBy(AsPakkoWrites, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Any(Renames) && g.Select(n => string.Join('/', Steps(n))).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
            .Select(g => g.Key)];

    /// <summary>The name without its leading "./" steps, as tar writes them.</summary>
    public static string StripLeadingDotSlash(string name)
    {
        while (name.StartsWith("./", StringComparison.Ordinal))
            name = name[2..];
        return name;
    }

    private static IEnumerable<string> Steps(string name) =>
        name.Split(Separators).Where(part => part.Length > 0 && part != ".");

    // A folder entry ("d./") ends with a separator: every part of it is a folder.
    private static string Written(string name, bool trimFileName)
    {
        string[] parts = [.. Steps(name)];
        bool folder = name.Length > 0 && Array.IndexOf(Separators, name[^1]) >= 0;
        for (int i = 0; i < parts.Length; i++)
        {
            // ".." is a step, not a name: the engines refuse it before (T-F49, T-F228); here it
            // stays one, so a containment check after this still sees it.
            if (parts[i] == "..")
                continue;
            string part = string.Concat(parts[i].Select(c => Replaced.Contains(c) ? '_' : c));
            if (trimFileName || folder || i < parts.Length - 1)
                part = part.TrimEnd(' ', '.');
            parts[i] = part.Length == 0 ? "_" : part;
        }
        return string.Join('/', parts);
    }
}
