using System.Globalization;
using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.Shell;

/// <summary>Progress status and size text for Archiver.Shell's windows.</summary>
internal static class ProgressText
{
    // T-F142: speedSampler is per-operation (a fresh ProgressSpeedSampler per operation, matching
    // Archiver.App's MainViewModel convention of a fresh instance per operation instead of a
    // Reset()). No speed or byte total is shown while TotalBytes is unknown (<= 0).
    // T-F431: the time left needs only the percent, so it is shown without a byte total too. The
    // IProgressDialog fallback passes no estimate: its AutoTime line already shows one.
    public static string FormatStatus(ProgressReport r, ProgressSpeedSampler? speedSampler, ProgressTimeEstimate? timeLeft = null)
    {
        if (r.Phase == ProgressPhase.CheckingArchive)
        {
            timeLeft?.Restart();
            return OperationTextLocalizer.Get("StatusCheckingArchive");
        }
        string timePart = timeLeft?.SecondsLeft(r.Percent, DateTime.UtcNow) is { } seconds
            ? $"  ·  {FormatTimeLeft(seconds)}"
            : string.Empty;
        if (r.TotalBytes <= 0)
            return $"{r.Percent}%{timePart}";

        string bytesPart = $"{FormatBytes(r.BytesTransferred)} / {FormatBytes(r.TotalBytes)}";
        double speedBytesPerSec = speedSampler?.Sample(r.BytesTransferred, DateTime.UtcNow) ?? 0;
        string speedPart = speedBytesPerSec >= 1 ? $"  ·  {FormatSpeed(speedBytesPerSec)}" : string.Empty;
        return $"{r.Percent}%  ·  {bytesPart}{speedPart}{timePart}";
    }

    /// <summary>Whole seconds under a minute, "m:ss" from a minute on, as the App's footer.</summary>
    public static string FormatTimeLeft(double seconds) => seconds < 60
        ? OperationTextLocalizer.Get("RemainingSeconds", (int)seconds)
        : OperationTextLocalizer.Get("RemainingMinutes", $"{(int)(seconds / 60)}:{(int)(seconds % 60):D2}");

    // T-F208: the unit follows the display language (resx), the number the regional format.
    public static string FormatBytes(long bytes) => FormatSize(bytes);

    public static string FormatSpeed(double bytesPerSecond) =>
        OperationTextLocalizer.Get("UnitPerSecond", FormatSize(bytesPerSecond));

    private static string FormatSize(double bytes) => bytes switch
    {
        >= 1_073_741_824 => OperationTextLocalizer.Get("UnitGB", (bytes / 1_073_741_824).ToString("F1", CultureInfo.CurrentCulture)),
        >= 1_048_576 => OperationTextLocalizer.Get("UnitMB", (bytes / 1_048_576).ToString("F1", CultureInfo.CurrentCulture)),
        >= 1_024 => OperationTextLocalizer.Get("UnitKB", (bytes / 1_024).ToString("F0", CultureInfo.CurrentCulture)),
        _ => OperationTextLocalizer.Get("UnitB", bytes.ToString("F0", CultureInfo.CurrentCulture))
    };
}
