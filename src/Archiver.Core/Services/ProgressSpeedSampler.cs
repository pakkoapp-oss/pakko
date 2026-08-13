namespace Archiver.Core.Services;

/// <summary>
/// EMA-smoothed transfer-speed sampler shared between Archiver.App's status line and
/// Archiver.Shell's progress dialog (T-F142) — extracted from what was private, untested
/// arithmetic inline in Archiver.App's MainViewModel.UpdateOperationStatus. Public (not internal),
/// matching Archiver.Core.IO.Crc32's own precedent for a dependency-free algorithm needed by
/// multiple frontends. Sampling only: byte/speed string formatting (localization-adjacent in the
/// App, plain text in the Shell dialog) and ETA (derived from Percent, not bytes — works already,
/// isn't shared) both stay separate per frontend, not folded in here. Callers gate on
/// <c>TotalBytes &gt; 0</c> themselves before calling <see cref="Sample"/> — a report with no real
/// byte total carries no speed signal at all, and deciding to skip it is the caller's job, not
/// this sampler's, so it stays a pure function of (bytes, time). Construct a fresh instance per
/// operation (Archive/Extract) rather than reusing one across operations — there is no
/// <c>Reset()</c> by design, so a stale carried-over speed from reusing an instance across two
/// operations is not possible.
/// </summary>
public sealed class ProgressSpeedSampler
{
    private const double SmoothingAlpha = 0.25;
    private const double MinSampleIntervalSeconds = 0.25;

    private long _lastBytesTransferred;
    private DateTime _lastSampleTimeUtc;
    private double _smoothedBytesPerSecond;

    /// <summary>Starts a fresh sampler — construct one per operation (see class remarks).</summary>
    public ProgressSpeedSampler(DateTime? startTimeUtc = null)
    {
        _lastSampleTimeUtc = startTimeUtc ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the running EMA from a new (bytesTransferred, nowUtc) sample and returns the
    /// current smoothed bytes/sec (0 until the first valid sample). A sample is only folded in
    /// once at least <see cref="MinSampleIntervalSeconds"/> has elapsed since the last one AND
    /// bytesTransferred has actually advanced — this also means a non-increasing or repeated
    /// bytesTransferred (an out-of-order report, or the same report delivered twice) is tolerated
    /// by construction: it is simply ignored, never treated as a negative delta or a spike, and
    /// the last known smoothed value is returned unchanged.
    /// </summary>
    public double Sample(long bytesTransferred, DateTime nowUtc)
    {
        long bytesDelta = bytesTransferred - _lastBytesTransferred;
        double timeDelta = (nowUtc - _lastSampleTimeUtc).TotalSeconds;

        if (timeDelta >= MinSampleIntervalSeconds && bytesDelta > 0)
        {
            double instantSpeed = bytesDelta / timeDelta;
            // `< 1` (not a `_hasSample` bool) is preserved verbatim from the original
            // MainViewModel arithmetic this was extracted from, for exact behavioral identity —
            // it doubles as the "no real sample yet" sentinel, since a genuine transfer speed
            // below 1 byte/sec is not a real-world case worth a separate code path for.
            _smoothedBytesPerSecond = _smoothedBytesPerSecond < 1
                ? instantSpeed
                : SmoothingAlpha * instantSpeed + (1 - SmoothingAlpha) * _smoothedBytesPerSecond;
            _lastBytesTransferred = bytesTransferred;
            _lastSampleTimeUtc = nowUtc;
        }

        return _smoothedBytesPerSecond;
    }
}
