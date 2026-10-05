using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Messages.Tests;

// T-F329: a template cannot inflect the noun after a number ("1 archives", and three or more
// forms in most of the 36 locales), so a count goes after a colon: "archives: {0}".
public sealed partial class CountTemplateTests
{
    [Fact]
    public void EnglishText_PutsNoCountBeforeTheThingCounted() =>
        LocalizedSources.Read(LocalizedSources.English)
            .Where(s => CountBeforeNoun().IsMatch(s.Value))
            .Select(s => $"{s.Key}: {s.Value}")
            .Should().BeEmpty();

    [Theory]
    [InlineData("{0} archive(s)", true)]
    [InlineData("Extracting {0} archives", true)]
    [InlineData("{0}: {1} files", true)]
    [InlineData("{0} more copies were skipped", true)]
    [InlineData("Extracting archives: {0}", false)]
    [InlineData("Archive {0} of {1}", false)]
    [InlineData("{0} of {1} selected", false)]
    [InlineData("…and {0} more", false)]
    [InlineData("up to {0} characters", false)]
    [InlineData("Creating a {0} archive is blocked", false)]
    public void TheRule_KnowsACountFromALabel(string template, bool isCount) =>
        CountBeforeNoun().IsMatch(template).Should().Be(isCount);

    [GeneratedRegex(@"\{\d\} (?:more )?(?:archive\(s\)|item\(s\)|archives|items|files|folders|entries|copies|threats|errors)", RegexOptions.IgnoreCase)]
    private static partial Regex CountBeforeNoun();
}
