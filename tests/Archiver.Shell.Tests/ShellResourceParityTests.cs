using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using Archiver.OperationUi.Protocol;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F268 step 6: a key missing from one locale falls back to English without any error, which is
// how the operation window ended up half English. Every family, every locale, every key, with the
// placeholders English has (a stray {2} throws FormatException only at run time).
public sealed partial class ShellResourceParityTests
{
    private static readonly string[] Families =
        ["ConflictMessages", "HashMessages", "OperationText", "PasswordMessages", "ResultMessages", "ScanMessages"];

    private static readonly string[] Cultures =
    [
        "ar-SA", "bg-BG", "cs-CZ", "da-DK", "de-DE", "el-GR", "es-ES", "et-EE", "fi-FI", "fr-FR",
        "he-IL", "hi-IN", "hr-HR", "hu-HU", "id-ID", "it-IT", "ja-JP", "ko-KR", "lt-LT", "lv-LV",
        "nb-NO", "nl-NL", "pl-PL", "pt-PT", "ro-RO", "sk-SK", "sl-SI", "sr-Latn-RS", "sv-SE",
        "sw-KE", "th-TH", "tr-TR", "uk-UA", "ur-PK", "vi-VN", "zh-Hans",
    ];

    public static TheoryData<string, string> FamilyCulturePairs()
    {
        var data = new TheoryData<string, string>();
        foreach (string family in Families)
            foreach (string culture in Cultures)
                data.Add(family, culture);
        return data;
    }

    [Theory]
    [MemberData(nameof(FamilyCulturePairs))]
    public void EveryKey_IsTranslated_WithTheSamePlaceholders(string family, string culture)
    {
        var manager = new ResourceManager($"Archiver.Shell.Resources.{family}", typeof(ResultMessagesLocalizer).Assembly);
        Dictionary<string, string> english = Read(manager, CultureInfo.InvariantCulture);
        Dictionary<string, string> local = Read(manager, CultureInfo.GetCultureInfo(culture));

        local.Keys.Should().BeEquivalentTo(english.Keys);
        foreach ((string key, string value) in english)
        {
            local[key].Should().NotBeNullOrWhiteSpace(key);
            Placeholders(local[key]).Should().BeEquivalentTo(Placeholders(value), $"{culture} {key}: {local[key]}");
        }
    }

    [Fact]
    public void Hello_UnderUkrainian_TranslatesEveryWindowLabel()
    {
        Dictionary<string, string> english = HelloStrings("en-US");
        Dictionary<string, string> ukrainian = HelloStrings("uk-UA");

        string[] keys = typeof(WindowStrings).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetValue(null)!).ToArray();
        english.Keys.Should().BeEquivalentTo(keys);
        ukrainian.Keys.Should().BeEquivalentTo(keys);
        foreach (string key in keys)
            ukrainian[key].Should().NotBe(english[key], key);
    }

    private static Dictionary<string, string> HelloStrings(string culture)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            return new Dictionary<string, string>(OperationWindowText.CreateHello().Strings);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    private static Dictionary<string, string> Read(ResourceManager manager, CultureInfo culture)
    {
        ResourceSet set = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException($"No resources for {culture.Name}");
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    private static string[] Placeholders(string value) =>
        PlaceholderPattern().Matches(value).Select(m => m.Value).Distinct().Order().ToArray();

    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex PlaceholderPattern();
}
