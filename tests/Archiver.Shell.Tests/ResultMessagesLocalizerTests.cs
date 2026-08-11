using System.Globalization;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F163: ShowErrorSummary/ShowSkippedSummary/RunTestAsync's "no errors"/"operation failed" text
// and ShellResultPresenter.BuildSkippedMessage's header were hardcoded English -- found by a real
// user running the T-F155 conflict dialog under uk-UA and getting an English summary afterward.
// Mirrors HashResultLocalizerTests' neutral/uk-UA smoke checks, plus ConflictDialogLocalizerTests'
// all-37-locale FormatException loop (defensive here too, since these translations were authored
// fresh rather than copied from an already-.Replace-tested source).
public sealed class ResultMessagesLocalizerTests
{
    private static readonly string[] AllKeys =
    [
        "ResultSkippedHeader",
        "ResultAndMoreLine",
        "ResultNoErrorsDetected",
        "ResultOperationFailed",
    ];

    private static readonly string[] PlaceholderKeys = ["ResultSkippedHeader", "ResultAndMoreLine"];

    // Matches ConflictDialogLocalizerTests' own NonNeutralCultures list (src/Archiver.App/Strings'
    // 37 locale folders minus en-US).
    private static readonly string[] NonNeutralCultures =
    [
        "ar-SA", "bg-BG", "cs-CZ", "da-DK", "de-DE", "el-GR", "es-ES", "et-EE", "fi-FI", "fr-FR",
        "he-IL", "hi-IN", "hr-HR", "hu-HU", "id-ID", "it-IT", "ja-JP", "ko-KR", "lt-LT", "lv-LV",
        "nb-NO", "nl-NL", "pl-PL", "pt-PT", "ro-RO", "sk-SK", "sl-SI", "sr-Latn-RS", "sv-SE",
        "sw-KE", "th-TH", "tr-TR", "uk-UA", "ur-PK", "vi-VN", "zh-Hans",
    ];

    [Fact]
    public void Get_NeutralCulture_SkippedHeaderContainsCount()
    {
        ResultMessagesLocalizer.Get("ResultSkippedHeader", 2).Should().Contain("2");
    }

    [Fact]
    public void Get_NeutralCulture_AndMoreLineContainsCount()
    {
        ResultMessagesLocalizer.Get("ResultAndMoreLine", 5).Should().Contain("5");
    }

    [Theory]
    [InlineData("ResultNoErrorsDetected")]
    [InlineData("ResultOperationFailed")]
    public void Get_NeutralCulture_PlainKeyReturnsNonEmptyString(string key)
    {
        ResultMessagesLocalizer.Get(key).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Get_UkrainianCulture_ReturnsTranslatedText()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("uk-UA");

            ResultMessagesLocalizer.Get("ResultSkippedHeader", 1).Should().Be("Пропущено (1):");
            ResultMessagesLocalizer.Get("ResultOperationFailed").Should().Be("Операція не вдалася.");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

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

            string result = PlaceholderKeys.Contains(key)
                ? ResultMessagesLocalizer.Get(key, 3)
                : ResultMessagesLocalizer.Get(key);

            result.Should().NotBeNullOrWhiteSpace();
            if (PlaceholderKeys.Contains(key))
                result.Should().Contain("3");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
