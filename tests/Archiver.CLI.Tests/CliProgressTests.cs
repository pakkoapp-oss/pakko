using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F221 item 9
public sealed class CliProgressTests
{
    [Fact]
    public void Report_RewritesThePercentInPlace_OnlyWhenItChanges()
    {
        var writer = new StringWriter();
        var progress = new CliProgress(writer);

        progress.Report(new ProgressReport { Percent = 5 });
        progress.Report(new ProgressReport { Percent = 5 });
        progress.Report(new ProgressReport { Percent = 42 });

        writer.ToString().Should().Be("\r  5%\r 42%");
    }

    [Fact]
    public void Clear_ErasesTheLine_AndIsANoOpWhenNothingWasShown()
    {
        var writer = new StringWriter();
        var progress = new CliProgress(writer);

        progress.Clear();
        writer.ToString().Should().BeEmpty();

        progress.Report(new ProgressReport { Percent = 100 });
        progress.Clear();
        writer.ToString().Should().Be("\r100%\r    \r");
    }

    [Fact]
    public void Report_ClampsOutOfRangeValues()
    {
        var writer = new StringWriter();

        new CliProgress(writer).Report(new ProgressReport { Percent = 250 });

        writer.ToString().Should().Be("\r100%");
    }
}
