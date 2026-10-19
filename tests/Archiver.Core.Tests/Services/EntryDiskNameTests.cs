using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F420: how an entry name lands on disk, for Pakko and for tar.exe, and the bookkeeping both
// engines share when two entries meet on one path.
public sealed class EntryDiskNameTests
{
    [Theory]
    [InlineData("a.txt", "a.txt")]
    [InlineData("a.txt.", "a.txt")]
    [InlineData("a.txt .", "a.txt")]
    [InlineData("d./x.txt", "d/x.txt")]
    [InlineData("d /x.txt ", "d/x.txt")]
    [InlineData("./d/x.txt", "d/x.txt")]
    public void AsPakkoWrites_DropsEveryPartsTrailingDotsAndSpaces(string name, string expected)
    {
        EntryDiskName.AsPakkoWrites(name).Should().Be(expected);
    }

    // libarchive creates a folder through GetFullPathNameW and opens a file through \\?\ (measured).
    [Theory]
    [InlineData("a.txt.", "a.txt.")]
    [InlineData("d./x.txt", "d/x.txt")]
    [InlineData("d./x.txt ", "d/x.txt ")]
    [InlineData("./a.txt", "a.txt")]
    public void AsTarExeWrites_TrimsFoldersOnly(string name, string expected)
    {
        EntryDiskName.AsTarExeWrites(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("a.txt", "A.TXT", true)]
    [InlineData("./a.txt", "a.txt", true)]
    [InlineData("a.txt", "a.txt.", false)]
    [InlineData("d/x.txt", "d./x.txt", false)]
    public void SameName_OnlyCaseAndALeadingDotSlash(string first, string second, bool same)
    {
        EntryDiskName.SameName(first, second).Should().Be(same);
    }

    [Theory]
    [InlineData("a.", true)]
    [InlineData("d /x", true)]
    [InlineData("./a", false)]
    [InlineData("a.b", false)]
    public void IsTrimmed_APartEndingInADotOrASpace(string name, bool trimmed)
    {
        EntryDiskName.IsTrimmed(name).Should().Be(trimmed);
    }

    [Fact]
    public void Registry_SameNameTwice_IsReplacedInRun_NotKeptApart()
    {
        var replaced = new List<string>();
        var names = new ExtractionNameRegistry(replaced);

        names.Claim(@"C:\o\a.txt", namedDifferently: false, "a.txt");
        bool keepApart = names.MustKeepApart(@"C:\o\a.txt", namedDifferently: false);
        names.Claim(@"C:\o\a.txt", namedDifferently: false, "a.txt");

        keepApart.Should().BeFalse("T-F30: the conflict rule decides");
        replaced.Should().Equal("a.txt");
    }

    [Fact]
    public void Registry_ATwinOnEitherSide_IsKeptApart()
    {
        var names = new ExtractionNameRegistry(null);
        names.Claim(@"C:\o\a.txt", namedDifferently: true, "a.txt.");

        names.MustKeepApart(@"C:\o\a.txt", namedDifferently: false).Should().BeTrue("the earlier one came under another name");
        names.MustKeepApart(@"C:\o\b.txt", namedDifferently: true).Should().BeFalse("nothing is there yet");
    }

    [Fact]
    public void Registry_TwinKnownBeforehand_IsKeptApartOnceClaimed()
    {
        var names = new ExtractionNameRegistry(null);
        names.KeepApart(@"C:\o\d\x.txt");

        names.MustKeepApart(@"C:\o\d\x.txt", namedDifferently: false).Should().BeFalse("not written yet");
        names.Claim(@"C:\o\d\x.txt", namedDifferently: false, @"d\x.txt");
        names.MustKeepApart(@"C:\o\d\x.txt", namedDifferently: false).Should().BeTrue();
    }
}
