using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.OperationUi.Protocol;
using BeginMessage = Archiver.OperationUi.Protocol.Begin;
using CompleteMessage = Archiver.OperationUi.Protocol.Complete;
using ProgressMessage = Archiver.OperationUi.Protocol.Progress;

namespace Archiver.Shell;

/// <summary>
/// <see cref="IOperationUi"/> in the WinUI operation window helper (T-F268 step 4), with
/// <paramref name="fallback"/> (the Win32 windows) whenever the helper is not there:
/// <list type="bullet">
/// <item>it cannot start, or is not ready within <see cref="ReadyTimeout"/> — the whole operation uses the fallback;</item>
/// <item>its pipe ends without <see cref="WindowClosed"/> (a crash) — a fallback session takes over the rest
/// of the operation's progress, cancel and result;</item>
/// <item>the user closed it — that is a cancel, never a failover.</item>
/// </list>
/// Prompts are still Win32 dialogs (<paramref name="askConflict"/>, <paramref name="askPassword"/>)
/// until step 5 moves them into the window.
/// </summary>
internal sealed class HelperOperationUi(
    IHelperLauncher launcher,
    IOperationUi fallback,
    Func<ConflictInfo, Task<ConflictDecision>> askConflict,
    Func<PasswordPromptInfo, bool, Task<PasswordDecision>> askPassword) : IOperationUi
{
    private readonly IOperationUi _fallbackUi = fallback;
    private readonly Func<ConflictInfo, Task<ConflictDecision>> _askConflict = askConflict;
    private readonly Func<PasswordPromptInfo, bool, Task<PasswordDecision>> _askPassword = askPassword;

    /// <summary>Gate 0 measured 0.3–1.7 s from start to ready.</summary>
    public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a clean end waits for the helper to confirm it closed before ending it.</summary>
    public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Progress is sent at most this often; the latest report wins.</summary>
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(50);

    public IOperationSession Begin(string title, ProgressStyle style)
    {
        HelperConnection connection;
        try
        {
            connection = launcher.Launch();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            return _fallbackUi.Begin(title, style);
        }
        return new Session(this, connection, title, style);
    }

    public void ShowMessage(OperationMessage message) => _fallbackUi.ShowMessage(message);

    private static ResultText ToResult(OperationMessage message) => new(
        message.Severity switch
        {
            MessageSeverity.Error => ResultSeverity.Error,
            MessageSeverity.Warning => ResultSeverity.Warning,
            _ => ResultSeverity.Information,
        },
        message.Title,
        message.Text);

    private sealed class Session : IOperationSession
    {
        private readonly HelperOperationUi _owner;
        private readonly HelperConnection _connection;
        private readonly MessageWriter _writer;
        private readonly string _title;
        private readonly ProgressStyle _style;
        private readonly CancellationTokenSource _cts = new();
        private readonly ProgressSpeedSampler _speed = new();
        private readonly Timer _readyTimer;

        // Signalled when the helper can no longer show anything: it confirmed its window closed,
        // or it failed. Complete waits on it the way MessageBoxW blocks until dismissed.
        private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Everything below is guarded by _lock. The pump sends queued messages in order; progress
        // is coalesced into one pending slot so a slow helper never blocks the operation.
        private readonly object _lock = new();
        private readonly Queue<ProtocolMessage> _queue = new();
        private readonly SemaphoreSlim _signal = new(0);
        private ProgressMessage? _pendingProgress;
        private (string Name, int Index, int Count)? _item;
        private IOperationSession? _fallback;
        private bool _ready;
        private bool _windowClosed;
        private bool _failed;
        private bool _completing;
        private bool _disposed;

        public Session(HelperOperationUi owner, HelperConnection connection, string title, ProgressStyle style)
        {
            _owner = owner;
            _connection = connection;
            _writer = new MessageWriter(connection.ToHelper);
            _title = title;
            _style = style;
            Progress = new HelperProgress(this);

            Enqueue(OperationWindowText.CreateHello());
            Enqueue(new BeginMessage(title, style == ProgressStyle.Percent ? ProgressKind.Percent : ProgressKind.Bytes));
            _ = Task.Run(PumpAsync);
            _ = Task.Run(ReadAsync);
            _readyTimer = new Timer(_ => OnReadyTimeout(), null, owner.ReadyTimeout, Timeout.InfiniteTimeSpan);
        }

        public IProgress<ProgressReport>? Progress { get; }

        public CancellationToken Cancellation => _cts.Token;

        public void BeginItem(string name, int index, int count)
        {
            IOperationSession? fallback;
            lock (_lock)
            {
                _item = (name, index, count);
                fallback = _fallback;
            }
            if (fallback is not null)
                fallback.BeginItem(name, index, count);
            else
                Enqueue(new Item(name, index, count));
        }

        public Task<ConflictDecision> AskConflictAsync(ConflictInfo info) =>
            CurrentFallback() is { } fallback ? fallback.AskConflictAsync(info) : _owner._askConflict(info);

        public Task<PasswordDecision> AskPasswordAsync(PasswordPromptInfo info, bool canApplyToRemaining) =>
            CurrentFallback() is { } fallback
                ? fallback.AskPasswordAsync(info, canApplyToRemaining)
                : _owner._askPassword(info, canApplyToRemaining);

        public void Complete(OperationMessage? message)
        {
            IOperationSession? fallback;
            bool helperGone;
            lock (_lock)
            {
                _completing = true;
                fallback = _fallback;
                helperGone = _failed || _windowClosed;
            }

            if (fallback is not null)
            {
                fallback.Complete(message);
                return;
            }
            if (helperGone)
            {
                // Closed by the user as the operation finished, or failed before any takeover.
                if (message is not null)
                    _owner._fallbackUi.ShowMessage(message);
                return;
            }

            Enqueue(new CompleteMessage(message is null ? null : ToResult(message)));
            if (message is null)
            {
                _ended.Task.Wait(_owner.CloseTimeout);
                return;
            }

            _ended.Task.Wait();
            bool failed;
            lock (_lock)
                failed = _failed;
            if (failed)
                _owner._fallbackUi.ShowMessage(message);
        }

        public void Dispose()
        {
            bool closeWindow;
            lock (_lock)
            {
                if (_disposed)
                    return;
                closeWindow = !_completing && !_failed && !_windowClosed;
                _completing = true;
            }

            // Disposed without Complete: the operation was cancelled or threw; the window goes away.
            if (closeWindow)
            {
                Enqueue(new CompleteMessage(null));
                _ended.Task.Wait(_owner.CloseTimeout);
            }

            IOperationSession? fallback;
            lock (_lock)
            {
                _disposed = true;
                fallback = _fallback;
            }
            _signal.Release();
            _readyTimer.Dispose();
            // Never waits for the reader: a blocked anonymous-pipe read may not wake on dispose,
            // but it does end once the helper is gone.
            _connection.Kill();
            _connection.Dispose();
            fallback?.Dispose();
            _cts.Dispose();
        }

        private IOperationSession? CurrentFallback()
        {
            lock (_lock)
                return _fallback;
        }

        private void Enqueue(ProtocolMessage message)
        {
            lock (_lock)
            {
                if (_failed || _disposed)
                    return;
                if (_pendingProgress is { } progress)
                {
                    _queue.Enqueue(progress);
                    _pendingProgress = null;
                }
                _queue.Enqueue(message);
            }
            _signal.Release();
        }

        private void Report(ProgressReport report)
        {
            IOperationSession? fallback;
            bool signal;
            lock (_lock)
            {
                fallback = _fallback;
                if (fallback is null)
                {
                    if (_failed || _completing || _windowClosed || _disposed)
                        return;
                    string status = _style == ProgressStyle.Percent
                        ? $"{report.Percent}%"
                        : ProgressText.FormatStatus(report, _speed);
                    signal = _pendingProgress is null;
                    _pendingProgress = new ProgressMessage(report.Percent, report.CurrentFile, status);
                    if (!signal)
                        return;
                }
            }

            if (fallback is not null)
                fallback.Progress?.Report(report);
            else
                _signal.Release();
        }

        private async Task PumpAsync()
        {
            try
            {
                while (true)
                {
                    await _signal.WaitAsync().ConfigureAwait(false);
                    List<ProtocolMessage> batch;
                    lock (_lock)
                    {
                        if (_failed)
                            return;
                        batch = [.. _queue];
                        _queue.Clear();
                        if (_pendingProgress is { } progress)
                        {
                            batch.Add(progress);
                            _pendingProgress = null;
                        }
                        if (batch.Count == 0 && _disposed)
                            return;
                    }

                    foreach (ProtocolMessage message in batch)
                        await _writer.WriteAsync(message, CancellationToken.None).ConfigureAwait(false);
                    if (batch.Count > 0 && batch[^1] is ProgressMessage)
                        await Task.Delay(_owner.ProgressInterval).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                Fail();
            }
        }

        private async Task ReadAsync()
        {
            try
            {
                while (await FrameCodec.ReadAsync(_connection.FromHelper, CancellationToken.None).ConfigureAwait(false) is { } message)
                {
                    switch (message)
                    {
                        case HelperReady ready when ready.ProtocolVersion == FrameCodec.ProtocolVersion:
                            lock (_lock)
                                _ready = true;
                            break;

                        case HelperReady:
                            Fail();
                            return;

                        case CancelRequested:
                            CancelOperation();
                            break;

                        case WindowClosed:
                            lock (_lock)
                                _windowClosed = true;
                            _ended.TrySetResult();
                            return;
                    }
                }
            }
            catch (Exception ex) when (ex is ProtocolException or IOException or ObjectDisposedException)
            {
                // Garbage or a broken pipe: the same as a crash below.
            }
            Fail();
        }

        private void OnReadyTimeout()
        {
            bool ready;
            lock (_lock)
                ready = _ready;
            if (!ready)
                Fail();
        }

        // The helper is gone without closing its window. A fallback session carries the operation on
        // unless it is already ending; either way nothing is decided on the user's behalf.
        private void Fail()
        {
            lock (_lock)
            {
                if (_failed || _windowClosed || _disposed)
                    return;
                _failed = true;
                _queue.Clear();
                _pendingProgress = null;
                if (!_completing)
                {
                    // Set up completely before it is published: a BeginItem racing this must not
                    // be overwritten by the replay of the archive it replaced.
                    IOperationSession fallback = _owner._fallbackUi.Begin(_title, _style);
                    fallback.Cancellation.Register(CancelOperation);
                    if (_item is { } item)
                        fallback.BeginItem(item.Name, item.Index, item.Count);
                    _fallback = fallback;
                }
            }

            _signal.Release();
            _connection.Kill();
            _ended.TrySetResult();
        }

        private void CancelOperation()
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

        private sealed class HelperProgress(Session session) : IProgress<ProgressReport>
        {
            public void Report(ProgressReport value) => session.Report(value);
        }
    }
}
