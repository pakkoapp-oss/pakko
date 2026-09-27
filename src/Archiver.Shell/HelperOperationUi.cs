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
/// Conflict and password prompts are asked inside the window (step 5). A prompt still open when the
/// helper fails is asked again through the fallback; one open or raised after the user cancelled
/// gets the answer that writes nothing (Skip, no password) without asking, as the operation is ending.
/// </summary>
internal sealed class HelperOperationUi(IHelperLauncher launcher, IOperationUi fallback) : IOperationUi
{
    private static readonly ConflictDecision SafeConflict = new() { Resolution = ConflictResolution.Skip };
    private static readonly PasswordDecision SafePassword = new() { Password = null };

    private readonly IOperationUi _fallbackUi = fallback;

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

    private sealed class Session : IOperationSession
    {
        private readonly HelperOperationUi _owner;
        private readonly HelperConnection _connection;
        private readonly MessageWriter _writer;
        private readonly string _title;
        private readonly ProgressStyle _style;
        private readonly CancellationTokenSource _cts = new();
        private ProgressSpeedSampler _speed = new();
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
        private readonly Dictionary<int, PendingConflict> _conflicts = [];
        private readonly Dictionary<int, PendingPassword> _passwords = [];
        private int _nextRequestId;
        private bool _ready;
        private bool _cancelRequested;
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
            // The operation's own token is not passed anywhere here: after Cancel the window still
            // has to be told to close, and its WindowClosed still has to be read.
            _ = Task.Run(PumpAsync, CancellationToken.None);
            _ = Task.Run(ReadAsync, CancellationToken.None);
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
                // Each archive's bytes start again from zero, which the old sampler ignores.
                _speed = new ProgressSpeedSampler();
                fallback = _fallback;
            }
            if (fallback is not null)
                fallback.BeginItem(name, index, count);
            else
                Enqueue(new Item(name, index, count));
        }

        public Task<ConflictDecision> AskConflictAsync(ConflictInfo info)
        {
            // Reads the existing file's size and date, so it stays outside the lock.
            AskConflict ask = OperationWindowText.CreateAskConflict(0, info);
            var pending = new PendingConflict(info, new TaskCompletionSource<ConflictDecision>(TaskCreationOptions.RunContinuationsAsynchronously));
            IOperationSession? fallback;
            lock (_lock)
            {
                fallback = _fallback;
                if (fallback is null)
                {
                    if (!CanAskLocked())
                        return Task.FromResult(SafeConflict);
                    int id = ++_nextRequestId;
                    _conflicts.Add(id, pending);
                    Enqueue(ask with { RequestId = id });
                }
            }
            return fallback is not null ? fallback.AskConflictAsync(info) : pending.Answer.Task;
        }

        public Task<PasswordDecision> AskPasswordAsync(PasswordPromptInfo info, bool canApplyToRemaining)
        {
            var pending = new PendingPassword(info, canApplyToRemaining, new TaskCompletionSource<PasswordDecision>(TaskCreationOptions.RunContinuationsAsynchronously));
            IOperationSession? fallback;
            lock (_lock)
            {
                fallback = _fallback;
                if (fallback is null)
                {
                    if (!CanAskLocked())
                        return Task.FromResult(SafePassword);
                    int id = ++_nextRequestId;
                    _passwords.Add(id, pending);
                    Enqueue(new AskPassword(id, info.ArchiveName, info.AttemptNumber, info.PreviousAttemptWasWrong, canApplyToRemaining));
                }
            }
            return fallback is not null ? fallback.AskPasswordAsync(info, canApplyToRemaining) : pending.Answer.Task;
        }

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
                _ended.Task.Wait(_owner.CloseTimeout, CancellationToken.None);
                return;
            }

            _ended.Task.Wait(CancellationToken.None);
            bool failed;
            lock (_lock)
                failed = _failed;
            if (failed)
                _owner._fallbackUi.ShowMessage(message);
        }

        public void Dispose()
        {
            bool closeWindow;
            (PendingConflict[] conflicts, PendingPassword[] passwords) open;
            lock (_lock)
            {
                if (_disposed)
                    return;
                open = TakePromptsLocked();
                closeWindow = !_completing && !_failed && !_windowClosed;
                _completing = true;
            }
            AnswerSafely(open);

            // Disposed without Complete: the operation was cancelled or threw; the window goes away.
            if (closeWindow)
            {
                Enqueue(new CompleteMessage(null));
                _ended.Task.Wait(_owner.CloseTimeout, CancellationToken.None);
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

        // Must hold _lock. No helper to ask and no fallback yet: failed while completing, cancelled
        // by the user, or ending.
        private bool CanAskLocked() => !_failed && !_cancelRequested && !_windowClosed && !_disposed;

        // Must hold _lock. Whoever takes a prompt out of the maps is the one who completes it.
        private (PendingConflict[] Conflicts, PendingPassword[] Passwords) TakePromptsLocked()
        {
            PendingConflict[] conflicts = [.. _conflicts.Values];
            PendingPassword[] passwords = [.. _passwords.Values];
            _conflicts.Clear();
            _passwords.Clear();
            return (conflicts, passwords);
        }

        private static void AnswerSafely((PendingConflict[] Conflicts, PendingPassword[] Passwords) open)
        {
            foreach (PendingConflict conflict in open.Conflicts)
                conflict.Answer.TrySetResult(SafeConflict);
            foreach (PendingPassword password in open.Passwords)
                password.Answer.TrySetResult(SafePassword);
        }

        // Off the calling thread: Fail runs on the pipe reader and the ready timer, and a Win32
        // dialog blocks its caller until dismissed.
        private static void AskAgain((PendingConflict[] Conflicts, PendingPassword[] Passwords) open, IOperationSession fallback)
        {
            foreach (PendingConflict conflict in open.Conflicts)
                _ = Task.Run(() => ForwardAsync(() => fallback.AskConflictAsync(conflict.Info), conflict.Answer), CancellationToken.None);
            foreach (PendingPassword password in open.Passwords)
                _ = Task.Run(() => ForwardAsync(() => fallback.AskPasswordAsync(password.Info, password.CanApplyToRemaining), password.Answer), CancellationToken.None);
        }

        private static async Task ForwardAsync<T>(Func<Task<T>> ask, TaskCompletionSource<T> answer)
        {
            try
            {
                answer.TrySetResult(await ask().ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                // The operation awaits this prompt; it gets the fallback's own failure.
                answer.TrySetException(ex);
            }
        }

        private void OnConflictAnswer(ConflictAnswer answer)
        {
            PendingConflict? pending;
            lock (_lock)
                _conflicts.Remove(answer.RequestId, out pending);
            pending?.Answer.TrySetResult(new ConflictDecision
            {
                // The helper is a separate process: anything but these two never overwrites.
                Resolution = answer.Choice switch
                {
                    ConflictChoice.Overwrite => ConflictResolution.Overwrite,
                    ConflictChoice.Rename => ConflictResolution.Rename,
                    _ => ConflictResolution.Skip,
                },
                ApplyToAll = answer.ApplyToAll,
            });
        }

        private void OnPasswordAnswer(PasswordAnswer answer)
        {
            PendingPassword? pending;
            lock (_lock)
                _passwords.Remove(answer.RequestId, out pending);
            pending?.Answer.TrySetResult(new PasswordDecision
            {
                Password = answer.Password,
                ApplyToRemaining = answer.ApplyToRemaining && pending.CanApplyToRemaining,
            });
        }

        // Cancel or X in the window, while it ran: the operation is ending, so an open prompt and
        // any later one are answered without asking.
        private void OnCancelRequested()
        {
            (PendingConflict[] conflicts, PendingPassword[] passwords) open;
            lock (_lock)
            {
                _cancelRequested = true;
                open = TakePromptsLocked();
            }
            AnswerSafely(open);
            CancelOperation();
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
                    await _signal.WaitAsync(CancellationToken.None).ConfigureAwait(false);
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
                        await Task.Delay(_owner.ProgressInterval, CancellationToken.None).ConfigureAwait(false);
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
                            OnCancelRequested();
                            break;

                        case ConflictAnswer answer:
                            OnConflictAnswer(answer);
                            break;

                        case PasswordAnswer answer:
                            OnPasswordAnswer(answer);
                            break;

                        case WindowClosed:
                            (PendingConflict[] conflicts, PendingPassword[] passwords) open;
                            lock (_lock)
                            {
                                _windowClosed = true;
                                open = TakePromptsLocked();
                            }
                            AnswerSafely(open);
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
        // unless it is already ending; an open prompt is asked again there, never decided for the user.
        private void Fail()
        {
            (PendingConflict[] conflicts, PendingPassword[] passwords) open;
            IOperationSession? takeover;
            lock (_lock)
            {
                if (_failed || _windowClosed || _disposed)
                    return;
                _failed = true;
                open = TakePromptsLocked();
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
                takeover = _fallback;
            }

            if (takeover is not null)
                AskAgain(open, takeover);
            else
                AnswerSafely(open);
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

        private static ResultText ToResult(OperationMessage message) => new(
            message.Severity switch
            {
                MessageSeverity.Error => ResultSeverity.Error,
                MessageSeverity.Warning => ResultSeverity.Warning,
                _ => ResultSeverity.Information,
            },
            message.Title,
            message.Text,
            message.Preformatted);

        private sealed record PendingConflict(ConflictInfo Info, TaskCompletionSource<ConflictDecision> Answer);

        private sealed record PendingPassword(PasswordPromptInfo Info, bool CanApplyToRemaining, TaskCompletionSource<PasswordDecision> Answer);

        private sealed class HelperProgress(Session session) : IProgress<ProgressReport>
        {
            public void Report(ProgressReport value) => session.Report(value);
        }
    }
}
