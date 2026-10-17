using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F405: the shade above the footer.
public sealed class ScrollEdgeHintTests
{
    [Fact]
    public void ContentBelow_ShowsTheShade()
    {
        ScrollEdgeHint.ShowsMoreBelow(scrollableHeight: 300, verticalOffset: 0).Should().BeTrue();
        ScrollEdgeHint.ShowsMoreBelow(scrollableHeight: 300, verticalOffset: 299).Should().BeTrue();
    }

    [Fact]
    public void ScrolledToTheEnd_HidesTheShade()
    {
        ScrollEdgeHint.ShowsMoreBelow(scrollableHeight: 300, verticalOffset: 300).Should().BeFalse();
    }

    // Layout rounding: an offset a fraction short of the end is the end.
    [Theory]
    [InlineData(300, 299.6)]
    [InlineData(0.4, 0)]
    public void AFractionShortOfTheEnd_HidesTheShade(double scrollableHeight, double verticalOffset)
    {
        ScrollEdgeHint.ShowsMoreBelow(scrollableHeight, verticalOffset).Should().BeFalse();
    }

    [Fact]
    public void NothingToScroll_HidesTheShade()
    {
        ScrollEdgeHint.ShowsMoreBelow(scrollableHeight: 0, verticalOffset: 0).Should().BeFalse();
    }

    // A ScrollViewer not yet laid out reports NaN; an overscroll can pass the end.
    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(300, double.NaN)]
    [InlineData(300, 320)]
    public void UnusableNumbers_HideTheShade(double scrollableHeight, double verticalOffset)
    {
        ScrollEdgeHint.ShowsMoreBelow(scrollableHeight, verticalOffset).Should().BeFalse();
    }
}
