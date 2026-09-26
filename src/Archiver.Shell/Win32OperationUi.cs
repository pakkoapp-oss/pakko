using System.Runtime.InteropServices;
using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.Shell;

/// <summary>
/// <see cref="IOperationUi"/> on native Win32 windows (T-F268): shell32's
/// <see cref="NativeProgressDialog"/> for progress and cancel, <see cref="ShellConflictDialog"/>
/// and <see cref="PasswordDialog"/> for prompts, <c>MessageBoxW</c> for results. One copy of the
/// progress/cancel plumbing that Extract/Archive/Test, Hash and Scan each carried before.
/// </summary>
internal sealed class Win32OperationUi : IOperationUi
{
    private const uint MbIconError = 0x10;
    private const uint MbIconWarning = 0x30;
    private const uint MbIconInformation = 0x40;

    public IOperationSession Begin(string title, ProgressStyle style) => new Session(title, style);

    public void ShowMessage(OperationMessage message) => Show(message);

    private static void Show(OperationMessage message)
    {
        uint icon = message.Severity switch
        {
            MessageSeverity.Error => MbIconError,
            MessageSeverity.Warning => MbIconWarning,
            _ => MbIconInformation,
        };
        _ = MessageBoxW(IntPtr.Zero, message.Text, message.Title, icon);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private sealed class Session : IOperationSession
    {
        private readonly object _dialogLock = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Timer? _cancelPoll;
        private NativeProgressDialog? _dialog;

        public Session(string title, ProgressStyle style)
        {
            try
            {
                _dialog = new NativeProgressDialog(title);
            }
            catch (COMException)
            {
                // A shell-triggered command must never crash: without the COM object the
                // operation runs with no progress window and no Cancel (Progress stays null).
                return;
            }

            // Polls independently of progress reporting: Report() only fires when a
            // ProgressStream was constructed (totalBytes > 0), so gating Cancel on it left Cancel
            // inert for operations on zero-byte files.
            _cancelPoll = new Timer(_ =>
            {
                lock (_dialogLock)
                {
                    if (_dialog is not null && _dialog.HasUserCancelled())
                        _cts.Cancel();
                }
            }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(250));

            var speedSampler = new ProgressSpeedSampler();
            Progress = new Progress<ProgressReport>(r =>
            {
                lock (_dialogLock)
                {
                    if (_dialog is null)
                        return;
                    if (r.CurrentFile is not null)
                        _dialog.SetLine(1, r.CurrentFile);
                    if (style == ProgressStyle.Percent)
                    {
                        _dialog.SetLine(2, $"{r.Percent}%");
                        _dialog.SetProgress(r.Percent, 100);
                    }
                    else
                    {
                        _dialog.SetLine(2, ProgressText.FormatStatus(r, speedSampler));
                        _dialog.SetProgress(r.BytesTransferred, r.TotalBytes);
                    }
                }
            });
        }

        public IProgress<ProgressReport>? Progress { get; }

        public CancellationToken Cancellation => _cts.Token;

        public Task<ConflictDecision> AskConflictAsync(ConflictInfo info) => ShellConflictDialog.ShowAsync(info);

        public Task<PasswordDecision> AskPasswordAsync(PasswordPromptInfo info, bool canApplyToRemaining) =>
            PasswordDialog.ShowAsync(info, canApplyToRemaining);

        // The progress window closes before the result shows, as it always did.
        public void Complete(OperationMessage? message)
        {
            CloseWindow();
            if (message is not null)
                Show(message);
        }

        public void Dispose()
        {
            CloseWindow();
            _cts.Dispose();
        }

        // The lock keeps a late progress callback or cancel poll from touching the COM object
        // after it has been stopped.
        private void CloseWindow()
        {
            _cancelPoll?.Dispose();
            lock (_dialogLock)
            {
                _dialog?.Dispose();
                _dialog = null;
            }
        }
    }
}
