using Archiver.Core.Services;
using Archiver.Core.Services.Sandbox;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F410: which list a filtered tar test hands tar.exe. libarchive matches every entry against
// every pattern, so the shorter side goes: "-T" with the files that passed or "-X" with the rest.
public sealed class TarTestSubsetTests
{
    private static readonly TarCatalog Catalog = TarCatalogs.Of(
        ("./", 0), ("./a.txt", 1), ("./dir/", 0), ("./dir/b.txt", 10), ("./c.txt", 100), ("./d.txt", 1000));

    [Fact]
    public void Of_FewerPass_ListsThePassedFilesForMinusT()
    {
        var subset = TarExeReader.TestSubset.Of(Catalog, (name, _) => name == "c.txt");

        subset.Members.Should().Equal("./c.txt");
        subset.ExcludedFiles.Should().BeNull();
        subset.DeclaredSize.Should().Be(100);
        subset.NothingToRead.Should().BeFalse();
    }

    [Fact]
    public void Of_FewerLeftOut_ListsTheLeftOutFilesForMinusX()
    {
        var subset = TarExeReader.TestSubset.Of(Catalog, (name, _) => name != "c.txt");

        subset.Members.Should().BeNull();
        subset.ExcludedFiles.Should().Equal("./c.txt");
        subset.DeclaredSize.Should().Be(1011);
    }

    [Fact]
    public void Of_EveryFilePasses_NoList()
    {
        var subset = TarExeReader.TestSubset.Of(Catalog, (_, isFolder) => !isFolder);

        subset.Members.Should().BeNull();
        subset.ExcludedFiles.Should().BeNull();
        subset.DeclaredSize.Should().Be(1111);
    }

    [Fact]
    public void Of_OnlyFoldersPass_NothingToRead()
    {
        var subset = TarExeReader.TestSubset.Of(Catalog, (_, isFolder) => isFolder);

        subset.NothingToRead.Should().BeTrue();
    }

    // T-F545: a declared size is the archive's to state (a sparse entry's can be any long); the sum
    // wrapped past long.MaxValue to a negative total, and LINQ's checked Sum threw out of Core.
    private static readonly TarCatalog Huge = new(long.MaxValue, ["a.txt", "b.txt", "c.txt"], [long.MaxValue, long.MaxValue, 1]);

    [Fact]
    public void Of_SizesPastTheLargestLong_SaturateThere()
    {
        TarExeReader.TestSubset.Of(Huge, (name, _) => name != "c.txt").DeclaredSize.Should().Be(long.MaxValue);
    }

    [Fact]
    public void DeclaredSizeOf_SizesPastTheLargestLong_SaturateThere()
    {
        TarExeReader.DeclaredSizeOf(Huge, ["a.txt", "b.txt"]).Should().Be(long.MaxValue);
    }

    // T-F561: a run's write limit is what tar.exe takes for its members: libarchive ignores a leading
    // "./" on both sides, and a member takes everything below it. Missing either refused a sound archive.
    private static readonly TarCatalog Spelled = new(0, ["./a.txt", "a.txt", "y", "y/z.txt", "other.txt"], [5_000_000, 10, 1, 2_000_000, 7]);

    [Fact]
    public void DeclaredSizeOf_CountsBothSpellingsOfAMember()
    {
        TarExeReader.DeclaredSizeOf(Spelled, ["a.txt"]).Should().Be(5_000_010);
    }

    [Fact]
    public void DeclaredSizeOf_CountsWhatIsBelowAMember()
    {
        TarExeReader.DeclaredSizeOf(Spelled, ["y"]).Should().Be(2_000_001);
    }

    [Fact]
    public void Of_APassedFileWithEntriesBelowItsName_DeclaresThemToo()
    {
        var subset = TarExeReader.TestSubset.Of(Spelled, (name, _) => name == "y");

        subset.Members.Should().Equal("y");
        subset.DeclaredSize.Should().Be(2_000_001);
    }

    // T-F554: a line whose columns cannot be trusted has no size, and the listing is refused - never a
    // size of 0, which let a bomb pass T-F94's check. Under --numeric-owner the owner and group are
    // digits on every line tar.exe 3.8.9 and 3.5.2 print (every format both read, measured); a name
    // there means the option did not take, and the column a size is read from may be another's.
    [Theory]
    [InlineData("-rw-r--r--", "0", "0", "0", "-5")]
    [InlineData("-rw-r--r--", "0", "0", "0", "big")]
    [InlineData("-rw-r--r--", "0", "user", "0", "5")]
    [InlineData("-rw-r--r--", "0", "0", "users", "5")]
    [InlineData("-rw-r--r--", "0", "my", "owner", "0", "1048576", "Jan", "1", "2026", "bomb.bin")]
    [InlineData("-rw-r--r--", "0", "0", "0")]
    public void ListingSize_ColumnsThatCannotBeTrusted_IsNoSize(params string[] fields)
    {
        TarSandboxedService.ParseTarListingSize(fields).Should().BeNull();
    }

    [Fact]
    public void ListingSize_NumericOwnerAndGroup_IsTheSizeColumn()
    {
        TarSandboxedService.ParseTarListingSize(["-rw-r--r--", "0", "1000", "1000", "5000", "Nov", "18", "2026", "a.txt"]).Should().Be(5000);
    }

    [Fact]
    public void Of_IsNotAskedForTheArchiveRoot()
    {
        var asked = new List<string>();

        TarExeReader.TestSubset.Of(Catalog, (name, _) =>
        {
            asked.Add(name);
            return true;
        });

        asked.Should().Equal("a.txt", "dir/", "dir/b.txt", "c.txt", "d.txt");
    }
}
