using System.Globalization;
using FluentAssertions;

namespace Archiver.Messages.Tests;

// T-F488: a combining mark (a Devanagari or Thai vowel sign, an Arabic or Hebrew point) joins the
// letter before it; after a bracket, a space or a slash it has nothing to join and Windows draws it
// on a dotted circle. hi-IN's "archive(s)" wrote its plural ending that way.
public sealed class CombiningMarkTests
{
    public static TheoryData<string> Locales()
    {
        var data = new TheoryData<string>();
        foreach (string locale in LocalizedSources.Locales())
            data.Add(locale);
        return data;
    }

    [Theory]
    [MemberData(nameof(Locales))]
    public void EveryCombiningMark_FollowsALetterOrAnotherMark(string locale)
    {
        List<string> orphans = [];
        foreach ((string key, string value) in LocalizedSources.Read(locale))
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (IsMark(value[i]) && (i == 0 || !(char.IsLetter(value[i - 1]) || IsMark(value[i - 1]))))
                    orphans.Add($"{key}: {value}");
            }
        }

        orphans.Should().BeEmpty();
    }

    private static bool IsMark(char c) => CharUnicodeInfo.GetUnicodeCategory(c) is
        UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
}
