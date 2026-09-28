namespace Archiver.App.Core;

/// <summary>
/// T-F201: Pakko is multi-instance by design (T-F88), and every new window opened at the same
/// default spot, exactly over the one already open. Cascades a new window's top-left corner off
/// every other Pakko window, staying inside the work area.
/// </summary>
public static class WindowCascade
{
    /// <summary>Offset between cascaded windows, in pixels.</summary>
    public const int Step = 32;

    // Corners closer than this count as "the same spot" — Windows' own default placement drifts
    // by a few pixels between launches.
    private const int SameSpotTolerance = Step / 2;

    // Enough steps to leave any realistic stack of windows; after that the default spot is used.
    private const int MaxSteps = 32;

    /// <summary>
    /// Returns where the new window's top-left corner goes: <paramref name="proposedLeft"/>/
    /// <paramref name="proposedTop"/> when no other window sits there, else the next cascade step
    /// that is free, wrapping to the work area's corner when a step would push the window out.
    /// </summary>
    public static (int Left, int Top) Place(
        int proposedLeft, int proposedTop, int width, int height,
        IReadOnlyList<(int Left, int Top)> otherWindows,
        (int Left, int Top, int Right, int Bottom) workArea)
    {
        int left = proposedLeft;
        int top = proposedTop;
        for (int step = 0; step < MaxSteps; step++)
        {
            if (!otherWindows.Any(w => Math.Abs(w.Left - left) < SameSpotTolerance && Math.Abs(w.Top - top) < SameSpotTolerance))
                return (left, top);

            left += Step;
            top += Step;
            if (left + width > workArea.Right || top + height > workArea.Bottom)
            {
                left = workArea.Left;
                top = workArea.Top;
            }
        }
        return (proposedLeft, proposedTop);
    }
}
