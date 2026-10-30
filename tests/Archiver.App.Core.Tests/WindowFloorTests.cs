using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F433: the App window's minimum size was 900 x 520 physical pixels, so at 175 % the footer
// could be resized out of the window.
public sealed class WindowFloorTests
{
    private static readonly (int Width, int Height) WorkArea = (1920, 1032);

    [Theory]
    [InlineData(96u, 900, 520)]
    [InlineData(120u, 1125, 650)]
    [InlineData(144u, 1350, 780)]
    [InlineData(168u, 1575, 910)]
    public void Physical_ScalesTheFloorByTheWindowsDpi(uint dpi, int width, int height)
    {
        WindowFloor.Physical(dpi, WorkArea).Should().Be((width, height));
    }

    [Fact]
    public void Physical_RoundsAFractionalPixelUp()
    {
        // 900 * 115 / 96 = 1078.1, 520 * 115 / 96 = 622.9: a floor rounded down would cut a pixel.
        WindowFloor.Physical(115, WorkArea).Should().Be((1079, 623));
    }

    [Fact]
    public void Physical_NeverExceedsTheWorkArea()
    {
        WindowFloor.Physical(192, (1366, 728)).Should().Be((1366, 728));
    }

    [Fact]
    public void Physical_ClampsEachSideOnItsOwn()
    {
        WindowFloor.Physical(168, (2560, 800)).Should().Be((1575, 800));
    }

    [Fact]
    public void Physical_ADpiOfZero_IsTakenAs96()
    {
        // GetDpiForWindow returns 0 for an invalid window.
        WindowFloor.Physical(0, WorkArea).Should().Be((900, 520));
    }

    [Fact]
    public void Physical_AnEmptyWorkArea_DoesNotClampToZero()
    {
        WindowFloor.Physical(168, (0, 0)).Should().Be((1575, 910));
    }

    [Fact]
    public void Grow_ASmallerWindow_GrowsToTheFloor()
    {
        WindowFloor.Grow((1100, 720), (1575, 910)).Should().Be((1575, 910));
    }

    [Fact]
    public void Grow_ALargerWindow_KeepsItsSize()
    {
        WindowFloor.Grow((1800, 1000), (1575, 910)).Should().Be((1800, 1000));
    }

    [Fact]
    public void Grow_GrowsEachSideOnItsOwn()
    {
        WindowFloor.Grow((1800, 720), (1575, 910)).Should().Be((1800, 910));
    }

    private static readonly (int Left, int Top, int Right, int Bottom) Work = (0, 0, 1920, 1032);

    [Fact]
    public void KeepInside_AWindowGrownPastTheBottomRight_MovesBackIn()
    {
        // 175 %: Windows placed the 1100 x 720 window at 220,220; grown to 1575 x 910 it ended at y 1130.
        WindowFloor.KeepInside((220, 220), (1575, 910), Work).Should().Be((220, 122));
    }

    [Fact]
    public void KeepInside_AWindowInside_StaysPut()
    {
        WindowFloor.KeepInside((100, 80), (1100, 720), Work).Should().Be((100, 80));
    }

    [Fact]
    public void KeepInside_AWindowLargerThanTheWorkArea_GoesToItsOrigin()
    {
        WindowFloor.KeepInside((300, 300), (2000, 1100), Work).Should().Be((0, 0));
    }

    [Fact]
    public void KeepInside_AWorkAreaNotAtTheOrigin_IsRespected()
    {
        // A second monitor to the left: its work area starts at a negative x.
        WindowFloor.KeepInside((-500, 900), (1575, 910), (-1920, 40, 0, 1072)).Should().Be((-1575, 162));
    }

    [Fact]
    public void KeepInside_AWindowAboveOrLeftOfTheWorkArea_MovesToItsOrigin()
    {
        WindowFloor.KeepInside((-50, -20), (900, 520), Work).Should().Be((0, 0));
    }
}
