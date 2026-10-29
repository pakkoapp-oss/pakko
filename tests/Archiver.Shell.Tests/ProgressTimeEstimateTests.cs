using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F431: the operation window's time left, the App footer's arithmetic (T-F303, T-F307).
public sealed class ProgressTimeEstimateTests
{
    private static readonly DateTime Start = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static ProgressTimeEstimate StartedAt(int percent)
    {
        var estimate = new ProgressTimeEstimate();
        estimate.SecondsLeft(percent, Start).Should().BeNull("the first report is the start");
        return estimate;
    }

    [Theory]
    [InlineData(10.0, 50, 10.0)]
    [InlineData(10.0, 20, 40.0)]
    [InlineData(30.0, 25, 90.0)]
    public void SecondsLeft_EstimatesFromTheTimeAndPercentSinceTheStart(double elapsedSeconds, int percent, double expected)
    {
        ProgressTimeEstimate estimate = StartedAt(0);

        estimate.SecondsLeft(percent, Start.AddSeconds(elapsedSeconds)).Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void SecondsLeft_FromALaterStartPercent_UsesOnlyTheProgressSinceThen()
    {
        ProgressTimeEstimate estimate = StartedAt(40);

        estimate.SecondsLeft(60, Start.AddSeconds(10)).Should().BeApproximately(20.0, 1e-9);
    }

    [Theory]
    [InlineData(0.9, 50)]
    [InlineData(10.0, 0)]
    [InlineData(10.0, 75)]
    [InlineData(10.0, 100)]
    public void SecondsLeft_UnderASecondNoProgressOrUnderFourSecondsLeft_IsNull(double elapsedSeconds, int percent)
    {
        ProgressTimeEstimate estimate = StartedAt(0);

        estimate.SecondsLeft(percent, Start.AddSeconds(elapsedSeconds)).Should().BeNull();
    }

    [Fact]
    public void SecondsLeft_PercentBelowTheStart_IsNull()
    {
        ProgressTimeEstimate estimate = StartedAt(30);

        estimate.SecondsLeft(10, Start.AddSeconds(10)).Should().BeNull();
    }

    [Fact]
    public void Restart_TheNextReportIsTheNewStart()
    {
        ProgressTimeEstimate estimate = StartedAt(0);
        estimate.SecondsLeft(10, Start.AddSeconds(60)).Should().NotBeNull();

        estimate.Restart();

        estimate.SecondsLeft(20, Start.AddSeconds(70)).Should().BeNull();
        estimate.SecondsLeft(60, Start.AddSeconds(80)).Should().BeApproximately(10.0, 1e-9);
    }
}
