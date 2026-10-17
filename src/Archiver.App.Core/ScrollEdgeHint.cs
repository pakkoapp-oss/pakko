namespace Archiver.App.Core;

/// <summary>
/// T-F405: whether the options panel shows its "more below" shade. The scrollbar appears only on
/// hover, so the shade is the one sign that cards continue under the footer.
/// </summary>
public static class ScrollEdgeHint
{
    // Layout rounding leaves a fraction of a pixel at the very bottom.
    private const double Tolerance = 0.5;

    /// <summary>
    /// True when <paramref name="scrollableHeight"/> (a <c>ScrollViewer</c>'s ScrollableHeight)
    /// is more than <paramref name="verticalOffset"/>: content is hidden below the panel.
    /// </summary>
    public static bool ShowsMoreBelow(double scrollableHeight, double verticalOffset) =>
        scrollableHeight - verticalOffset > Tolerance;
}
