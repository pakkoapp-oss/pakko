using Archiver.OperationUi.Core;
using Archiver.OperationUi.Protocol;
using FluentAssertions;

namespace Archiver.OperationUi.Tests;

// T-F268 step 4: the operation window's state machine, tested without WinUI.
public sealed class OperationWindowModelTests
{
    private static readonly ResultText Warning = new(ResultSeverity.Warning, "Extracting", "old.zip: damaged");

    private static OperationWindowModel Running(params ProtocolMessage[] then)
    {
        var model = new OperationWindowModel();
        model.Receive(new Hello(FrameCodec.ProtocolVersion, "uk-UA", RightToLeft: false, new Dictionary<string, string>
        {
            [WindowStrings.Cancel] = "Скасувати",
            [WindowStrings.CancelAll] = "Скасувати все",
            [WindowStrings.Close] = "Закрити",
            [WindowStrings.ItemOfCount] = "Архів {0} з {1} · {2}",
        }));
        model.Receive(new Begin("Розпакування", ProgressKind.Bytes));
        foreach (var m in then)
            model.Receive(m);
        return model;
    }

    // --- Happy path ---

    [Fact]
    public void Begin_StaysHiddenUntilTheShowDelay()
    {
        var model = Running(new Progress(40, "a.txt", "40%"));

        model.IsVisible.Should().BeFalse();
        model.ShowDelayElapsed().Command.Should().Be(WindowCommand.Show);
        model.IsVisible.Should().BeTrue();
        model.Percent.Should().Be(40);
        model.CurrentFile.Should().Be("a.txt");
        model.Status.Should().Be("40%");
    }

    [Fact]
    public void CleanCompleteBeforeTheDelay_ClosesWithoutEverShowing()
    {
        var model = Running();

        var update = model.Receive(new Complete(null));

        update.Command.Should().Be(WindowCommand.Close);
        update.Send.Should().Equal(new WindowClosed());
        model.IsVisible.Should().BeFalse();
        model.ShowDelayElapsed().Should().Be(WindowUpdate.Nothing);
    }

    [Fact]
    public void ResultBeforeTheDelay_ShowsTheResultAtOnce()
    {
        var model = Running();

        model.Receive(new Complete(Warning)).Command.Should().Be(WindowCommand.Show);

        model.Phase.Should().Be(WindowPhase.Result);
        model.Result.Should().Be(Warning);
    }

    [Fact]
    public void ResultAfterTheWindowShowed_Refreshes()
    {
        var model = Running();
        model.ShowDelayElapsed();

        model.Receive(new Complete(Warning)).Command.Should().Be(WindowCommand.Refresh);
    }

    [Fact]
    public void ProgressWhileVisible_Refreshes_WhileHidden_DoesNothing()
    {
        var model = Running();
        model.Receive(new Progress(10, null, null)).Should().Be(WindowUpdate.Nothing);

        model.ShowDelayElapsed();

        model.Receive(new Progress(20, null, null)).Command.Should().Be(WindowCommand.Refresh);
    }

    [Fact]
    public void SeveralArchives_NameTheCurrentOneAndCancelAll()
    {
        var model = Running(new Item("photos.zip", 2, 5));

        model.ItemLine.Should().Be("Архів 2 з 5 · photos.zip");
        model.CancelLabel.Should().Be("Скасувати все");
        model.CloseLabel.Should().Be("Закрити");
    }

    [Fact]
    public void OneArchive_HasNoItemLineAndPlainCancel()
    {
        var model = Running(new Item("photos.zip", 1, 1));

        model.ItemLine.Should().BeNull();
        model.CancelLabel.Should().Be("Скасувати");
    }

    [Fact]
    public void CloseWhileRunning_CancelsTheOperationThenCloses()
    {
        var model = Running();

        var update = model.UserClosed();

        update.Command.Should().Be(WindowCommand.Close);
        update.Send.Should().Equal(new CancelRequested(), new WindowClosed());
        model.Phase.Should().Be(WindowPhase.Closed);
    }

    [Fact]
    public void CloseOnTheResult_OnlyCloses()
    {
        var model = Running(new Complete(Warning));

        model.UserClosed().Send.Should().Equal(new WindowClosed());
    }

    [Fact]
    public void Hello_CarriesCultureAndDirection()
    {
        var model = new OperationWindowModel();

        model.Receive(new Hello(FrameCodec.ProtocolVersion, "ar-SA", RightToLeft: true, new Dictionary<string, string>()));

        model.Culture.Should().Be("ar-SA");
        model.RightToLeft.Should().BeTrue();
    }

    // --- Security & boundary ---

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(150, 100)]
    public void Percent_IsClampedToTheBar(int sent, int shown)
    {
        Running(new Progress(sent, null, null)).Percent.Should().Be(shown);
    }

    [Fact]
    public void MissingLabel_RendersAsItsKey()
    {
        var model = new OperationWindowModel();
        model.Receive(new Begin("t", ProgressKind.Percent));

        model.CancelLabel.Should().Be(WindowStrings.Cancel);
    }

    [Fact]
    public void NextItem_ResetsTheFileLine()
    {
        var model = Running(new Item("a.zip", 1, 2), new Progress(100, "last.txt", "100%"), new Item("b.zip", 2, 2));

        model.Percent.Should().Be(0);
        model.CurrentFile.Should().BeNull();
        model.Status.Should().BeNull();
    }

    // --- Misuse ---

    [Fact]
    public void ProgressBeforeBegin_IsIgnored()
    {
        var model = new OperationWindowModel();

        model.Receive(new Progress(50, "x", "y"));

        model.Percent.Should().Be(0);
        model.Phase.Should().Be(WindowPhase.Starting);
    }

    [Fact]
    public void SecondBegin_IsIgnored()
    {
        var model = Running(new Begin("other", ProgressKind.Percent));

        model.Title.Should().Be("Розпакування");
        model.Kind.Should().Be(ProgressKind.Bytes);
    }

    [Fact]
    public void AfterClose_EverythingIsIgnored()
    {
        var model = Running(new Complete(null));

        model.Receive(new Complete(Warning)).Should().Be(WindowUpdate.Nothing);
        model.UserClosed().Should().Be(WindowUpdate.Nothing);
        model.ShellDisconnected().Should().Be(WindowUpdate.Nothing);
        model.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void ShowDelay_ShowsOnlyOnce()
    {
        var model = Running();
        model.ShowDelayElapsed();

        model.ShowDelayElapsed().Should().Be(WindowUpdate.Nothing);
    }

    [Fact]
    public void AHelperSideMessageFromShell_IsIgnored()
    {
        var model = Running();

        model.Receive(new CancelRequested()).Should().Be(WindowUpdate.Nothing);
        model.Phase.Should().Be(WindowPhase.Running);
    }

    // --- Error path ---

    [Fact]
    public void ShellGoneWhileRunning_ClosesWithNothingToSend()
    {
        var model = Running();

        var update = model.ShellDisconnected();

        update.Command.Should().Be(WindowCommand.Close);
        update.Send.Should().BeEmpty();
    }

    [Fact]
    public void ShellGoneWhileAResultShows_KeepsTheResult()
    {
        var model = Running(new Complete(Warning));

        model.ShellDisconnected().Should().Be(WindowUpdate.Nothing);
        model.Phase.Should().Be(WindowPhase.Result);
        model.UserClosed().Command.Should().Be(WindowCommand.Close);
    }
}
