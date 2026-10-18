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

    [Fact]
    public void NothingPassedIn_ACleanResultWithNoEntryThrough_IsTrue() =>
        new CliFilterCount().NothingPassedIn(new Archiver.Core.Models.ArchiveResult()).Should().BeTrue();

    [Fact]
    public void NothingPassedIn_AnEntryThrough_IsFalse()
    {
        var count = new CliFilterCount();
        count.Counting((_, _) => true)("a.txt", false);

        count.NothingPassedIn(new Archiver.Core.Models.ArchiveResult()).Should().BeFalse();
    }

    // T-F410 review: an archive skipped by policy or tar.exe's capabilities never met the filter.
    [Fact]
    public void NothingPassedIn_ASkippedArchive_IsFalse() =>
        new CliFilterCount().NothingPassedIn(new Archiver.Core.Models.ArchiveResult
        {
            SkippedFiles = [new Archiver.Core.Models.SkippedFile { Path = "a.rar", Reason = "refused" }],
        }).Should().BeFalse();

    [Fact]
    public void NothingPassedIn_AFailedArchive_IsFalse() =>
        new CliFilterCount().NothingPassedIn(new Archiver.Core.Models.ArchiveResult
        {
            Errors = [new Archiver.Core.Models.ArchiveError { SourcePath = "a.zip", Message = "damaged" }],
        }).Should().BeFalse();
}
