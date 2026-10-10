using Archiver.Core.Models;

namespace Archiver.Core.Recovery;

/// <summary>
/// One percent for an archive operation that also writes recovery data (T-F275): the engine's
/// 0-100 is scaled into [0, 100 · (1 - share)), the PAR2 work into the rest, and 100 is sent only
/// by <see cref="Complete"/>, after the last set is written — a frontend shows "Finalizing" on
/// 100. The PAR2 share grows with the percent (5 % → 20 %, 20 % → 50 %), as its work does.
/// Never goes backwards; a PAR2 report is passed on only when the percent changes.
/// </summary>
internal sealed class RecoveryProgressSplit
{
    private readonly IProgress<ProgressReport>? _target;
    private readonly double _archiveShare;
    private readonly Lock _lock = new();
    private int _shown = -1;

    internal RecoveryProgressSplit(IProgress<ProgressReport>? target, int recoveryPercent)
    {
        _target = target;
        _archiveShare = 1.0 - recoveryPercent / (recoveryPercent + 20.0);
        Archive = target is null ? null : new ArchiveRelay(this);
    }

    /// <summary>What the engine reports to; null when nobody listens.</summary>
    internal IProgress<ProgressReport>? Archive { get; }

    /// <summary>The PAR2 work done, 0-1 over every archive.</summary>
    internal void Recovery(double fraction)
    {
        int percent = Scale(_archiveShare * 100 + (1 - _archiveShare) * 100 * Math.Clamp(fraction, 0, 1));
        Send(percent, onlyWhenChanged: true, p => new ProgressReport { Percent = p, Phase = ProgressPhase.CreatingRecoveryData });
    }

    internal void Complete() =>
        Send(100, onlyWhenChanged: true, p => new ProgressReport { Percent = p, Phase = ProgressPhase.CreatingRecoveryData });

    private static int Scale(double percent) => Math.Min(99, (int)percent);

    private void Send(int percent, bool onlyWhenChanged, Func<int, ProgressReport> build)
    {
        if (_target is null)
            return;
        ProgressReport report;
        lock (_lock)
        {
            int shown = Math.Max(_shown, percent);
            if (onlyWhenChanged && shown == _shown)
                return;
            _shown = shown;
            report = build(shown);
        }
        _target.Report(report);
    }

    private sealed class ArchiveRelay(RecoveryProgressSplit owner) : IProgress<ProgressReport>
    {
        public void Report(ProgressReport value) =>
            owner.Send(Scale(Math.Clamp(value.Percent, 0, 100) * owner._archiveShare), onlyWhenChanged: false, p => new ProgressReport
            {
                Percent = p,
                BytesTransferred = value.BytesTransferred,
                TotalBytes = value.TotalBytes,
                CurrentFile = value.CurrentFile,
                Phase = value.Phase,
            });
    }
}
