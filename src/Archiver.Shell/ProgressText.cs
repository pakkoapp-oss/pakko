using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.Shell;

/// <summary>Progress status and size text for Archiver.Shell's windows.</summary>
internal static class ProgressText
{
    // T-F142: speedSampler is per-operation (a fresh ProgressSpeedSampler per operation, matching
    // Archiver.App's MainViewModel convention of a fresh instance per operation instead of a
    // Reset()). No speed or byte total is shown while TotalBytes is unknown (<= 0).
    public static string FormatStatus(ProgressReport r, ProgressSpeedSampler? speedSampler)
    {
        if (r.TotalBytes <= 0)
            return $"{r.Percent}%";

        string bytesPart = $"{FormatBytes(r.BytesTransferred)} / {FormatBytes(r.TotalBytes)}";
        double speedBytesPerSec = speedSampler?.Sample(r.BytesTransferred, DateTime.UtcNow) ?? 0;
        string speedPart = speedBytesPerSec >= 1 ? $"  ·  {FormatSpeed(speedBytesPerSec)}" : string.Empty;
        return $"{r.Percent}%  ·  {bytesPart}{speedPart}";
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024 => $"{bytes / 1_024.0:F0} KB",
        _ => $"{bytes} B"
    };

    public static string FormatSpeed(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1_073_741_824 => $"{bytesPerSecond / 1_073_741_824:F1} GB/s",
        >= 1_048_576 => $"{bytesPerSecond / 1_048_576:F1} MB/s",
        >= 1_024 => $"{bytesPerSecond / 1_024:F0} KB/s",
        _ => $"{bytesPerSecond:F0} B/s"
    };
}
