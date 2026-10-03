using System.Globalization;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F208: size units follow the display language (КБ, Ko, kt...), numbers the regional format.
public sealed class ProgressTextTests : IDisposable
{
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalUiCulture;
        CultureInfo.CurrentCulture = _originalCulture;
    }

    [Theory]
    [InlineData("en-US", 6, "6 B")]
    [InlineData("en-US", 2048, "2 KB")]
    [InlineData("en-US", 1_572_864, "1.5 MB")]
    [InlineData("uk-UA", 2048, "2 КБ")]
    [InlineData("uk-UA", 1_572_864, "1,5 МБ")]
    [InlineData("fr-FR", 3_221_225_472, "3,0 Go")]
    public void FormatBytes_UsesTheLanguagesUnit(string culture, long bytes, string expected)
    {
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

        ProgressText.FormatBytes(bytes).Should().Be(expected);
    }

    [Fact]
    public void FormatSpeed_UnderUkrainian_UsesTheLocalPerSecond()
    {
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("uk-UA");

        ProgressText.FormatSpeed(2_097_152).Should().Be("2,0 МБ/с");
    }

    // T-F307: the tar listing passes report no bytes; the status says what is going on instead of "0%".
    [Theory]
    [InlineData("en-US", "Checking the archive's contents...")]
    [InlineData("uk-UA", "Перевірка вмісту архіву...")]
    public void FormatStatus_CheckingArchive_SaysSoInTheLanguage(string culture, string expected)
    {
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

        ProgressText.FormatStatus(new Archiver.Core.Models.ProgressReport { Phase = Archiver.Core.Models.ProgressPhase.CheckingArchive }, null)
            .Should().Be(expected);
    }
}
