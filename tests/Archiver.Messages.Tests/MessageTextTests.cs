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
        // T-F297: the OS-error details are translated into Ukrainian only.
        MessageCode[] expected = [.. MessageTemplates.Codes.Where(c => culture == "uk-UA" || !MessageText.UkrainianOnlyCodes.Contains(c))];

        local.Keys.Should().BeEquivalentTo(expected.Select(c => c.ToString()));
        foreach (MessageCode code in expected)
        {
            string translated = local[code.ToString()];
            translated.Should().NotBeNullOrWhiteSpace(code.ToString());
            Placeholders(translated).Should().BeEquivalentTo(Placeholders(MessageTemplates.English(code)), $"{culture} {code}: {translated}");
        }
    }

    // A stray brace in one of the hand-written strings would throw FormatException only when that
    // message is shown; format every one of them here instead.
    [Theory]
    [MemberData(nameof(Cultures))]
    public void EveryTranslation_Formats(string culture)
    {
        foreach ((string code, string template) in Read(CultureInfo.GetCultureInfo(culture)))
        {
            int count = Placeholders(template).Select(p => int.Parse(p[1..^1], CultureInfo.InvariantCulture)).DefaultIfEmpty(-1).Max() + 1;
            object[] arguments = [.. Enumerable.Range(0, count).Select(i => (object)("arg" + i))];
            Func<string> format = () => string.Format(CultureInfo.InvariantCulture, template, arguments);
            format.Should().NotThrow($"{culture} {code}: {template}").Which.Should().NotBeEmpty();
        }
    }

    // T-F276: the App and Explorer's menu call creating an archive "compress".
    [Theory]
    [InlineData("", "archiving")]
    [InlineData("uk-UA", "архівуван")]
    public void Messages_CallCreatingAnArchiveCompress(string culture, string oldWord)
    {
        foreach ((string code, string template) in Read(CultureInfo.GetCultureInfo(culture)))
            template.Should().NotContainEquivalentOf(oldWord, code);
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

    // T-F297 (user, 2026-10-03): the English text and code everywhere, Ukrainian added in front
    // for Ukrainian only.
    [Fact]
    public void Render_Ukrainian_AddsTheTranslationToTheEnglishOsText()
    {
        CoreText detail = CoreMessages.Detail(new IOException("The filename is incorrect.", unchecked((int)0x8007007B)));

        string text = MessageText.Render(detail, "fallback", CultureInfo.GetCultureInfo("uk-UA"));

        text.Should().Be(string.Format(CultureInfo.InvariantCulture, Uk(MessageCode.SystemInvalidName), "The filename is incorrect.", "0x8007007B"));
        text.Should().MatchRegex(@"\p{IsCyrillic}").And.EndWith("The filename is incorrect. (0x8007007B)");
    }

    [Fact]
    public void Render_OtherLanguage_ShowsTheEnglishOsTextAndCode()
    {
        CoreText detail = CoreMessages.Detail(new IOException("The filename is incorrect.", unchecked((int)0x8007007B)));

        MessageText.Render(detail, "fallback", CultureInfo.GetCultureInfo("de-DE"))
            .Should().Be("The filename is incorrect. (0x8007007B)");
    }

    [Fact]
    public void UkrainianOnlyCodes_AreAbsentFromEveryOtherTable()
    {
        foreach (string culture in UiCulture.Supported.Where(c => c != "uk-UA"))
            Read(CultureInfo.GetCultureInfo(culture)).Keys.Should().NotContain(MessageText.UkrainianOnlyCodes.Select(c => c.ToString()), culture);
    }

    [Fact]
    public void UkrainianOnlyCodes_KeepTheEnglishText()
    {
        foreach (MessageCode code in MessageText.UkrainianOnlyCodes)
        {
            string english = MessageTemplates.English(code);
            Uk(code).Should().EndWith(english.EndsWith("({1})", StringComparison.Ordinal) ? "{0} ({1})" : english, code.ToString());
        }
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
