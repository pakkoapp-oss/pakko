namespace Archiver.Core.Services;

/// <summary>
/// T-F420: how an archive entry's name lands on disk - the one place both engines, and the App's
/// browser, ask. Windows drops a part's trailing dots and spaces; tar.exe (libarchive,
/// <c>archive_write_disk_windows.c</c>) creates a folder through <c>GetFullPathNameW</c>, which drops
/// them, but opens a file through the <c>\\?\</c> prefix, which keeps them (measured on build 26300).
/// Names use '/' between parts, as the archive does.
/// </summary>
public static class EntryDiskName
{
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>The name as Pakko writes it: every part without its trailing dots and spaces.</summary>
    public static string AsPakkoWrites(string name) => Trimmed(StripLeadingDotSlash(name), fileNameToo: true);

    /// <summary>The name as tar.exe writes it inside the quarantine: folder parts trimmed, the file
    /// name as it is.</summary>
    public static string AsTarExeWrites(string name) => Trimmed(StripLeadingDotSlash(name), fileNameToo: false);

    /// <summary>Whether Windows writes <paramref name="name"/> under another name: a part ends in a
    /// space or a dot ("d./x.txt" is "d\x.txt"). "." itself is a path step, not a trimmed name.</summary>
    public static bool IsTrimmed(string name) =>
        name.Split(Separators).Any(part => part != "." && (part.EndsWith(' ') || part.EndsWith('.')));

    /// <summary>T-F30's "the same name": equal but for case and a leading "./" - a later copy may
    /// replace an earlier one under Overwrite, as in 7-Zip. Two different names that only land on one
    /// path are twins, and are kept apart.</summary>
    public static bool SameName(string first, string second) =>
        string.Equals(StripLeadingDotSlash(first), StripLeadingDotSlash(second), StringComparison.OrdinalIgnoreCase);

    /// <summary>The names, as Pakko writes them, that two different entries share only through
    /// Windows' trimming ("a.txt." and "a.txt", "d./x.txt" and "d/x.txt").</summary>
    public static IReadOnlyList<string> TrimmedTwins(IEnumerable<string> names) =>
        [.. names.Select(StripLeadingDotSlash)
            .GroupBy(AsPakkoWrites, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Any(IsTrimmed) && g.Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
            .Select(g => g.Key)];

    /// <summary>The name without its leading "./" steps, as tar writes them.</summary>
    public static string StripLeadingDotSlash(string name)
    {
        while (name.StartsWith("./", StringComparison.Ordinal))
            name = name[2..];
        return name;
    }

    private static string Trimmed(string name, bool fileNameToo)
    {
        string[] parts = name.Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            if (fileNameToo || i < parts.Length - 1)
                parts[i] = parts[i].TrimEnd(' ', '.');
        }
        return string.Join('/', parts);
    }
}
