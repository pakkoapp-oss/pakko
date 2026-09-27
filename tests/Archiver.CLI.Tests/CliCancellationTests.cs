using Archiver.CLI;
using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F244 item 4: the first Ctrl+C stops the command cleanly; a second one ends the process.
public sealed class CliCancellationTests
{
    [Fact]
    public void FirstInterrupt_CancelsTokenAndKeepsProcessAlive()
    {
        using CliCancellation cancellation = CliCancellation.Detached();

        bool keepRunning = cancellation.HandleInterrupt();

        keepRunning.Should().BeTrue();
        cancellation.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void SecondInterrupt_LetsTheProcessEnd()
    {
        using CliCancellation cancellation = CliCancellation.Detached();
        cancellation.HandleInterrupt();

        cancellation.HandleInterrupt().Should().BeFalse();
    }

    [Fact]
    public void InterruptAfterQuitAtPrompt_LetsTheProcessEnd()
    {
        using CliCancellation cancellation = CliCancellation.Detached();
        cancellation.Source.Cancel();

        cancellation.HandleInterrupt().Should().BeFalse();
    }
}
