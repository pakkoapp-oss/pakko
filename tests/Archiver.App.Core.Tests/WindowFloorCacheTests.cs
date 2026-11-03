using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F439: the floor was computed again only on a DPI change, so a move to a monitor of the same DPI
// with a smaller work area kept a floor computed for the larger one.
public sealed class WindowFloorCacheTests
{
    private static readonly (int Width, int Height) Large = (2560, 1392);
    private static readonly (int Width, int Height) Small = (1280, 680);

    [Fact]
    public void Changed_TheFirstCall_IsTrue()
    {
        new WindowFloorCache().Changed(96, Large).Should().BeTrue();
    }

    [Fact]
    public void Changed_TheSameDpiAndWorkArea_IsFalse()
    {
        WindowFloorCache cache = new();
        cache.Changed(96, Large);

        cache.Changed(96, Large).Should().BeFalse();
    }

    [Fact]
    public void Changed_TheSameDpiOnASmallerWorkArea_IsTrue()
    {
        WindowFloorCache cache = new();
        cache.Changed(96, Large);

        cache.Changed(96, Small).Should().BeTrue();
    }

    [Fact]
    public void Changed_OnlyOneSideOfTheWorkArea_IsTrue()
    {
        WindowFloorCache cache = new();
        cache.Changed(144, Large);

        cache.Changed(144, (Large.Width, Small.Height)).Should().BeTrue();
    }

    [Fact]
    public void Changed_AnotherDpiOnTheSameWorkArea_IsTrue()
    {
        WindowFloorCache cache = new();
        cache.Changed(96, Large);

        cache.Changed(168, Large).Should().BeTrue();
    }

    [Fact]
    public void Changed_BackToTheFirstMonitor_IsTrue()
    {
        WindowFloorCache cache = new();
        cache.Changed(96, Large);
        cache.Changed(96, Small);

        cache.Changed(96, Large).Should().BeTrue();
    }
}
