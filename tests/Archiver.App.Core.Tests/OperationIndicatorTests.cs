using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F432: what the App's taskbar button and tray icon show while an operation runs.
public sealed class OperationIndicatorTests
{
    private static OperationIndicator Running(int percent = 0)
    {
        var indicator = new OperationIndicator();
        indicator.Start();
        indicator.Report(percent);
        return indicator;
    }

    // --- Happy path ---

    [Fact]
    public void BeforeAnyOperation_ShowsNothing()
    {
        var indicator = new OperationIndicator();

        indicator.State.Should().Be(TaskbarProgressState.None);
        indicator.Percent.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(100)]
    public void Running_ShowsThePercent(int percent)
    {
        OperationIndicator indicator = Running(percent);

        indicator.State.Should().Be(TaskbarProgressState.Normal);
        indicator.Percent.Should().Be(percent);
    }

    [Fact]
    public void Finalizing_IsIndeterminate_AndBackToNormalAfter()
    {
        OperationIndicator indicator = Running(99);

        indicator.SetIndeterminate(true);
        indicator.State.Should().Be(TaskbarProgressState.Indeterminate);

        indicator.SetIndeterminate(false);
        indicator.State.Should().Be(TaskbarProgressState.Normal);
    }

    [Fact]
    public void ASuccessfulRun_IsClearedWhenTheSummaryShows()
    {
        OperationIndicator indicator = Running(100);

        indicator.Finished(failed: false);

        indicator.State.Should().Be(TaskbarProgressState.None);
    }

    [Fact]
    public void StateValues_AreTheTaskbarsOwnFlags()
    {
        // TaskbarButton passes the value straight to ITaskbarList3::SetProgressState (TBPFLAG).
        ((uint)TaskbarProgressState.None).Should().Be(0x0);
        ((uint)TaskbarProgressState.Indeterminate).Should().Be(0x1);
        ((uint)TaskbarProgressState.Normal).Should().Be(0x2);
        ((uint)TaskbarProgressState.Error).Should().Be(0x4);
        ((uint)TaskbarProgressState.Paused).Should().Be(0x8);
    }

    // --- Security & boundary ---

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(101, 100)]
    [InlineData(int.MaxValue, 100)]
    [InlineData(int.MinValue, 0)]
    public void Report_ClampsThePercent(int reported, int shown)
    {
        Running(reported).Percent.Should().Be(shown);
    }

    // --- Misuse ---

    [Fact]
    public void InputsWithoutARunningOperation_ShowNothing()
    {
        var indicator = new OperationIndicator();

        indicator.Report(50);
        indicator.PromptOpened();
        indicator.Finished(failed: true);

        indicator.State.Should().Be(TaskbarProgressState.None);
    }

    [Fact]
    public void PromptClosedWithoutOpened_NeverGoesBelowNone()
    {
        OperationIndicator indicator = Running(30);

        indicator.PromptClosed();
        indicator.PromptOpened();

        indicator.State.Should().Be(TaskbarProgressState.Paused);
    }

    [Fact]
    public void ANewStart_ForgetsTheLastRun()
    {
        OperationIndicator indicator = Running(80);
        indicator.PromptOpened();
        indicator.Finished(failed: true);
        indicator.End();

        indicator.Start();

        indicator.State.Should().Be(TaskbarProgressState.Normal);
        indicator.Percent.Should().Be(0);
    }

    // --- Error path ---

    [Fact]
    public void AFailedRun_IsRedWhileTheSummaryShows_AndClearedAfter()
    {
        OperationIndicator indicator = Running(100);

        indicator.Finished(failed: true);
        indicator.State.Should().Be(TaskbarProgressState.Error);
        indicator.Percent.Should().Be(100);

        indicator.End();
        indicator.State.Should().Be(TaskbarProgressState.None);
    }

    [Fact]
    public void ACancel_ClearsAtOnce_EvenDuringAPrompt()
    {
        OperationIndicator indicator = Running(40);
        indicator.PromptOpened();

        indicator.Finished(failed: false);

        indicator.State.Should().Be(TaskbarProgressState.None);
    }

    [Fact]
    public void AFailure_OutranksAPromptStillOpen()
    {
        OperationIndicator indicator = Running(40);
        indicator.PromptOpened();

        indicator.Finished(failed: true);

        indicator.State.Should().Be(TaskbarProgressState.Error);
    }

    // --- Interaction ---

    [Fact]
    public void TwoPrompts_StayPausedUntilBothClose_ThenResumeAtTheLastPercent()
    {
        OperationIndicator indicator = Running(20);
        indicator.PromptOpened();
        indicator.PromptOpened();
        indicator.Report(35);

        indicator.PromptClosed();
        indicator.State.Should().Be(TaskbarProgressState.Paused);

        indicator.PromptClosed();
        indicator.State.Should().Be(TaskbarProgressState.Normal);
        indicator.Percent.Should().Be(35);
    }

    [Fact]
    public void Changed_FiresOnlyWhenWhatIsShownChanges()
    {
        OperationIndicator indicator = Running(10);
        int changes = 0;
        indicator.Changed += (_, _) => changes++;

        indicator.Report(10);
        indicator.SetIndeterminate(false);
        indicator.Report(11);
        indicator.PromptOpened();
        indicator.Report(12);

        // 11 changes the percent, the prompt changes the state; a report while paused changes the
        // percent too (the yellow bar still shows how far it got).
        changes.Should().Be(3);
    }

    [Fact]
    public void ReportsFromManyThreads_LeaveAConsistentState()
    {
        OperationIndicator indicator = Running();

        Parallel.For(0, 1000, i =>
        {
            indicator.PromptOpened();
            indicator.Report(i % 101);
            indicator.PromptClosed();
        });

        indicator.State.Should().Be(TaskbarProgressState.Normal);
        indicator.Percent.Should().BeInRange(0, 100);
    }
}
