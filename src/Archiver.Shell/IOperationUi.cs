using Archiver.Core.Models;

namespace Archiver.Shell;

/// <summary>How a progress window renders a <see cref="ProgressReport"/>.</summary>
internal enum ProgressStyle
{
    /// <summary>Percent, bytes done / total and speed.</summary>
    Bytes,

    /// <summary>Percent only (the scan reports entries, not bytes).</summary>
    Percent,
}

/// <summary>How serious a result message is; picks its icon.</summary>
internal enum MessageSeverity
{
    Information,
    Warning,
    Error,
}

/// <summary>A result or error shown to the user after an operation.</summary>
internal sealed record OperationMessage(string Title, MessageSeverity Severity, string Text);

/// <summary>
/// The only way an Explorer command talks to the user (T-F268). Today's implementation is
/// <see cref="Win32OperationUi"/>; a modern window will be a second implementation of this same
/// interface, so the commands themselves never change with the look.
/// </summary>
internal interface IOperationUi
{
    /// <summary>Starts one operation's window. Dispose the session when the operation ends.</summary>
    IOperationSession Begin(string title, ProgressStyle style);

    /// <summary>A message with no operation behind it (the open-in-Pakko hand-off failing).</summary>
    void ShowMessage(OperationMessage message);
}

/// <summary>
/// One operation's window: progress and cancel, the prompts asked while it runs, and its result.
/// Prompts belong here rather than on <see cref="IOperationUi"/> so a modern implementation can
/// show them inside the operation's own window.
/// </summary>
internal interface IOperationSession : IDisposable
{
    /// <summary>Null when no progress window could be created; the operation then runs without one.</summary>
    IProgress<ProgressReport>? Progress { get; }

    /// <summary>Cancelled when the user presses Cancel; stops the whole Explorer command (T-F269).</summary>
    CancellationToken Cancellation { get; }

    /// <summary>
    /// One window covers a whole Explorer command; this names the archive now being processed,
    /// <paramref name="index"/> of <paramref name="count"/> (1-based).
    /// </summary>
    void BeginItem(string name, int index, int count);

    Task<ConflictDecision> AskConflictAsync(ConflictInfo info);

    Task<PasswordDecision> AskPasswordAsync(PasswordPromptInfo info, bool canApplyToRemaining);

    /// <summary>Ends the operation's window and shows <paramref name="message"/>, if any.</summary>
    void Complete(OperationMessage? message);
}
