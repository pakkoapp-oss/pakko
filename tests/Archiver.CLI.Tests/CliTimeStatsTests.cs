using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F408: -bt prints 7-Zip's PrintStat lines (Main.cpp: PrintTime, PrintMemUsage).
public sealed class CliTimeStatsTests
{
    [Fact]
    public void Lines_AreSevenZipsLayout()
    {
        IReadOnlyList<string> lines = CliTimeStats.Lines(
            kernel: TimeSpan.FromMilliseconds(15), user: TimeSpan.FromMilliseconds(1234),
            global: TimeSpan.FromSeconds(2.5), peakPagefileBytes: 4L << 20, peakWorkingSetBytes: (10L << 20) + 1);

        lines.Should().Equal(
            "",
            "Kernel  Time =     0.015 =    0%",
            "User    Time =     1.234 =   49%",
            "Process Time =     1.249 =   49%    Virtual  Memory =      4 MB",
            "Global  Time =     2.500 =  100%    Physical Memory =     11 MB");
    }

    [Fact]
    public void Lines_ZeroGlobalTime_IsZeroPercent()
    {
        IReadOnlyList<string> lines = CliTimeStats.Lines(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 0, 0);

        lines[4].Should().Be("Global  Time =     0.000 =    0%    Physical Memory =      0 MB");
    }

    [Fact]
    public void Lines_LongRun_KeepsWideSeconds()
    {
        IReadOnlyList<string> lines = CliTimeStats.Lines(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(1234567.891), 0, 0);

        lines[4].Should().StartWith("Global  Time =1234567.891 =  100%");
    }
}
