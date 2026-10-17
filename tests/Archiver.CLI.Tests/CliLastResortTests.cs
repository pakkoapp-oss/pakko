using Archiver.CLI;
using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F406: what escapes a pakko run ends as 7-Zip ends it — the error on stderr and exit code 2 —
// not as the runtime's unhandled-exception crash.
public sealed class CliLastResortTests
{
    [Fact]
    public async Task ARunThatThrows_ExitsTwoWithTheErrorOnStderr()
    {
        var stderr = new StringWriter();

        int exitCode = await CliLastResort.RunAsync(() => throw new InvalidOperationException("boom"), stderr);

        exitCode.Should().Be(2);
        stderr.ToString().Should().StartWith("pakko: error: Unexpected error: InvalidOperationException: boom");
        stderr.ToString().Should().Contain(nameof(ARunThatThrows_ExitsTwoWithTheErrorOnStderr), "the stack trace is kept for the bug report");
    }

    [Fact]
    public async Task ARunThatThrowsAfterAwaiting_ExitsTwo()
    {
        var stderr = new StringWriter();

        int exitCode = await CliLastResort.RunAsync(async () =>
        {
            await Task.Yield();
            throw new IOException("disk gone");
        }, stderr);

        exitCode.Should().Be(2);
        stderr.ToString().Should().Contain("IOException: disk gone");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(255)]
    public async Task ARunThatReturns_KeepsItsExitCodeAndWritesNothing(int code)
    {
        var stderr = new StringWriter();

        (await CliLastResort.RunAsync(() => Task.FromResult(code), stderr)).Should().Be(code);
        stderr.ToString().Should().BeEmpty();
    }

    // Cancellation keeps its own path: the commands map a Ctrl+C to 255 themselves.
    [Fact]
    public async Task Cancellation_IsNotTurnedIntoAnError()
    {
        Func<Task> act = () => CliLastResort.RunAsync(() => throw new OperationCanceledException(), new StringWriter());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
