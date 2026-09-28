using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Messages.Tests;

// T-F209: Core's messages rendered in the user's language from one shared table.
public sealed partial class MessageTextTests
{
    private static readonly ResourceManager Resources =
        new("Archiver.Messages.Resources.CoreMessages", typeof(MessageText).Assembly);

    public static TheoryData<string> Cultures()
    {
        var data = new TheoryData<string>();
        foreach (string culture in UiCulture.Supported)
            data.Add(culture);
        return data;
    }

    [Fact]
    public void NeutralTable_IsExactlyCoresEnglish()
    {
        Dictionary<string, string> neutral = Read(CultureInfo.InvariantCulture);

        neutral.Keys.Should().BeEquivalentTo(MessageTemplates.Codes.Select(c => c.ToString()));
        foreach (MessageCode code in MessageTemplates.Codes)
            neutral[code.ToString()].Should().Be(MessageTemplates.English(code), code.ToString());
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void EveryCode_IsTranslated_WithTheSamePlaceholders(string culture)
    {
        Dictionary<string, string> local = Read(CultureInfo.GetCultureInfo(culture));

        local.Keys.Should().BeEquivalentTo(MessageTemplates.Codes.Select(c => c.ToString()));
        foreach (MessageCode code in MessageTemplates.Codes)
        {
            string translated = local[code.ToString()];
            translated.Should().NotBeNullOrWhiteSpace(code.ToString());
            Placeholders(translated).Should().BeEquivalentTo(Placeholders(MessageTemplates.English(code)), $"{culture} {code}: {translated}");
        }
    }

    [Fact]
    public void Render_Ukrainian_TranslatesEveryLevelOfANestedMessage()
    {
        var error = new ArchiveError
        {
            SourcePath = "a.7z",
            Message = "unused",
            Text = CoreTextFor(MessageCode.CannotExtractArchive, CoreTextFor(MessageCode.TarExtractionFailed, "boom")),
        };

        string text = MessageText.Render(error, CultureInfo.GetCultureInfo("uk-UA"));

        text.Should().Be(string.Format(CultureInfo.InvariantCulture, Uk(MessageCode.CannotExtractArchive),
            string.Format(CultureInfo.InvariantCulture, Uk(MessageCode.TarExtractionFailed), "boom")));
        text.Should().NotContain("Cannot extract");
    }

    [Fact]
    public void Render_English_IsCoresText()
    {
        CoreText text = CoreTextFor(MessageCode.SourceNotFound, @"C:\x");

        MessageText.Render(text, "fallback", CultureInfo.GetCultureInfo("en-US")).Should().Be(@"Source path does not exist: C:\x");
    }

    [Fact]
    public void Render_RegionalVariant_UsesTheLanguagesTranslation()
    {
        CoreText text = CoreTextFor(MessageCode.ZipCorrupted);

        MessageText.Render(text, "fallback", CultureInfo.GetCultureInfo("de-AT"))
            .Should().Be(Read(CultureInfo.GetCultureInfo("de-DE"))[nameof(MessageCode.ZipCorrupted)]);
    }

    [Fact]
    public void Render_WithoutACode_ReturnsTheEnglishText()
    {
        var skipped = new SkippedFile { Path = "x", Reason = "raw reason" };

        MessageText.Render(skipped, CultureInfo.GetCultureInfo("uk-UA")).Should().Be("raw reason");
    }

    [Fact]
    public void Render_UncodedArgument_IsKeptAsIs()
    {
        CoreText text = CoreTextFor(MessageCode.CannotReadArchive, CoreTextFor(MessageCode.None, "tar: {weird} output"));

        MessageText.Render(text, "fallback", CultureInfo.GetCultureInfo("uk-UA")).Should().Contain("tar: {weird} output");
    }

    private static string Uk(MessageCode code) => Read(CultureInfo.GetCultureInfo("uk-UA"))[code.ToString()];

    private static CoreText CoreTextFor(MessageCode code, params object[] arguments) => CoreMessages.Text(code, arguments);

    private static Dictionary<string, string> Read(CultureInfo culture)
    {
        ResourceSet set = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!;
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    private static string[] Placeholders(string value) =>
        PlaceholderPattern().Matches(value).Select(m => m.Value).Distinct().Order().ToArray();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();
}
