using Archiver.Shell;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F406: an exception that escapes an Explorer command used to end the process with nothing on
// screen; now the user is told the operation failed and the exit code is 2.
public sealed class ShellLastResortTests
{
    [Fact]
    public async Task ACommandThatThrows_ShowsAnErrorAndExitsTwo()
    {
        var ui = new FakeOperationUi();

        int exitCode = await ShellLastResort.RunAsync(() => throw new InvalidOperationException("boom"), ui);

        exitCode.Should().Be(2);
        OperationMessage message = ui.Messages.Should().ContainSingle().Subject;
        message.Severity.Should().Be(MessageSeverity.Error);
        message.Text.Should().StartWith(ResultMessagesLocalizer.Get("ResultOperationFailed"))
            .And.Contain("InvalidOperationException: boom");
    }

    [Fact]
    public async Task ACommandThatCompletes_ShowsNothingAndExitsZero()
    {
        var ui = new FakeOperationUi();

        (await ShellLastResort.RunAsync(() => Task.CompletedTask, ui)).Should().Be(0);
        ui.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_IsNotTurnedIntoAnError()
    {
        Func<Task> act = () => ShellLastResort.RunAsync(() => throw new OperationCanceledException(), new FakeOperationUi());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
