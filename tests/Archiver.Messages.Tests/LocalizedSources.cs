using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Archiver.Messages.Tests;

// T-F329: every place a user-visible string is kept, read from the source files so one test can
// compare them: the App's .resw, CoreMessages, the six Shell families and Localization.cpp's table.
internal static partial class LocalizedSources
{
    public const string English = "en-US";

    public static readonly string[] ShellFamilies =
        ["ConflictMessages", "HashMessages", "OperationText", "PasswordMessages", "ResultMessages", "ScanMessages"];

    // The order of LocalizedStrings' fields in Localization.h.
    public static readonly string[] ShellExtensionFields =
    [
        "extractDialog", "extractHereFlat", "extractHereIntelligent", "extractFolderFallback",
        "extractFolderMultiFallback", "extractFolderNamedTemplate", "compressDialog", "archiveFallback",
        "archiveNamedTemplate", "testArchive", "scanArchive", "browseArchive", "hashSubmenu",
        "selectionNotOnDisk", "launchFailedTemplate", "recoveryVerify", "recoveryRepair",
    ];

    public static readonly string RepoRoot = FindRepoRoot();

    public static string[] Locales() =>
        [.. Directory.EnumerateDirectories(Path.Combine(RepoRoot, "src", "Archiver.App", "Strings")).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal)];

    /// <summary>Every string of one locale, keyed "Source/Key".</summary>
    public static Dictionary<string, string> Read(string locale)
    {
        var all = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(all, "App", ReadXml(Path.Combine(RepoRoot, "src", "Archiver.App", "Strings", locale, "Resources.resw")));
        Add(all, "CoreMessages", ReadXml(ResxPath(Path.Combine("src", "Archiver.Messages", "Resources"), "CoreMessages", locale)));
        foreach (string family in ShellFamilies)
            Add(all, family, ReadXml(ResxPath(Path.Combine("src", "Archiver.Shell", "Resources"), family, locale)));
        Add(all, "ShellExt", ReadShellExtension(locale));
        return all;
    }

    private static void Add(Dictionary<string, string> all, string source, Dictionary<string, string> values)
    {
        if (values.Count == 0)
            throw new InvalidOperationException($"{source} has no strings");
        foreach ((string key, string value) in values)
            all.Add($"{source}/{key}", value);
    }

    private static string ResxPath(string folder, string family, string locale) =>
        Path.Combine(RepoRoot, folder, locale == English ? $"{family}.resx" : $"{family}.{locale}.resx");

    private static Dictionary<string, string> ReadXml(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty);

    private static Dictionary<string, string> ReadShellExtension(string locale)
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot, "src", "Archiver.ShellExtension", "Localization.cpp"));
        string row = source.Split('\n').SingleOrDefault(l => l.TrimStart().StartsWith($"{{ L\"{locale}\", {{", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Localization.cpp has no row for {locale}");
        string[] fields = [.. WideLiteral().Matches(row).Skip(1).Select(m => Unescape(m.Groups[1].Value))];
        if (fields.Length != ShellExtensionFields.Length)
            throw new InvalidOperationException($"Localization.cpp's {locale} row has {fields.Length} fields");
        return ShellExtensionFields.Zip(fields).ToDictionary(p => p.First, p => p.Second);
    }

    private static string Unescape(string literal) =>
        UnicodeEscape().Replace(literal, m => ((char)int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString())
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);

    [GeneratedRegex(@"L""((?:[^""\\]|\\.)*)""")]
    private static partial Regex WideLiteral();

    [GeneratedRegex(@"\\u([0-9A-Fa-f]{4})")]
    private static partial Regex UnicodeEscape();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
