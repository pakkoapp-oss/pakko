using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F201: a second Pakko window opened exactly over the first, hiding it.
public sealed class WindowCascadeTests
{
    private static readonly (int, int, int, int) WorkArea = (0, 0, 1920, 1040);

    [Fact]
    public void NoOtherWindow_KeepsProposedSpot()
    {
        WindowCascade.Place(100, 80, 1100, 780, [], WorkArea).Should().Be((100, 80));
    }

    [Fact]
    public void OtherWindowAtSameSpot_ShiftsOneStep()
    {
        WindowCascade.Place(100, 80, 1100, 780, [(100, 80)], WorkArea)
            .Should().Be((100 + WindowCascade.Step, 80 + WindowCascade.Step));
    }

    [Fact]
    public void OtherWindowAFewPixelsOff_CountsAsSameSpot()
    {
        WindowCascade.Place(100, 80, 1100, 780, [(103, 78)], WorkArea)
            .Should().Be((100 + WindowCascade.Step, 80 + WindowCascade.Step));
    }

    [Fact]
    public void TwoWindowsStacked_SkipsBothSteps()
    {
        WindowCascade.Place(100, 80, 1100, 780, [(100, 80), (132, 112)], WorkArea)
            .Should().Be((164, 144));
    }

    [Fact]
    public void OtherWindowFarAway_KeepsProposedSpot()
    {
        WindowCascade.Place(100, 80, 1100, 780, [(700, 200)], WorkArea).Should().Be((100, 80));
    }

    [Fact]
    public void StepPastWorkArea_WrapsToWorkAreaCornerAndStaysInside()
    {
        (int left, int top) = WindowCascade.Place(800, 250, 1100, 780, [(800, 250)], WorkArea);

        left.Should().BeGreaterThanOrEqualTo(0);
        top.Should().Be(0);
        (left + 1100).Should().BeLessThanOrEqualTo(1920);
    }

    [Fact]
    public void WorkAreaOnSecondMonitor_WrapsToThatMonitorsCorner()
    {
        (int left, int top) = WindowCascade.Place(2800, 250, 1100, 780, [(2800, 250)], (1920, 0, 3840, 1040));

        left.Should().BeGreaterThanOrEqualTo(1920);
        top.Should().Be(0);
    }

    [Fact]
    public void EverySpotTaken_FallsBackToProposed()
    {
        var everywhere = new List<(int, int)>();
        for (int x = 0; x < 1920; x += 8)
            for (int y = 0; y < 1040; y += 8)
                everywhere.Add((x, y));

        WindowCascade.Place(100, 80, 1100, 780, everywhere, WorkArea).Should().Be((100, 80));
    }
}
