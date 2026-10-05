using FluentAssertions;

namespace Archiver.Messages.Tests;

// The Store listing (docs/store-listing) names the app's actions and menu items. It is pasted into
// Partner Center by hand, so nothing else catches a listing that says one word while the app says
// another, or a field longer than Partner Center accepts.
public sealed class StoreListingTests
{
    private static readonly string[] Sections =
        ["Description", "Short description", "Product features", "Search terms", "What's new in this version"];

    private static readonly string[] MenuItemsNamed =
        ["browseArchive", "extractHereFlat", "compressDialog", "testArchive", "scanArchive", "hashSubmenu"];

    public static TheoryData<string> Locales()
    {
        var data = new TheoryData<string>();
        foreach (string locale in LocalizedSources.Locales())
            data.Add(locale);
        return data;
    }

    [Theory]
    [MemberData(nameof(Locales))]
    public void Listing_HasEveryField_WithinPartnerCenterLimits(string locale)
    {
        Dictionary<string, string> listing = Read(locale);

        listing.Keys.Should().Equal(Sections);
        listing["Description"].Length.Should().BeLessThanOrEqualTo(10000);
        listing["Description"].Split("\n\n").Should().HaveCount(4);
        listing["Short description"].Length.Should().BeLessThanOrEqualTo(1000);
        listing["Short description"].Should().NotContain("\n");
        listing["What's new in this version"].Length.Should().BeLessThanOrEqualTo(1500);
        Lines(listing, "Product features").Should().HaveCount(12).And.OnlyContain(f => f.Length <= 200);
        Lines(listing, "Search terms").Should().HaveCount(7).And.OnlyHaveUniqueItems().And.OnlyContain(t => t.Length <= 30);
        Lines(listing, "Search terms").Sum(t => t.Split(' ').Length).Should().BeLessThanOrEqualTo(21);
    }

    [Theory]
    [MemberData(nameof(Locales))]
    public void Listing_NamesTheMenuItems_AsTheMenuShowsThem(string locale)
    {
        Dictionary<string, string> strings = LocalizedSources.Read(locale);
        string menuFeature = Lines(Read(locale), "Product features")[4];

        foreach (string field in MenuItemsNamed)
            menuFeature.Should().Contain(strings[$"ShellExt/{field}"].TrimEnd('…'), $"{locale} {field}");
    }

    [Theory]
    [MemberData(nameof(Locales))]
    public void Listing_UsesNoReplacedWord(string locale)
    {
        string text = File.ReadAllText(ListingPath(locale));

        foreach (string word in ReplacedWords(locale))
            text.Contains(word, StringComparison.OrdinalIgnoreCase).Should().BeFalse($"{locale} replaced \"{word}\" in the app");
    }

    private static string[] Lines(Dictionary<string, string> listing, string section) => listing[section].Split('\n');

    private static string ListingPath(string locale) =>
        Path.Combine(LocalizedSources.RepoRoot, "docs", "store-listing", locale + ".txt");

    private static Dictionary<string, string> Read(string locale)
    {
        var sections = new Dictionary<string, string>();
        string? current = null;
        foreach (string line in File.ReadAllLines(ListingPath(locale)))
        {
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = line[1..^1];
                sections.Add(current, string.Empty);
            }
            else if (current is not null)
            {
                sections[current] += line + "\n";
            }
        }

        return sections.ToDictionary(s => s.Key, s => s.Value.Trim('\n'));
    }

    private static IEnumerable<string> ReplacedWords(string locale) =>
        File.ReadAllLines(Path.Combine(LocalizedSources.RepoRoot, "tests", "Archiver.Messages.Tests", "Glossary.tsv"))
            .Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t')).Where(c => c[2] == locale)
            .SelectMany(c => c[4].Split(';', StringSplitOptions.RemoveEmptyEntries));
}
