using Archiver.Core.Models;
using Archiver.Shell;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F356: the operation's window is started only when it is needed. The window here is
// FakeOperationUi's session, so the tests see exactly what a late-started window is handed.
public sealed class DeferredOperationSessionTests
{
    private static readonly TimeSpan Never = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);
    private static readonly OperationMessage Result = new("Extracting", MessageSeverity.Warning, "old.zip: damaged");
    private static readonly ConflictInfo Conflict = new() { ExistingPath =@"C:\out\a.txt" };

    private readonly FakeOperationUi _ui = new();
    private readonly List<TimeSpan> _startedAfter = [];

    private DeferredOperationSession Create(TimeSpan delay) => new(elapsed =>
    {
        lock (_startedAfter)
            _startedAfter.Add(elapsed);
        return _ui.Begin("Extracting: a.zip", ProgressStyle.Bytes);
    }, delay);

    private FakeOperationSession Window => _ui.Sessions.Single();

    private async Task WaitForWindowAsync()
    {
        DateTime limit = DateTime.UtcNow + WaitLimit;
        while (_ui.Sessions.Count == 0)
        {
            if (DateTime.UtcNow > limit)
                throw new TimeoutException("The window was never started.");
            await Task.Delay(10);
        }
    }

    // --- Happy path ---

    [Fact]
    public void FastCleanOperation_NeverStartsTheWindow()
    {
        using (DeferredOperationSession session = Create(Never))
        {
            session.BeginItem("a.zip", 1, 1);
            session.Progress!.Report(new ProgressReport { Percent = 50 });
            session.Complete(null);
        }

        _ui.Sessions.Should().BeEmpty();
        _ui.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task OperationLongerThanTheDelay_StartsTheWindowOnceWithWhatHappenedSoFar()
    {
        using DeferredOperationSession session = Create(TimeSpan.FromMilliseconds(150));
        session.BeginItem("a.zip", 1, 2);
        session.Progress!.Report(new ProgressReport { Percent = 10 });
        session.Progress.Report(new ProgressReport { Percent = 20 });

        await WaitForWindowAsync();
        session.BeginItem("b.zip", 2, 2);
        session.Progress.Report(new ProgressReport { Percent = 60 });

        _startedAfter.Should().ContainSingle().Which.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100));
        Window.Items.Should().Equal(("a.zip", 1, 2), ("b.zip", 2, 2));
        Window.ProgressReports.Should().Be(2, "the latest report before the start, then the one after it");
    }

    [Fact]
    public async Task ResultBeforeTheDelay_StartsTheWindowAndShowsIt()
    {
        using DeferredOperationSession session = Create(Never);

        session.Complete(Result);

        await WaitForWindowAsync();
        Window.Completed.Should().BeTrue();
        _ui.Messages.Should().Equal(Result);
    }

    [Fact]
    public async Task PromptBeforeTheDelay_StartsTheWindowAtOnceAndReturnsItsAnswer()
    {
        _ui.ConflictAnswer = _ => new ConflictDecision { Resolution = ConflictResolution.Overwrite, ApplyToAll = true };
        _ui.PasswordAnswer = _ => new PasswordDecision { Password = "secret" };
        _ui.ConfirmAnswer = true;
        using DeferredOperationSession session = Create(Never);
        session.BeginItem("a.zip", 1, 1);

        ConflictDecision conflict = await session.AskConflictAsync(Conflict);
        PasswordDecision password = await session.AskPasswordAsync(new PasswordPromptInfo { ArchiveName = "a.zip", Purpose = PasswordPurpose.Decrypt }, canApplyToRemaining: true);
        bool confirmed = await session.ConfirmAsync(new ConfirmPrompt("t", "m", "yes", "no"));

        conflict.Should().Be(new ConflictDecision { Resolution = ConflictResolution.Overwrite, ApplyToAll = true });
        password.Password.Should().Be("secret");
        confirmed.Should().BeTrue();
        _ui.Sessions.Should().ContainSingle("three prompts share the one window");
        Window.Items.Should().Equal(("a.zip", 1, 1));
        _ui.PasswordPrompts.Single().CanApplyToRemaining.Should().BeTrue();
    }

    // --- Cancel ---

    [Fact]
    public async Task CancelInTheWindow_CancelsTheOperation()
    {
        using DeferredOperationSession session = Create(TimeSpan.FromMilliseconds(1));
        await WaitForWindowAsync();
        await Task.Delay(50); // the window is listed before it is wired up

        Window.Cancel();

        session.Cancellation.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void WindowAlreadyCancelledWhenItStarts_CancelsTheOperation()
    {
        _ui.CancelOnBegin = true;
        using DeferredOperationSession session = Create(Never);

        _ = session.ConfirmAsync(new ConfirmPrompt("t", "m", "yes", "no"));

        session.Cancellation.IsCancellationRequested.Should().BeTrue();
    }

    // --- Misuse and endings ---

    [Fact]
    public async Task DisposedBeforeTheDelay_NeverStartsTheWindowLater()
    {
        Create(TimeSpan.FromMilliseconds(100)).Dispose();

        await Task.Delay(400);

        _ui.Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task CompletedCleanBeforeTheDelay_NeverStartsTheWindowLater()
    {
        using DeferredOperationSession session = Create(TimeSpan.FromMilliseconds(100));
        session.Complete(null);

        await Task.Delay(400);

        _ui.Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task EndsAfterTheWindowStarted_CompletesAndDisposesTheWindow()
    {
        DeferredOperationSession session = Create(TimeSpan.FromMilliseconds(1));
        await WaitForWindowAsync();

        session.Complete(null);
        session.Dispose();

        Window.Completed.Should().BeTrue();
        Window.Disposed.Should().BeTrue();
    }

    [Fact]
    public void WindowWithoutProgress_ReportsAreDropped()
    {
        _ui.NoProgressWindow = true;
        using DeferredOperationSession session = Create(Never);
        session.Progress!.Report(new ProgressReport { Percent = 10 });

        _ = session.ConfirmAsync(new ConfirmPrompt("t", "m", "yes", "no"));
        Action later = () => session.Progress.Report(new ProgressReport { Percent = 20 });

        later.Should().NotThrow();
    }

    [Fact]
    public void NextArchiveBeforeTheStart_DoesNotHandOverThePreviousArchivesProgress()
    {
        using DeferredOperationSession session = Create(Never);
        session.BeginItem("a.zip", 1, 2);
        session.Progress!.Report(new ProgressReport { Percent = 100 });
        session.BeginItem("b.zip", 2, 2);

        _ = session.ConfirmAsync(new ConfirmPrompt("t", "m", "yes", "no"));

        Window.Items.Should().Equal(("b.zip", 2, 2));
        Window.ProgressReports.Should().Be(0);
    }

    // --- Concurrency ---

    [Fact]
    public async Task PromptsAndTheTimerAtTheSameMoment_StartOneWindow()
    {
        using DeferredOperationSession session = Create(TimeSpan.FromMilliseconds(20));

        _ui.ConfirmAnswer = true;

        bool[] answers = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await Task.Delay(20);
            return await session.ConfirmAsync(new ConfirmPrompt("t", "m", "yes", "no"));
        })));

        // The fake's own prompt list is not written under a lock, so the answers are counted.
        _ui.Sessions.Should().ContainSingle();
        answers.Should().HaveCount(8).And.OnlyContain(answer => answer);
    }
}
