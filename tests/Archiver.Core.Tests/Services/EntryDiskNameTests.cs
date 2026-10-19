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
    [InlineData("a//b/./c.txt", "a/b/c.txt")]
    [InlineData("What?.txt", "What_.txt")]
    [InlineData("d|e/x<y>.txt", "d_e/x_y_.txt")]
    [InlineData("a\"b*c.txt", "a_b_c.txt")]
    [InlineData("a/.../b.txt", "a/_/b.txt")]
    [InlineData("a/ /b.txt", "a/_/b.txt")]
    [InlineData("d./", "d")]
    public void AsPakkoWrites_ReplacesRefusedCharactersAndDropsTrailingDotsAndSpaces(string name, string expected)
    {
        EntryDiskName.AsPakkoWrites(name).Should().Be(expected);
    }

    // libarchive creates a folder through GetFullPathNameW and opens a file through \\?\ (measured).
    [Theory]
    [InlineData("a.txt.", "a.txt.")]
    [InlineData("d./x.txt", "d/x.txt")]
    [InlineData("d./x.txt ", "d/x.txt ")]
    [InlineData("./a.txt", "a.txt")]
    [InlineData("a?b.txt", "a_b.txt")]
    [InlineData("d?./x|.txt.", "d_/x_.txt.")]
    [InlineData("d./", "d")]
    public void AsTarExeWrites_ReplacesCharactersAndTrimsFoldersOnly(string name, string expected)
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
    [InlineData("a?b", true)]
    [InlineData("./a", false)]
    [InlineData("a//b", false)]
    [InlineData("a.b", false)]
    public void Renames_OnlyWhatWindowsWritesAsAnotherName(string name, bool renames)
    {
        EntryDiskName.Renames(name).Should().Be(renames);
    }

    // What Pakko writes is final on Windows: GetFullPath, which a path goes through on its way to
    // the file system, changes nothing more. (Windows alone would keep "d " - a middle part loses
    // only its dots - which Explorer cannot open; Pakko drops the space too, T-F420.)
    [Theory]
    [InlineData("a.txt.")]
    [InlineData("d. /x.txt .")]
    [InlineData("d ./x.txt")]
    [InlineData("a//b/./c.txt")]
    [InlineData("a/.../b?.txt")]
    public void AsPakkoWrites_IsLeftAsItIsByGetFullPath(string name)
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "oracle")) + Path.DirectorySeparatorChar;
        string written = EntryDiskName.AsPakkoWrites(name).Replace('/', Path.DirectorySeparatorChar);

        Path.GetFullPath(Path.Combine(root, written)).Should().Be(root + written);
    }

    [Fact]
    public void Registry_SameNameTwice_TheArchiveIsNotWhollyOnDisk_NotKeptApart()
    {
        var names = new ExtractionNameRegistry { CurrentArchive = @"C:\a.zip" };

        names.Claim(@"C:\o\a.txt", namedDifferently: false);
        bool keepApart = names.MustKeepApart(@"C:\o\a.txt", namedDifferently: false);
        names.Claim(@"C:\o\a.txt", namedDifferently: false);

        keepApart.Should().BeFalse("T-F30: the conflict rule decides");
        names.NotWhollyOnDisk(@"C:\a.zip").Should().BeTrue();
    }

    // x *.zip into one folder: the later archive's file replaced the earlier archive's.
    [Fact]
    public void Registry_AcrossArchives_TheEarlierArchiveLostItsFile()
    {
        var names = new ExtractionNameRegistry { CurrentArchive = @"C:\a1.zip" };
        names.Claim(@"C:\o\a.txt", namedDifferently: false);
        names.CurrentArchive = @"C:\a2.tar";

        bool renamedAsInThisArchive = names.ClaimedInThisArchive.Contains(@"C:\o\a.txt");
        names.Claim(@"C:\o\a.txt", namedDifferently: false);

        renamedAsInThisArchive.Should().BeFalse("-aot renames the earlier archive's file, which is on disk");
        names.NotWhollyOnDisk(@"C:\a1.zip").Should().BeTrue();
        names.NotWhollyOnDisk(@"C:\a2.tar").Should().BeFalse();
    }

    [Fact]
    public void Registry_ATwinOnEitherSide_IsKeptApart()
    {
        var names = new ExtractionNameRegistry();
        names.Claim(@"C:\o\a.txt", namedDifferently: true);

        names.MustKeepApart(@"C:\o\a.txt", namedDifferently: false).Should().BeTrue("the earlier one came under another name");
        names.MustKeepApart(@"C:\o\b.txt", namedDifferently: true).Should().BeFalse("nothing is there yet");
    }

    [Fact]
    public void Registry_TwinKnownBeforehand_IsKeptApartOnceClaimed()
    {
        var names = new ExtractionNameRegistry();
        names.KeepApart(@"C:\o\d\x.txt");

        names.MustKeepApart(@"C:\o\d\x.txt", namedDifferently: false).Should().BeFalse("not written yet");
        names.Claim(@"C:\o\d\x.txt", namedDifferently: false);
        names.MustKeepApart(@"C:\o\d\x.txt", namedDifferently: false).Should().BeTrue();
    }
}
