using System.Globalization;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F330: Explorer's commands follow the user's language list, as the App's own strings do.
public sealed class ShellUiLanguageTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData("en-US", "uk,en-US", "uk-UA")]          // English display language, Ukrainian listed first
    [InlineData("de-DE", "en-US,de-DE", "en-US")]       // English listed first wins over a German display
    [InlineData("en-US", "ga-IE,de-AT,uk", "de-DE")]    // skips a language Pakko has no translation for
    [InlineData("uk-UA", "ga-IE", "en-US")]             // nothing listed is shipped: English, as the App
    [InlineData("en-US", "zh-Hans-CN,en-US", "zh-Hans")]
    public void Pick_FollowsTheLanguageList(string display, string list, string expected) =>
        ShellUiLanguage.Pick(list.Split(','), CultureInfo.GetCultureInfo(display)).Name.Should().Be(expected);

    [Theory]
    [InlineData("de-AT", "de-DE")]
    [InlineData("uk-UA", "uk-UA")]
    [InlineData("en-GB", "en-GB")]
    public void Pick_WithNoList_FollowsTheDisplayLanguage(string display, string expected) =>
        ShellUiLanguage.Pick([], CultureInfo.GetCultureInfo(display)).Name.Should().Be(expected);

    [Fact]
    public void ReadUserLanguages_ReturnsTagsOrNothing() =>
        ShellUiLanguage.ReadUserLanguages().Should().OnlyContain(tag => tag.Length >= 2 && !tag.Contains(' '));

    [Fact]
    public void Pick_EnglishIsARealCulture_NotTheInvariantOne() =>
        ShellUiLanguage.Pick(["en-GB", "uk"], English).Should().Be(English);
}
