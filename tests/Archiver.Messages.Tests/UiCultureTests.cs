using FluentAssertions;

namespace Archiver.Messages.Tests;

// T-F254: Windows' own language tags must land on the translation users expect.
public sealed class UiCultureTests
{
    [Theory]
    [InlineData("uk-UA", "uk-UA")]
    [InlineData("UK-ua", "uk-UA")]
    [InlineData("uk", "uk-UA")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-SG", "zh-Hans")]
    [InlineData("zh-Hans-CN", "zh-Hans")]
    [InlineData("pt-BR", "pt-PT")]
    [InlineData("de-AT", "de-DE")]
    [InlineData("de-CH", "de-DE")]
    [InlineData("fr-CA", "fr-FR")]
    [InlineData("es-MX", "es-ES")]
    [InlineData("sr-Cyrl-RS", "sr-Latn-RS")]
    [InlineData("no", "nb-NO")]
    public void ResolveName_FindsTheShippedTranslation(string tag, string expected) =>
        UiCulture.ResolveName(tag).Should().Be(expected);

    [Theory]
    [InlineData("en-US")]
    [InlineData("en-GB")]
    [InlineData("zh-TW")]
    [InlineData("zh-HK")]
    [InlineData("zh-Hant-TW")]
    [InlineData("ga-IE")]
    [InlineData("")]
    public void ResolveName_WithoutATranslation_IsEnglish(string tag) =>
        UiCulture.ResolveName(tag).Should().BeNull();
}
