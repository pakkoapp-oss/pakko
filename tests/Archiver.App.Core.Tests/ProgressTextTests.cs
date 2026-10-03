using System.Globalization;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F303: the footer's speed and time left were hardcoded English ("54,5 MB/s", "~40:37 remaining")
// while sizes went through the localized units. The words come from the App's resources; the tests
// see the English defaults.
public sealed class ProgressTextTests
{
    [Theory]
    [InlineData(500, "500 B/s")]
    [InlineData(1_536, "1.5 KB/s")]
    [InlineData(57_147_392, "54.5 MB/s")]
    [InlineData(2_147_483_648, "2.0 GB/s")]
    public void Speed_UsesTheSizeUnitsPerSecond(double bytesPerSecond, string expected)
    {
        using var _ = new CultureScope("en-US");

        ProgressText.Speed(bytesPerSecond).Should().Be(expected);
    }

    [Fact]
    public void Speed_NumberFollowsTheUserFormat()
    {
        using var _ = new CultureScope("uk-UA");

        ProgressText.Speed(57_147_392).Should().Be("54,5 MB/s");
    }

    [Theory]
    [InlineData(0.5, 50, "")]          // under a second elapsed: no estimate yet
    [InlineData(10, 0, "")]            // nothing done: no estimate
    [InlineData(10, 75, "")]           // 3.3 s left: not worth showing
    [InlineData(10, 50, "~10 sec remaining")]
    [InlineData(60, 50, "~1:00 remaining")]
    [InlineData(2437, 50, "~40:37 remaining")]
    public void Remaining_EstimatesFromElapsedAndPercent(double elapsedSeconds, int percent, string expected)
    {
        ProgressText.Remaining(TimeSpan.FromSeconds(elapsedSeconds), percent).Should().Be(expected);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CultureScope(string name) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
