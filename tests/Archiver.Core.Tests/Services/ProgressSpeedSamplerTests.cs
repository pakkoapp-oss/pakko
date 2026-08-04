using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

public sealed class ProgressSpeedSamplerTests
{
    [Fact]
    public void Sample_ZeroElapsedTime_ProducesNoSpikeAndNoDivisionByZero()
    {
        var now = DateTime.UtcNow;
        var sut = new ProgressSpeedSampler(now);

        double speed = sut.Sample(bytesTransferred: 1_000_000, now); // same instant as construction

        speed.Should().Be(0, "a zero-elapsed-time sample must not be folded in at all");
    }

    [Fact]
    public void Sample_ElapsedTimeBelowMinInterval_IsIgnored()
    {
        var start = DateTime.UtcNow;
        var sut = new ProgressSpeedSampler(start);

        double speed = sut.Sample(1_000_000, start.AddMilliseconds(100)); // < 250ms floor

        speed.Should().Be(0);
    }

    [Fact]
    public void Sample_FirstValidSample_ReturnsRawInstantSpeedNotSmoothed()
    {
        var start = DateTime.UtcNow;
        var sut = new ProgressSpeedSampler(start);

        double speed = sut.Sample(1_000_000, start.AddSeconds(1));

        speed.Should().Be(1_000_000, "the very first sample has nothing to blend with yet");
    }

    [Fact]
    public void Sample_SecondValidSample_BlendsWithPreviousViaEma()
    {
        var start = DateTime.UtcNow;
        var sut = new ProgressSpeedSampler(start);

        sut.Sample(1_000_000, start.AddSeconds(1)); // instant speed 1,000,000 B/s
        double speed = sut.Sample(3_000_000, start.AddSeconds(2)); // instant speed 2,000,000 B/s over this interval

        // EMA: alpha * instant + (1 - alpha) * previous = 0.25 * 2,000,000 + 0.75 * 1,000,000 = 1,250,000
        speed.Should().Be(1_250_000);
    }

    [Fact]
    public void Sample_NonIncreasingBytesTransferred_IsToleratedAndReturnsLastKnownValueUnchanged()
    {
        var start = DateTime.UtcNow;
        var sut = new ProgressSpeedSampler(start);

        double firstSpeed = sut.Sample(1_000_000, start.AddSeconds(1));

        // A repeated or out-of-order report (bytes did not advance) must not throw, go negative,
        // or spike — it's simply ignored, same as an elapsed-time-too-short sample.
        double afterStale = sut.Sample(1_000_000, start.AddSeconds(2));
        double afterRegressed = sut.Sample(500_000, start.AddSeconds(3));

        afterStale.Should().Be(firstSpeed);
        afterRegressed.Should().Be(firstSpeed);
    }

    [Fact]
    public void Sample_RepeatedCallsAcrossManyIntervals_NeverProducesNegativeSpeed()
    {
        var start = DateTime.UtcNow;
        var sut = new ProgressSpeedSampler(start);
        long bytes = 0;

        for (int i = 1; i <= 20; i++)
        {
            bytes += 10_000;
            double speed = sut.Sample(bytes, start.AddSeconds(i));
            speed.Should().BeGreaterThanOrEqualTo(0);
        }
    }
}
