using Archiver.OperationUi.Core;
using Archiver.OperationUi.Protocol;
using FluentAssertions;

namespace Archiver.OperationUi.Tests;

// T-F432: the operation window's taskbar button follows the whole batch, as Explorer's copy does.
public sealed class OperationWindowModelIndicatorTests
{
    private static OperationWindowModel Running(params ProtocolMessage[] then)
    {
        var model = new OperationWindowModel();
        model.Receive(new Hello(FrameCodec.ProtocolVersion, "en-US", RightToLeft: false, new Dictionary<string, string>()));
        model.Receive(new Begin("Extracting", ProgressKind.Bytes));
        foreach (ProtocolMessage m in then)
            model.Receive(m);
        return model;
    }

    private static AskConflict Conflict(int id) =>
        new(id, @"C:\out\a.txt", "1 KB", "2 KB", IncomingIsNewer: false);

    // --- Happy path ---

    [Fact]
    public void ASingleArchive_ShowsItsPercent()
    {
        OperationWindowModel model = Running(new Item("a.zip", 1, 1), new Progress(42, null, null));

        model.IndicatorState.Should().Be(TaskbarProgressState.Normal);
        model.IndicatorPercent.Should().Be(42);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(1, 50, 16)]
    [InlineData(2, 50, 50)]
    [InlineData(3, 100, 100)]
    public void ABatch_ShowsTheWholeBatch(int index, int percent, int shown)
    {
        OperationWindowModel model = Running(new Item("x.zip", index, 3), new Progress(percent, null, null));

        model.IndicatorPercent.Should().Be(shown);
    }

    [Fact]
    public void ANewItem_StartsFromWhereTheLastEnded()
    {
        OperationWindowModel model = Running(new Item("a.zip", 1, 2), new Progress(100, null, null), new Item("b.zip", 2, 2));

        model.IndicatorPercent.Should().Be(50);
    }

    [Fact]
    public void ACleanResult_ClearsTheButton()
    {
        OperationWindowModel model = Running(new Progress(100, null, null),
            new Complete(new ResultText(ResultSeverity.Information, "Done", "ok")));

        model.IndicatorState.Should().Be(TaskbarProgressState.None);
    }

    // --- Security & boundary ---

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 3)]
    [InlineData(5, 3)]
    [InlineData(2, 0)]
    [InlineData(2, -4)]
    public void AnItemOutOfRange_StaysWithinZeroToHundred(int index, int count)
    {
        OperationWindowModel model = Running(new Item("x.zip", index, count), new Progress(100, null, null));

        model.IndicatorPercent.Should().BeInRange(0, 100);
    }

    [Fact]
    public void ALargeBatch_DoesNotOverflow()
    {
        OperationWindowModel model = Running(new Item("x.zip", int.MaxValue, int.MaxValue), new Progress(100, null, null));

        model.IndicatorPercent.Should().Be(100);
    }

    // --- Misuse ---

    [Fact]
    public void ProgressWithoutAnItem_IsTheSingleArchivesPercent()
    {
        OperationWindowModel model = Running(new Progress(30, null, null));

        model.IndicatorPercent.Should().Be(30);
    }

    [Fact]
    public void BeforeBegin_ShowsNothing()
    {
        var model = new OperationWindowModel();
        model.Receive(new Progress(30, null, null));

        model.IndicatorState.Should().Be(TaskbarProgressState.None);
    }

    // --- Error path ---

    [Fact]
    public void AnErrorResult_IsRed_AtTheLastPercent()
    {
        OperationWindowModel model = Running(new Item("a.zip", 2, 2), new Progress(40, null, null),
            new Complete(new ResultText(ResultSeverity.Error, "Failed", "a.zip: damaged")));

        model.IndicatorState.Should().Be(TaskbarProgressState.Error);
        model.IndicatorPercent.Should().Be(70);
    }

    [Fact]
    public void AWarningResult_IsNotRed()
    {
        // The same rule as the App: red only where the summary shows errors (T-F432).
        OperationWindowModel model = Running(new Complete(new ResultText(ResultSeverity.Warning, "Done", "1 skipped")));

        model.IndicatorState.Should().Be(TaskbarProgressState.None);
    }

    [Fact]
    public void AClosedWindow_ShowsNothing()
    {
        OperationWindowModel model = Running(new Progress(40, null, null));

        model.UserClosed();

        model.IndicatorState.Should().Be(TaskbarProgressState.None);
    }

    // --- Interaction ---

    [Fact]
    public void APrompt_IsPausedUntilAnswered()
    {
        OperationWindowModel model = Running(new Progress(40, null, null), Conflict(1), Conflict(2));
        model.IndicatorState.Should().Be(TaskbarProgressState.Paused);

        model.AnswerConflict(ConflictChoice.Skip, applyToAll: false);
        model.IndicatorState.Should().Be(TaskbarProgressState.Paused);

        model.AnswerConflict(ConflictChoice.Skip, applyToAll: false);
        model.IndicatorState.Should().Be(TaskbarProgressState.Normal);
        model.IndicatorPercent.Should().Be(40);
    }
}
