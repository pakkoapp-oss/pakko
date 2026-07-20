using Archiver.Core.Models;

namespace Archiver.Core.IO;

/// <summary>
/// T-F128 follow-up: unlike <see cref="ProgressStream"/> (which tracks percent against a single
/// stream's own length), this tracks bytes read across MANY concurrently-hashed files against one
/// shared folder-wide total — fixing the folder-hash progress bug where the dialog showed each
/// file's own completion (resetting to 0% per file) instead of the whole folder's.
/// </summary>
internal sealed class AggregateProgressTracker
{
    private readonly long _totalBytes;
    private readonly IProgress<ProgressReport> _progress;
    private readonly object _sync = new();
    private long _bytesTransferred;
    private int _lastReportedPercent = -1;

    public AggregateProgressTracker(long totalBytes, IProgress<ProgressReport> progress)
    {
        _totalBytes = totalBytes;
        _progress = progress;
    }

    public void Report(int bytesRead, string? currentFile)
    {
        if (bytesRead <= 0 || _totalBytes <= 0) return;

        lock (_sync)
        {
            _bytesTransferred += bytesRead;
            int pct = (int)Math.Min(100, _bytesTransferred * 100L / _totalBytes);
            if (pct == _lastReportedPercent) return;
            _lastReportedPercent = pct;
            _progress.Report(new ProgressReport
            {
                Percent          = pct,
                BytesTransferred = _bytesTransferred,
                TotalBytes       = _totalBytes,
                CurrentFile      = currentFile
            });
        }
    }
}
