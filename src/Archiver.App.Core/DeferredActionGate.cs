namespace Archiver.App.Core;

/// <summary>
/// Runs actions immediately once "opened," queues them (FIFO) beforehand. Backs MainWindow's
/// "don't mutate FileItems before the root Grid's first Loaded/layout pass" fix (T-F106) — kept
/// WinUI-free (mirrors FileActivationRouter's split) so the queue/flush logic itself is
/// unit-testable even though the actual trigger (Loaded) isn't. Open() is idempotent — a second
/// call is a no-op, not a re-flush.
/// </summary>
public sealed class DeferredActionGate
{
    private readonly List<Action> _pending = [];
    private bool _open;

    public void RunOrDefer(Action action)
    {
        if (_open) { action(); return; }
        _pending.Add(action);
    }

    public void Open()
    {
        if (_open) return;
        _open = true;
        var toRun = _pending.ToArray();
        _pending.Clear();
        foreach (var action in toRun) action();
    }

    /// <summary>Discards any queued actions without running them. Never re-opens a closed gate.</summary>
    public void Cancel() => _pending.Clear();
}
