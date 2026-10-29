namespace Archiver.Shell;

/// <summary>
/// The operation window's time left (T-F431), from the time and the percent since the estimate
/// started, as the App's footer computes it (App.Core's <c>ProgressText.Remaining</c>, T-F303,
/// T-F307). One per archive, like <see cref="Archiver.Core.Services.ProgressSpeedSampler"/>: in a
/// batch the next archive starts a new one.
/// </summary>
internal sealed class ProgressTimeEstimate
{
    private DateTime? _startUtc;
    private int _startPercent;

    /// <summary>The next report starts the estimate again: a checking pass moves no bytes, so its
    /// time would make the rest look slower (T-F307).</summary>
    public void Restart() => _startUtc = null;

    /// <summary>
    /// Seconds left, or null: on the first report (it is the start), before a second has passed,
    /// before the percent has moved past the start, and when under four seconds are left.
    /// </summary>
    public double? SecondsLeft(int percent, DateTime nowUtc)
    {
        if (_startUtc is not { } start)
        {
            (_startUtc, _startPercent) = (nowUtc, percent);
            return null;
        }
        double elapsed = (nowUtc - start).TotalSeconds;
        if (elapsed < 1.0 || percent <= _startPercent)
            return null;
        double left = elapsed * (100 - percent) / (percent - _startPercent);
        return left < 4 ? null : left;
    }
}
