using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F410: "No files to process" depends on what the filter let through, not on what arrived.
public sealed class CliFilterCountTests
{
    [Fact]
    public void Counting_CountsOnlyTheEntriesThatPass()
    {
        var count = new CliFilterCount();
        Func<string, bool, bool> filter = count.Counting((path, _) => path.EndsWith(".txt", StringComparison.Ordinal));

        filter("a.txt", false).Should().BeTrue();
        filter("b.nsis", false).Should().BeFalse();
        filter("c.txt", false).Should().BeTrue();

        count.Count.Should().Be(2);
    }
}
