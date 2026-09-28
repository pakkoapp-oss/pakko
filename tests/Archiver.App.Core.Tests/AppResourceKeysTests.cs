using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F198: the words that replaced the App's hard-coded English ("Folder", "N bytes", "Yes"/"No",
// "OK", "Error", the busy status lines). A key missing from one locale silently shows English
// there, so every locale must have every one of them, with English's placeholders.
public sealed partial class AppResourceKeysTests
{
    private static readonly string[] Keys =
    [
        "DialogYesButton", "DialogNoButton", "DialogOkButton", "DialogErrorTitle", "TypeFolder", "TypeFile",
        "SizeBytes", "SizeKB", "SizeMB", "SizeGB", "StatusArchivingCount", "StatusExtractingCount",
    ];

    private static readonly string StringsRoot = Path.Combine(FindRepoRoot(), "src", "Archiver.App", "Strings");

    public static TheoryData<string> Locales()
    {
        var data = new TheoryData<string>();
        foreach (string dir in Directory.EnumerateDirectories(StringsRoot))
            data.Add(Path.GetFileName(dir));
        return data;
    }

    [Fact]
    public void ThereAreThirtySevenLocales() => Directory.EnumerateDirectories(StringsRoot).Should().HaveCount(37);

    [Theory]
    [MemberData(nameof(Locales))]
    public void EveryLocale_HasEveryKey_WithEnglishPlaceholders(string locale)
    {
        Dictionary<string, string> english = Read("en-US");
        Dictionary<string, string> local = Read(locale);

        foreach (string key in Keys)
        {
            local.Should().ContainKey(key, locale);
            local[key].Should().NotBeNullOrWhiteSpace($"{locale} {key}");
            Placeholders(local[key]).Should().Equal(Placeholders(english[key]), $"{locale} {key}: {local[key]}");
        }
    }

    private static Dictionary<string, string> Read(string locale) =>
        XDocument.Load(Path.Combine(StringsRoot, locale, "Resources.resw")).Root!
            .Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty);

    private static string[] Placeholders(string value) =>
        PlaceholderPattern().Matches(value).Select(m => m.Value).Distinct().Order().ToArray();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
