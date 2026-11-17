using Archiver.Core.IO;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.IO;

// T-F531: ZIP archives a name Windows trims by its \\?\ path, so every key built from a source path
// carries that spelling: the source key, the listing "delete the sources afterwards" works from, and
// the final path. A plain spelling of such a name still has none (it is the twin's).
public sealed class SourceTreeTrimmedNameTests : IDisposable
{
    private const string Literal = @"\\?\";
    private readonly string _root = Directory.CreateTempSubdirectory("pakko-tree-").FullName;

    public SourceTreeTrimmedNameTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "tw"));
        File.WriteAllText(Path.Combine(_root, "tw", "a.txt"), "twin");
        File.WriteAllText(Literal + Path.Combine(_root, "tw", "a.txt "), "spaced");
        Directory.CreateDirectory(Literal + Path.Combine(_root, "tw", "d."));
        File.WriteAllText(Literal + Path.Combine(_root, "tw", "d.", "in.txt"), "in dotted");
        File.WriteAllText(Literal + Path.Combine(_root, "b.txt "), "b spaced");
    }

    public void Dispose() => Directory.Delete(Literal + _root, recursive: true);

    [Fact]
    public void SourceKey_LiteralSpelling_IsItself_PlainSpellingHasNone()
    {
        string plain = Path.Combine(_root, "b.txt ");

        SourceKey.Of(Literal + plain).Should().Be(Literal + plain);
        SourceKey.Of(Literal + Path.Combine(_root, "tw", "d.") + @"\").Should().Be(Literal + Path.Combine(_root, "tw", "d."));
        SourceKey.Of(plain).Should().BeNull();
    }

    [Fact]
    public void List_FolderHoldingNamesWindowsTrims_StampsThemByTheirLiteralPaths()
    {
        string tw = Path.Combine(_root, "tw");

        SourceListing listing = SourceTree.List(tw);

        listing.Unreadable.Should().BeFalse();
        listing.Files.Select(f => f.Path).Should().BeEquivalentTo(
            Path.Combine(tw, "a.txt"), Literal + Path.Combine(tw, "a.txt "), Literal + Path.Combine(tw, "d.", "in.txt"));
        listing.Files.Single(f => f.Path.EndsWith("a.txt ", StringComparison.Ordinal)).Length.Should().Be("spaced".Length);
        listing.Folders.Should().BeEquivalentTo(tw, Literal + Path.Combine(tw, "d."));
        listing.TrimmedNames.Should().BeEquivalentTo(Literal + Path.Combine(tw, "a.txt "), Literal + Path.Combine(tw, "d."));
    }

    [Fact]
    public void List_LiteralSourceWindowsTrims_IsListedAndNamedAsTrimmed()
    {
        string file = Literal + Path.Combine(_root, "b.txt ");
        string folder = Literal + Path.Combine(_root, "tw", "d.");

        SourceListing fileListing = SourceTree.List(file);
        SourceListing folderListing = SourceTree.List(folder);

        fileListing.Files.Should().ContainSingle().Which.Path.Should().Be(file);
        fileListing.TrimmedNames.Should().Equal(file);
        folderListing.Files.Select(f => f.Path).Should().Equal(Path.Combine(folder, "in.txt"));
        folderListing.TrimmedNames.Should().Equal(folder);
        SourceTree.List(Path.Combine(_root, "b.txt ")).Exists.Should().BeFalse("the plain spelling is the twin's");
    }

    [Fact]
    public void FinalPathOfItself_LiteralNameWindowsTrims_KeepsTheLiteralSpelling()
    {
        string file = Literal + Path.Combine(_root, "b.txt ");

        PathIdentity.FinalPathOfItself(file).Should().StartWith(Literal).And.EndWith(@"\b.txt ");
        PathIdentity.Resolve(file)!.FinalPath.Should().StartWith(Literal).And.EndWith(@"\b.txt ");
        PathIdentity.FinalPathOfItself(Literal + Path.Combine(_root, "tw", "a.txt")).Should().NotStartWith(Literal, "an ordinary name drops the prefix as before");
    }

    [Theory]
    [InlineData(@"\\?\C:\w\a.txt", @"C:\w\a.txt")]
    [InlineData(@"\\?\UNC\server\share\a.txt", @"\\server\share\a.txt")]
    [InlineData(@"\\?\C:\w\a.txt ", @"\\?\C:\w\a.txt ")]
    [InlineData(@"\\?\C:\w\d.\x.txt", @"\\?\C:\w\d.\x.txt")]
    [InlineData(@"C:\w\a.txt", @"C:\w\a.txt")]
    [InlineData(@"\\?\Volume{0}\a.txt", @"\\?\Volume{0}\a.txt")]
    public void SamePlainPath_DropsThePrefixOnlyWhereThePlainPathNamesTheSameFile(string path, string expected)
    {
        InputPaths.SamePlainPath(path).Should().Be(expected);
    }
}
