using System.Globalization;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F192: mirrors ConflictDialogLocalizerTests' own approach exactly, including its
// advisor-flagged real check -- the 6 reused PasswordDialog* values were written for the App's
// own DialogService.cs .Replace("{0}", ...) call (tolerant of any brace content), but this resx
// is consumed here via string.Format (which throws FormatException on a stray/unbalanced brace
// anywhere in the string). PasswordDialogShowPasswordCheck is the one key with no App-side
// equivalent at all (WinUI's PasswordBox has its own built-in reveal button).
public sealed class PasswordDialogLocalizerTests
{
    private static readonly string[] NoArgKeys =
    [
        "PasswordDialogTitle",
        "PasswordDialogWrongPasswordHint",
        "PasswordDialogApplyToRemainingCheck",
        "PasswordDialogOkButton",
        "PasswordDialogCancelButton",
        "PasswordDialogShowPasswordCheck",
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
    [InlineData("PasswordDialogTitle")]
    [InlineData("PasswordDialogWrongPasswordHint")]
    [InlineData("PasswordDialogApplyToRemainingCheck")]
    [InlineData("PasswordDialogOkButton")]
    [InlineData("PasswordDialogCancelButton")]
    [InlineData("PasswordDialogShowPasswordCheck")]
    public void Get_NeutralCulture_ReturnsNonEmptyString(string key)
    {
        PasswordDialogLocalizer.Get(key).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Get_NeutralCulture_MessageKeyContainsSubstitutedArchiveName()
    {
        PasswordDialogLocalizer.Get("PasswordDialogMessage", "secret.zip").Should().Contain("secret.zip");
    }

    [Fact]
    public void Get_UkrainianCulture_ReturnsTranslatedText()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("uk-UA");

            PasswordDialogLocalizer.Get("PasswordDialogTitle").Should().Be("Потрібен пароль");
            PasswordDialogLocalizer.Get("PasswordDialogShowPasswordCheck").Should().Be("Показати пароль");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    public static IEnumerable<object[]> AllCultureKeyPairs()
    {
        foreach (var culture in NonNeutralCultures)
            foreach (var key in NoArgKeys)
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

            PasswordDialogLocalizer.Get(key).Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Theory]
    [MemberData(nameof(NonNeutralCulturesMemberData))]
    public void Get_EveryLocale_MessageKeyNeverThrowsFormatExceptionAndContainsArchiveName(string culture)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

            PasswordDialogLocalizer.Get("PasswordDialogMessage", "secret.zip").Should().Contain("secret.zip");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    public static IEnumerable<object[]> NonNeutralCulturesMemberData()
    {
        foreach (var culture in NonNeutralCultures)
            yield return [culture];
    }
}
