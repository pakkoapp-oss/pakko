using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Messages.Tests;

// T-F329: T-F328 found one action under two or three words inside one locale, because the sources
// were translated at different times. Glossary.tsv names the word each locale uses and the words
// it replaced; a replaced word that comes back in any source fails here.
public sealed class GlossaryTests
{
    private static readonly string[] Concepts =
        ["extract", "compress", "archive", "password", "test", "scan", "hash", "skip", "entry", "folder"];

    private static readonly Row[] Rows = ReadGlossary();

    public static TheoryData<string> Locales()
    {
        var data = new TheoryData<string>();
        foreach (string locale in LocalizedSources.Locales())
            data.Add(locale);
        return data;
    }

    [Fact]
    public void Glossary_HasEveryConcept_ForEveryLocale() =>
        Rows.Select(r => (r.Concept, r.Locale)).Should().BeEquivalentTo(
            from concept in Concepts from locale in LocalizedSources.Locales() select (concept, locale));

    // A parser that silently reads nothing would make the two tests below pass.
    [Theory]
    [MemberData(nameof(Locales))]
    public void EverySource_IsRead(string locale)
    {
        Dictionary<string, string> strings = LocalizedSources.Read(locale);

        strings.Keys.Count(k => k.StartsWith("ShellExt/", StringComparison.Ordinal)).Should().Be(15);
        strings.Keys.Select(k => k[..k.IndexOf('/')]).Distinct().Should().HaveCount(9);
        strings.Should().HaveCountGreaterThan(300);
        strings["ShellExt/launchFailedTemplate"].Should().Contain("{0}").And.NotContain("\\u");
    }

    [Theory]
    [MemberData(nameof(Locales))]
    public void Term_IsTheWordItsKeyShows(string locale)
    {
        Dictionary<string, string> strings = LocalizedSources.Read(locale);

        foreach (Row row in Rows.Where(r => r.Locale == locale))
            strings[row.Key].Should().ContainEquivalentOf(row.Term, $"{locale} {row.Concept}");
    }

    [Theory]
    [MemberData(nameof(Locales))]
    public void NoString_UsesAReplacedWord(string locale)
    {
        Dictionary<string, string> strings = LocalizedSources.Read(locale);

        foreach (Row row in Rows.Where(r => r.Locale == locale))
            foreach (string word in row.Replaced)
                strings.Where(s => row.Except is null || !row.Except.IsMatch(s.Key))
                    .Where(s => s.Value.Contains(word, StringComparison.OrdinalIgnoreCase))
                    .Select(s => $"{s.Key}: {s.Value}")
                    .Should().BeEmpty($"{locale} says \"{row.Term}\" for {row.Concept}, not \"{word}\"");
    }

    private static Row[] ReadGlossary() =>
        [.. File.ReadAllLines(Path.Combine(LocalizedSources.RepoRoot, "tests", "Archiver.Messages.Tests", "Glossary.tsv"))
            .Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t')).Select(c => new Row(
                c[0], c[1], c[2], c[3],
                c[4].Split(';', StringSplitOptions.RemoveEmptyEntries),
                c[5].Length == 0 ? null : new Regex($"^(?:{c[5]})$", RegexOptions.CultureInvariant)))];

    private sealed record Row(string Concept, string Key, string Locale, string Term, string[] Replaced, Regex? Except);
}
