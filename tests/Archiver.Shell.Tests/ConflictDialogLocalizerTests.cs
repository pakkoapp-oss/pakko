using System.Globalization;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F155: mirrors HashResultLocalizerTests' own approach, plus a real regression check the App's
// own .resw values don't already need: the generator script (Generate-ConflictMessages.py) copies
// values that were written for DialogService.ShowConflictDialogAsync's .Replace("{0}", ...) call
// (tolerant of any brace content) into a resx consumed here via string.Format (which throws
// FormatException on an unescaped/unmatched brace anywhere in the string, not just at {0}). This
// test is the actual check for that semantic change surviving the copy across all 37 locales, not
// just the 2 this file's HashResultLocalizerTests-style smoke tests cover.
public sealed class ConflictDialogLocalizerTests
{
    private static readonly string[] AllKeys =
    [
        "ConflictDialogTitle",
        "ConflictDialogMessage",
        "ConflictDialogOverwriteButton",
        "ConflictDialogRenameButton",
        "ConflictDialogSkipButton",
        "ConflictDialogApplyToAllCheck",
    ];

    // Matches src/Archiver.App/Strings/*'s own 37 locale folders (T-F91/T-F105 etc.) minus en-US,
    // which is covered separately as the neutral-culture case below.
    private static readonly string[] NonNeutralCultures =
    [
        "ar-SA", "bg-BG", "cs-CZ", "da-DK", "de-DE", "el-GR", "es-ES", "et-EE", "fi-FI", "fr-FR",
        "he-IL", "hi-IN", "hr-HR", "hu-HU", "id-ID", "it-IT", "ja-JP", "ko-KR", "lt-LT", "lv-LV",
        "nb-NO", "nl-NL", "pl-PL", "pt-PT", "ro-RO", "sk-SK", "sl-SI", "sr-Latn-RS", "sv-SE",
        "sw-KE", "th-TH", "tr-TR", "uk-UA", "ur-PK", "vi-VN", "zh-Hans",
    ];

    [Theory]
    [InlineData("ConflictDialogTitle")]
    [InlineData("ConflictDialogOverwriteButton")]
    [InlineData("ConflictDialogRenameButton")]
    [InlineData("ConflictDialogSkipButton")]
    [InlineData("ConflictDialogApplyToAllCheck")]
    public void Get_NeutralCulture_ReturnsNonEmptyString(string key)
    {
        ConflictDialogLocalizer.Get(key).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Get_NeutralCulture_MessageKeyContainsSubstitutedFilename()
    {
        ConflictDialogLocalizer.Get("ConflictDialogMessage", "photo.png").Should().Contain("photo.png");
    }

    [Fact]
    public void Get_UkrainianCulture_ReturnsTranslatedText()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("uk-UA");

            ConflictDialogLocalizer.Get("ConflictDialogTitle").Should().Be("Файл вже існує");
            ConflictDialogLocalizer.Get("ConflictDialogOverwriteButton").Should().Be("Перезаписати");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    // Advisor-flagged real gap: loop every locale x every key and prove the copied .resw values
    // survive string.Format unharmed -- a stray/unbalanced brace in any translated string throws
    // FormatException here even though it was harmless under the App's own .Replace("{0}", ...).
    public static IEnumerable<object[]> AllCultureKeyPairs()
    {
        foreach (var culture in NonNeutralCultures)
            foreach (var key in AllKeys)
                yield return [culture, key];
    }

    [Theory]
    [MemberData(nameof(AllCultureKeyPairs))]
    public void Get_EveryLocaleAndKey_NeverThrowsFormatException(string culture, string key)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

            string result = key == "ConflictDialogMessage"
                ? ConflictDialogLocalizer.Get(key, "photo.png")
                : ConflictDialogLocalizer.Get(key);

            result.Should().NotBeNullOrWhiteSpace();
            if (key == "ConflictDialogMessage")
                result.Should().Contain("photo.png");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
