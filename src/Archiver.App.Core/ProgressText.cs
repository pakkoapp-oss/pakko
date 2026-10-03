using System.Globalization;
using System.Text;

namespace Archiver.App.Core;

/// <summary>
/// The footer's speed and time left during an operation (T-F303), in the same localized units as
/// the list's sizes (<see cref="DisplayText"/>). App.Core has no resource loader, so the App sets
/// the words once at startup; until then they are English, which is what the tests see.
/// </summary>
public static class ProgressText
{
    private static CompositeFormat _perSecond = CompositeFormat.Parse("{0}/s");
    private static CompositeFormat _secondsRemaining = CompositeFormat.Parse("~{0} sec remaining");
    private static CompositeFormat _minutesRemaining = CompositeFormat.Parse("~{0} remaining");

    /// <summary>Called once by the App: the per-second template takes a size as {0}; the
    /// remaining templates take whole seconds, and minutes as "m:ss".</summary>
    public static void Configure(string perSecond, string secondsRemaining, string minutesRemaining)
    {
        _perSecond = CompositeFormat.Parse(perSecond);
        _secondsRemaining = CompositeFormat.Parse(secondsRemaining);
        _minutesRemaining = CompositeFormat.Parse(minutesRemaining);
    }

    /// <summary>A transfer speed, e.g. "54,5 MB/s".</summary>
    public static string Speed(double bytesPerSecond) =>
        string.Format(CultureInfo.CurrentCulture, _perSecond, DisplayText.FormatSize((long)bytesPerSecond));

    /// <summary>
    /// The time left estimated from the time so far and the percent done; empty before the first
    /// second, before any progress, and when under four seconds are left.
    /// </summary>
    public static string Remaining(TimeSpan elapsed, int percent) => Remaining(elapsed, percent, 0);

    /// <summary>As <see cref="Remaining(TimeSpan, int)"/>, with <paramref name="elapsed"/> measured
    /// from the moment the bar stood at <paramref name="startPercent"/> (T-F307).</summary>
    public static string Remaining(TimeSpan elapsed, int percent, int startPercent)
    {
        if (elapsed.TotalSeconds < 1.0 || percent <= startPercent)
            return string.Empty;
        double remaining = elapsed.TotalSeconds * (100 - percent) / (percent - startPercent);
        return remaining switch
        {
            < 4 => string.Empty,
            < 60 => string.Format(CultureInfo.CurrentCulture, _secondsRemaining, (int)remaining),
            _ => string.Format(CultureInfo.CurrentCulture, _minutesRemaining,
                $"{(int)(remaining / 60)}:{(int)(remaining % 60):D2}"),
        };
    }
}
