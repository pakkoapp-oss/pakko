using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F198/T-F199: a key missing from one locale silently shows English there, so every locale must
// have every key, with English's placeholders.
public sealed partial class AppResourceKeysTests
{
    // T-F199 step 8: every word the App shows, not a hand-kept list — a new key that is not
    // translated shows English in 35 locales. Only the About dialog's links stay English-only.
    private static readonly string[] EnglishOnlyKeys = ["AboutGitHubUrl", "AboutPrivacyUrl", "AboutKofiUrl"];

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

        foreach (string key in english.Keys.Except(EnglishOnlyKeys))
        {
            local.Should().ContainKey(key, locale);
            local[key].Should().NotBeNullOrWhiteSpace($"{locale} {key}");
            Placeholders(local[key]).Should().Equal(Placeholders(english[key]), $"{locale} {key}: {local[key]}");
        }
    }

    // T-F199 step 0 (the T-F104 class): a dotted key only resolves through a matching x:Uid, a plain
    // one only through a GetString call. The redesign removes and renames many keys; a key nothing
    // uses anymore is dead in 37 files.
    [Fact]
    public void EveryEnglishKey_IsUsedByAnXUidOrAStringLiteral()
    {
        string appDir = Path.Combine(FindRepoRoot(), "src", "Archiver.App");
        string xaml = string.Concat(Directory.EnumerateFiles(appDir, "*.xaml", SearchOption.AllDirectories)
            .Where(IsSource).Select(File.ReadAllText));
        string code = string.Concat(new[] { appDir, Path.Combine(FindRepoRoot(), "src", "Archiver.App.Core") }
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(IsSource).Select(File.ReadAllText));

        string[] unused = Read("en-US").Keys.Where(key =>
        {
            int dot = key.IndexOf('.');
            return dot > 0
                ? !xaml.Contains($"x:Uid=\"{key[..dot]}\"", StringComparison.Ordinal)
                : !code.Contains($"\"{key}\"", StringComparison.Ordinal);
        }).ToArray();

        unused.Should().BeEmpty();
    }

    [Fact]
    public void EveryXUid_HasAnEnglishKey()
    {
        string appDir = Path.Combine(FindRepoRoot(), "src", "Archiver.App");
        var uidsWithKeys = Read("en-US").Keys.Where(k => k.Contains('.')).Select(k => k[..k.IndexOf('.')]).ToHashSet();

        string[] orphans = Directory.EnumerateFiles(appDir, "*.xaml", SearchOption.AllDirectories).Where(IsSource)
            .SelectMany(f => XUidPattern().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Where(uid => !uidsWithKeys.Contains(uid)).ToArray();

        orphans.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Locales))]
    public void NoLocale_HasAKeyEnglishLacks(string locale)
    {
        var english = Read("en-US").Keys.ToHashSet();

        Read(locale).Keys.Where(k => !english.Contains(k)).Should().BeEmpty(locale);
    }

    private static bool IsSource(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    [GeneratedRegex("x:Uid=\"([^\"]+)\"")]
    private static partial Regex XUidPattern();

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
