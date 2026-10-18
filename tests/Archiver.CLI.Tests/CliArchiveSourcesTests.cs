using FluentAssertions;

namespace Archiver.CLI.Tests;

/// <summary>
/// T-F414: the name a source of 'a' takes, against 7za 26.02 measured 2026-10-18 (docs/DECISIONS.md,
/// "T-F414 and T-F410, wave C"): <c>dir\sub</c> keeps both parts, an absolute path and one with a
/// <c>.</c>/<c>..</c> part keep the last, <c>.</c> and <c>dir\.</c> are the folder's contents.
/// </summary>
public sealed class CliArchiveSourcesTests : IAsyncLifetime
{
    private readonly TestTempFolder _temp = new("pakko-sources-");

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Path, "dir", "sub"));
        File.WriteAllText(Path.Combine(_temp.Path, "dir", "c.txt"), "c");
        File.WriteAllText(Path.Combine(_temp.Path, "dir", "sub", "s.txt"), "s");
        File.WriteAllText(Path.Combine(_temp.Path, "t.txt"), "t");
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _temp.DeleteAsync();

    private CliArchiveSourceList Resolve(params string[] typed) =>
        CliArchiveSources.Resolve(typed, _temp.Path, Path.Combine(_temp.Path, "o.zip"));

    private string Full(string relative) => Path.GetFullPath(Path.Combine(_temp.Path, relative));

    [Theory]
    [InlineData(@"dir\sub", @"dir\sub", 2)]
    [InlineData(@"dir\sub\s.txt", @"dir\sub\s.txt", 3)]
    [InlineData("dir/sub", @"dir\sub", 2)]
    [InlineData(@"dir\sub\", @"dir\sub", 2)]
    [InlineData(@"dir\\sub", @"dir\sub", 2)]
    [InlineData("t.txt", "t.txt", 1)]
    [InlineData(@".\dir\sub", @"dir\sub", 1)]
    [InlineData(@"dir\sub\..\c.txt", @"dir\c.txt", 1)]
    public void Resolve_RelativeName_KeepsThePartsAsTyped(string typed, string expectedPath, int expectedParts)
    {
        CliArchiveSourceList list = Resolve(typed);

        list.Problems.Should().BeEmpty();
        list.Sources.Should().Equal(new CliArchiveSource(Full(expectedPath), expectedParts));
    }

    [Fact]
    public void Resolve_AbsoluteName_KeepsTheLastPart()
    {
        CliArchiveSourceList list = Resolve(Full(@"dir\sub"));

        list.Sources.Should().Equal(new CliArchiveSource(Full(@"dir\sub"), 1));
    }

    [Fact]
    public void Resolve_NameFromAnotherFolder_ByDotDot_KeepsTheLastPart()
    {
        CliArchiveSourceList list = CliArchiveSources.Resolve([@"..\t.txt"], Full("dir"), Full("o.zip"));

        list.Sources.Should().Equal(new CliArchiveSource(Full("t.txt"), 1));
    }

    [Theory]
    [InlineData(".")]
    [InlineData(@".\")]
    public void Resolve_Dot_IsEveryChildOfTheFolder_ButTheArchive(string typed)
    {
        File.WriteAllText(Full("o.zip"), "old archive");

        CliArchiveSourceList list = Resolve(typed);

        list.Problems.Should().BeEmpty();
        list.Sources.Should().BeEquivalentTo([new CliArchiveSource(Full("dir"), 1), new CliArchiveSource(Full("t.txt"), 1)]);
    }

    [Theory]
    [InlineData(@"dir\.")]
    [InlineData(@"dir\sub\..")]
    public void Resolve_FolderEndingInDot_IsItsContents(string typed)
    {
        CliArchiveSourceList list = Resolve(typed);

        list.Sources.Should().BeEquivalentTo([new CliArchiveSource(Full(@"dir\c.txt"), 1), new CliArchiveSource(Full(@"dir\sub"), 1)]);
    }

    [Fact]
    public void Resolve_Contents_TakeHiddenFiles()
    {
        string hidden = Full(@"dir\sub\h.txt");
        File.WriteAllText(hidden, "h");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        CliArchiveSourceList list = Resolve(@"dir\sub\.");

        list.Sources.Should().BeEquivalentTo([new CliArchiveSource(hidden, 1), new CliArchiveSource(Full(@"dir\sub\s.txt"), 1)]);
    }

    [Fact]
    public void Resolve_MissingName_IsAProblem_TheRestStays()
    {
        CliArchiveSourceList list = Resolve("missing.txt", "t.txt");

        list.Sources.Should().Equal(new CliArchiveSource(Full("t.txt"), 1));
        list.Problems.Should().Equal(new CliSourceProblem("missing.txt", CliArchiveSources.NotFound));
    }

    [Fact]
    public void Resolve_DotOfAMissingFolder_IsAProblem()
    {
        CliArchiveSourceList list = Resolve(@"nope\.");

        list.Sources.Should().BeEmpty();
        list.Problems.Should().ContainSingle().Which.Name.Should().Be(@"nope\.");
    }

    [Fact]
    public void Resolve_DotOfAFile_IsAProblem()
    {
        CliArchiveSourceList list = Resolve(@"t.txt\.");

        list.Sources.Should().BeEmpty();
        list.Problems.Should().ContainSingle();
    }

    [Fact]
    public void Resolve_DriveRoot_IsPassedOnWhole()
    {
        string root = Path.GetPathRoot(_temp.Path)!;

        CliArchiveSourceList list = Resolve(root + ".");

        list.Sources.Should().Equal(new CliArchiveSource(root, 1));
    }
}
