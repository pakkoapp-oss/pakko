using System.Reflection;
using Archiver.OperationUi.Core;
using Archiver.OperationUi.Protocol;
using FluentAssertions;

namespace Archiver.OperationUi.Tests;

// T-F268 step 5: conflict and password prompts inside the operation window.
public sealed class OperationWindowModelPromptTests
{
    private const string Secret = "correct horse ї 🔑";

    private static readonly AskConflict Conflict = new(1, @"D:\звіт\платежі.pdf", "1,2 МБ · змінено 12.09.2026 14:03", "1,4 МБ · змінено 20.09.2026 09:41", IncomingIsNewer: true);
    private static readonly AskPassword Password = new(2, "secret.zip", 1, PreviousAttemptWasWrong: false, CanApplyToRemaining: true);

    private static OperationWindowModel Running(params ProtocolMessage[] then)
    {
        var model = new OperationWindowModel();
        model.Receive(new Hello(FrameCodec.ProtocolVersion, "uk-UA", RightToLeft: false, new Dictionary<string, string>
        {
            [WindowStrings.PasswordMessage] = "Архів «{0}» захищено паролем.",
            [WindowStrings.Newer] = "новіший",
        }));
        model.Receive(new Begin("Розпакування", ProgressKind.Bytes));
        foreach (var m in then)
            model.Receive(m);
        return model;
    }

    // --- Happy path ---

    [Fact]
    public void ConflictBeforeTheShowDelay_ShowsTheWindowAtOnce()
    {
        var model = Running();

        model.Receive(Conflict).Command.Should().Be(WindowCommand.Show);

        model.IsVisible.Should().BeTrue();
        model.Prompt.Should().Be(Conflict);
    }

    [Fact]
    public void PromptWhileTheWindowShows_BringsItForward()
    {
        var model = Running();
        model.ShowDelayElapsed();

        model.Receive(Password).Command.Should().Be(WindowCommand.Activate);
    }

    [Fact]
    public void AnswerConflict_SendsTheChoiceAndReturnsToProgress()
    {
        var model = Running(Conflict);

        var update = model.AnswerConflict(ConflictChoice.Rename, applyToAll: true);

        update.Send.Should().Equal(new ConflictAnswer(1, ConflictChoice.Rename, ApplyToAll: true));
        update.Command.Should().Be(WindowCommand.Refresh);
        model.Prompt.Should().BeNull();
        model.Phase.Should().Be(WindowPhase.Running);
    }

    [Fact]
    public void SubmitPassword_SendsItWithApplyToRemaining()
    {
        var model = Running(Password);

        var update = model.SubmitPassword(Secret, applyToRemaining: true);

        update.Send.Should().Equal(new PasswordAnswer(2, Secret, ApplyToRemaining: true));
        model.Prompt.Should().BeNull();
    }

    [Fact]
    public void DeclinePassword_SendsNoPassword()
    {
        var model = Running(Password);

        model.DeclinePassword().Send.Should().Equal(new PasswordAnswer(2, null, ApplyToRemaining: false));
    }

    // Win32 PasswordDialog.MapResult returns the edit text as typed on OK, an empty one included.
    [Fact]
    public void SubmitEmptyPassword_IsSentAsTyped()
    {
        Running(Password).SubmitPassword("", applyToRemaining: false).Send
            .Should().Equal(new PasswordAnswer(2, "", ApplyToRemaining: false));
    }

    [Fact]
    public void TwoPrompts_TheSecondShowsOnceTheFirstIsAnswered()
    {
        var model = Running(Conflict, Password);
        model.Prompt.Should().Be(Conflict);

        model.AnswerConflict(ConflictChoice.Skip, applyToAll: false).Command.Should().Be(WindowCommand.Activate);

        model.Prompt.Should().Be(Password);
    }

    [Fact]
    public void Texts_AreBuiltFromTheLabels()
    {
        var model = Running(Password);
        model.PasswordMessage.Should().Be("Архів «secret.zip» захищено паролем.");

        model.DeclinePassword();
        model.Receive(Conflict);
        model.IncomingDetails.Should().Be("1,4 МБ · змінено 20.09.2026 09:41 · новіший");
    }

