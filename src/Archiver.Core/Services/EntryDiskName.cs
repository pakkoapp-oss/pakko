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
