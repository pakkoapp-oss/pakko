using Archiver.Core.Models;
using Archiver.Shell;

namespace Archiver.Shell.Tests;

// T-F268: records every call ShellCommands makes through IOperationUi and answers prompts with
// preset decisions, so a command's prompt/cancel/result paths run without any native dialog.
internal sealed class FakeOperationUi : IOperationUi
{
    public List<FakeOperationSession> Sessions { get; } = [];
    public List<OperationMessage> Messages { get; } = [];
    public List<ConflictInfo> ConflictPrompts { get; } = [];
    public List<(PasswordPromptInfo Info, bool CanApplyToRemaining)> PasswordPrompts { get; } = [];

    public Func<ConflictInfo, ConflictDecision> ConflictAnswer { get; set; } =
        _ => new ConflictDecision { Resolution = ConflictResolution.Skip };

    public Func<PasswordPromptInfo, PasswordDecision> PasswordAnswer { get; set; } =
        _ => new PasswordDecision { Password = null };

    /// <summary>Simulates a progress window that could not be created (Progress is null).</summary>
    public bool NoProgressWindow { get; set; }

    /// <summary>Simulates the user pressing Cancel before the operation does any work.</summary>
    public bool CancelOnBegin { get; set; }

    public IOperationSession Begin(string title, ProgressStyle style)
    {
        var session = new FakeOperationSession(this, title, style);
        if (CancelOnBegin)
            session.Cancel();
        Sessions.Add(session);
        return session;
    }

    public void ShowMessage(OperationMessage message) => Messages.Add(message);
}

internal sealed class FakeOperationSession(FakeOperationUi owner, string title, ProgressStyle style) : IOperationSession
{
    private readonly CancellationTokenSource _cts = new();
    private readonly SynchronousProgress _progress = new();

    public string Title { get; } = title;
    public ProgressStyle Style { get; } = style;
    public bool Completed { get; private set; }
    public bool Disposed { get; private set; }
    public int ProgressReports => _progress.Count;

    public IProgress<ProgressReport>? Progress => owner.NoProgressWindow ? null : _progress;
    public CancellationToken Cancellation => _cts.Token;

    public void Cancel() => _cts.Cancel();

    public Task<ConflictDecision> AskConflictAsync(ConflictInfo info)
    {
        owner.ConflictPrompts.Add(info);
        return Task.FromResult(owner.ConflictAnswer(info));
    }

    public Task<PasswordDecision> AskPasswordAsync(PasswordPromptInfo info, bool canApplyToRemaining)
    {
        owner.PasswordPrompts.Add((info, canApplyToRemaining));
        return Task.FromResult(owner.PasswordAnswer(info));
    }

    public void Complete(OperationMessage? message)
    {
        Completed = true;
        if (message is not null)
            owner.Messages.Add(message);
    }

    public void Dispose()
    {
        Disposed = true;
        _cts.Dispose();
    }

    private sealed class SynchronousProgress : IProgress<ProgressReport>
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Report(ProgressReport value) => Interlocked.Increment(ref _count);
    }
}
