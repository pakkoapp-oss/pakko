using Archiver.OperationUi.Core;
using Archiver.OperationUi.Protocol;
using FluentAssertions;

namespace Archiver.OperationUi.Tests;

// T-F217: a yes/no question inside the operation window (extract a suspected compression bomb).
// Declining is the safe answer: Esc, a cancel and a second question never confirm.
public sealed class OperationWindowModelConfirmTests
{
    private static readonly AskConfirm Bomb = new(3, "Підозрілий архів", "Цей архів заявляє про 5 ГБ даних. Видобути попри це?", "Видобути", "Пропустити");

    private static OperationWindowModel Running(params ProtocolMessage[] then)
    {
        var model = new OperationWindowModel();
        model.Receive(new Hello(FrameCodec.ProtocolVersion, "uk-UA", RightToLeft: false, new Dictionary<string, string>()));
        model.Receive(new Begin("Розпакування", ProgressKind.Bytes));
        foreach (ProtocolMessage m in then)
            model.Receive(m);
        return model;
    }

    [Fact]
    public void AskConfirm_IsShownAsThePrompt()
    {
        OperationWindowModel model = Running();

        model.Receive(Bomb).Command.Should().Be(WindowCommand.Show);

        model.Prompt.Should().Be(Bomb);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnswerConfirm_SendsTheAnswerAndReturnsToProgress(bool confirmed)
    {
        OperationWindowModel model = Running(Bomb);

        WindowUpdate update = model.AnswerConfirm(confirmed);

        update.Send.Should().Equal(new ConfirmAnswer(3, confirmed));
        model.Prompt.Should().BeNull();
        model.Phase.Should().Be(WindowPhase.Running);
    }

    [Fact]
    public void Escape_Declines()
    {
        OperationWindowModel model = Running(Bomb);

        model.Escape(applyToAllChecked: true).Send.Should().Equal(new ConfirmAnswer(3, false));
    }

    [Fact]
    public void AnswerConfirm_WithoutAQuestion_DoesNothing()
    {
        OperationWindowModel model = Running();

        model.AnswerConfirm(true).Should().Be(WindowUpdate.Nothing);
    }

    [Fact]
    public void AskConfirm_BeforeBegin_IsIgnored()
    {
        var model = new OperationWindowModel();

        model.Receive(Bomb).Should().Be(WindowUpdate.Nothing);
        model.Prompt.Should().BeNull();
    }
}
