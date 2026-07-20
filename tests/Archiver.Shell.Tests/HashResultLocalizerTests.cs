using System.Globalization;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F128 follow-up: HashResultLocalizer wraps a ResourceManager over the .resx satellite
// assemblies under Archiver.Shell/Resources/ — this only smoke-tests that the neutral resource
// resolves for all 5 keys used by ShowHashResults with a working {0} placeholder. Translation
// correctness across the other 36 locales isn't unit-testable (matches this project's existing
// precedent — Archiver.App's own resw translations aren't automated-tested either).
public sealed class HashResultLocalizerTests
{
    [Theory]
    [InlineData("HashResultFilesLine")]
    [InlineData("HashResultSizeLine")]
    [InlineData("HashResultDataSumLine")]
    [InlineData("HashResultNamesSumLine")]
    [InlineData("HashResultAndMoreLine")]
    public void Get_NeutralCulture_ReturnsFormattedStringWithPlaceholder(string key)
    {
        string result = HashResultLocalizer.Get(key, "42");

        result.Should().NotBeNullOrWhiteSpace();
        result.Should().Contain("42");
    }

    [Fact]
    public void Get_UkrainianCulture_ReturnsTranslatedText()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("uk-UA");

            HashResultLocalizer.Get("HashResultFilesLine", 5).Should().Be("Файлів: 5");
            HashResultLocalizer.Get("HashResultSizeLine", "1 MB").Should().Be("Розмір: 1 MB");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
