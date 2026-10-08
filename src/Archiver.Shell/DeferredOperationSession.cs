using System.Diagnostics;
using Archiver.Core.Models;

namespace Archiver.Shell;

/// <summary>
/// T-F356: an operation's window that is not started until it is needed — after
/// <paramref name="delay"/>, or at once for a prompt or a result. A fast clean operation ends
/// before that and never starts the window at all. Until then the archive name and the latest
/// progress are kept and handed over, so a window started late opens on the current state.
/// <paramref name="start"/> gets the time the operation has already run.
/// </summary>
internal sealed class DeferredOperationSession : IOperationSession
{
    private readonly Func<TimeSpan, IOperationSession> _start;
    private readonly Stopwatch _running = Stopwatch.StartNew();
    private readonly CancellationTokenSource _cts = new();
    private readonly Timer _timer;
    private readonly DeferredProgress _progress;

    // Guards everything below. Starting the window happens inside it, so nothing reaches the
    // window before the state kept so far has.
    private readonly Lock _lock = new();
    private IOperationSession? _window;
    private (string Name, int Index, int Count)? _item;
    private ProgressReport? _latest;
    private bool _ended;

    public DeferredOperationSession(Func<TimeSpan, IOperationSession> start, TimeSpan delay)
    {
        _start = start;
        _progress = new DeferredProgress(this);
        _timer = new Timer(_ => StartUnlessEnded(), null, delay, Timeout.InfiniteTimeSpan);
    }

    public IProgress<ProgressReport>? Progress => _progress;

    public CancellationToken Cancellation => _cts.Token;

    public void BeginItem(string name, int index, int count)
    {
        lock (_lock)
        {
            _item = (name, index, count);
            // The next archive's bytes start again from zero.
            _latest = null;
            _window?.BeginItem(name, index, count);
        }
    }

    public Task<ConflictDecision> AskConflictAsync(ConflictInfo info) => Window().AskConflictAsync(info);

    public Task<PasswordDecision> AskPasswordAsync(PasswordPromptInfo info, bool canApplyToRemaining) =>
        Window().AskPasswordAsync(info, canApplyToRemaining);

    public Task<bool> ConfirmAsync(ConfirmPrompt prompt) => Window().ConfirmAsync(prompt);

    public void Complete(OperationMessage? message)
    {
        IOperationSession? window;
        if (message is null)
        {
            lock (_lock)
            {
                _ended = true;
                window = _window;
            }
        }
        else
        {
            window = Window();
        }
        // Outside the lock: showing a result waits until the user closes it.
        window?.Complete(message);
    }

    public void Dispose()
    {
        IOperationSession? window;
        lock (_lock)
        {
            _ended = true;
            window = _window;
        }
        _timer.Dispose();
        window?.Dispose();
        _cts.Dispose();
    }

    private void StartUnlessEnded()
    {
        lock (_lock)
        {
            if (!_ended)
                StartLocked();
        }
    }

    private IOperationSession Window()
    {
        lock (_lock)
            return StartLocked();
    }

    // Must hold _lock.
    private IOperationSession StartLocked()
    {
        if (_window is not null)
            return _window;

        IOperationSession window = _start(_running.Elapsed);
        window.Cancellation.Register(Cancel);
        if (_item is { } item)
            window.BeginItem(item.Name, item.Index, item.Count);
        if (_latest is { } report)
            window.Progress?.Report(report);
        _window = window;
        return window;
    }

    private void Cancel()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The session already ended.
        }
    }

    private void Report(ProgressReport report)
    {
        lock (_lock)
        {
            if (_window is null)
                _latest = report;
            else
                _window.Progress?.Report(report);
        }
    }

    private sealed class DeferredProgress(DeferredOperationSession session) : IProgress<ProgressReport>
    {
        public void Report(ProgressReport value) => session.Report(value);
    }
}