    [Fact]
    public void IncomingNotNewer_HasNoMark()
    {
        var model = Running(Conflict with { IncomingIsNewer = false });

        model.IncomingDetails.Should().Be("1,4 МБ · змінено 20.09.2026 09:41");
    }

    // --- Security & boundary ---

    [Fact]
    public void ThePassword_IsKeptNowhereInTheModelOrTheUpdate()
    {
        var model = Running(Password);

        var update = model.SubmitPassword(Secret, applyToRemaining: false);

        update.ToString().Should().NotContain(Secret);
        update.Send.Single().ToString().Should().NotContain(Secret);
        foreach (PropertyInfo property in typeof(OperationWindowModel).GetProperties())
            (property.GetValue(model)?.ToString() ?? "").Should().NotContain(Secret, property.Name);
        foreach (FieldInfo field in typeof(OperationWindowModel).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            (field.GetValue(model)?.ToString() ?? "").Should().NotContain(Secret, field.Name);
    }

    [Fact]
    public void ApplyToRemaining_IsNeverSentWhenItWasNotOffered()
    {
        var model = Running(Password with { CanApplyToRemaining = false });

        model.SubmitPassword(Secret, applyToRemaining: true).Send
            .Should().Equal(new PasswordAnswer(2, Secret, ApplyToRemaining: false));
    }

    // --- Misuse ---

    [Fact]
    public void EscapeOnAConflict_SkipsWithTheCheckboxAsTicked()
    {
        // ShellConflictDialog.MapResult: IDCANCEL is Skip with the verification checkbox's state.
        Running(Conflict).Escape(applyToAllChecked: true).Send
            .Should().Equal(new ConflictAnswer(1, ConflictChoice.Skip, ApplyToAll: true));
    }

    [Fact]
    public void EscapeOnAPassword_DeclinesIt()
    {
        Running(Password).Escape(applyToAllChecked: true).Send
            .Should().Equal(new PasswordAnswer(2, null, ApplyToRemaining: false));
    }

    [Fact]
    public void EscapeWithoutAPrompt_CancelsTheOperation()
    {
        Running().Escape(applyToAllChecked: false).Send.Should().Equal(new CancelRequested(), new WindowClosed());
    }

    [Fact]
    public void CloseDuringAPrompt_CancelsTheOperationWithoutAnswering()
    {
        var model = Running(Conflict);

        var update = model.UserClosed();

        update.Command.Should().Be(WindowCommand.Close);
        update.Send.Should().Equal(new CancelRequested(), new WindowClosed());
    }

    [Fact]
    public void AnAnswerOfTheWrongKind_IsIgnored()
    {
        var model = Running(Password);

        model.AnswerConflict(ConflictChoice.Overwrite, applyToAll: true).Should().Be(WindowUpdate.Nothing);
        model.Prompt.Should().Be(Password);
        Running(Conflict).SubmitPassword(Secret, applyToRemaining: false).Should().Be(WindowUpdate.Nothing);
        Running().DeclinePassword().Should().Be(WindowUpdate.Nothing);
    }

    [Fact]
    public void PromptBeforeBeginOrAfterTheResult_IsIgnored()
    {
        var model = new OperationWindowModel();
        model.Receive(Conflict).Should().Be(WindowUpdate.Nothing);
        model.Prompt.Should().BeNull();

        var done = Running(new Complete(new ResultText(ResultSeverity.Warning, "t", "x")));
        done.Receive(Password).Should().Be(WindowUpdate.Nothing);
        done.Prompt.Should().BeNull();
    }

    // --- Error path ---

    [Fact]
    public void ResultWhileAPromptIsOpen_DropsThePrompt()
    {
        var model = Running(Conflict);

        model.Receive(new Complete(new ResultText(ResultSeverity.Error, "t", "x")));

        model.Prompt.Should().BeNull();
        model.Phase.Should().Be(WindowPhase.Result);
    }
}
