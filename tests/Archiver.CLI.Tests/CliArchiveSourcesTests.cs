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

    // T-F512 (c): a name Windows trims goes to Core as typed, not as its full path (the twin "t.txt").
    [Theory]
    [InlineData("t.txt.")]
    [InlineData("t.txt ")]
    [InlineData(@"d.\t.txt")]
    public void Resolve_NameWindowsTrims_IsKeptAsTyped(string typed)
    {
        CliArchiveSourceList list = CliArchiveSources.Resolve([typed], _temp.Path, Full("o.zip"));

        list.Sources.Should().Equal(new CliArchiveSource(Path.Combine(_temp.Path, typed), 1));
        list.Problems.Should().BeEmpty("Core refuses it and says why");
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

    // T-F410: masks, searched on disk. 7za 26.02's answers for the same tree (step 0 of wave C).
    private CliArchiveSourceList Search(IReadOnlyList<CliNameMask> masks, params string[] excludes) =>
        CliArchiveSources.Resolve(
            masks,
            excludes.Length == 0 ? null : CliNameFilter.Create(excludes.Select(x => new CliNameMask(x, Include: false, CliRecursion.None)), startFromAll: true),
            caseSensitive: false, _temp.Path, Full("o.zip"));

    private static CliNameMask Mask(string path, CliRecursion recursion = CliRecursion.None) => new(path, Include: true, recursion);

    private void AddLog() => File.WriteAllText(Full(@"dir\c.log"), "l");

    [Fact]
    public void Search_WildcardUnderAFolder_KeepsTheFolderInTheName()
    {
        AddLog();

        CliArchiveSourceList list = Search([Mask(@"dir\*.txt")]);

        list.Sources.Should().Equal(new CliArchiveSource(Full(@"dir\c.txt"), 2));
    }

    [Fact]
    public void Search_DotPrefix_DropsTheFolderFromTheName()
    {
        CliArchiveSourceList list = Search([Mask(@".\dir\*.txt")]);

        list.Sources.Should().Equal(new CliArchiveSource(Full(@"dir\c.txt"), 1));
    }

    [Fact]
    public void Search_AbsoluteMask_NamesFromItsFirstWildcard()
    {
        CliArchiveSourceList list = Search([Mask(Full(@"*\*.txt"))]);

        list.Sources.Should().Equal(new CliArchiveSource(Full(@"dir\c.txt"), 2));
    }

    [Fact]
    public void Search_RecursiveMask_FindsEveryDepth()
    {
        CliArchiveSourceList list = Search([Mask("*.txt", CliRecursion.Recursive)]);

        list.Sources.Should().BeEquivalentTo([
            new CliArchiveSource(Full(@"dir\c.txt"), 2),
            new CliArchiveSource(Full(@"dir\sub\s.txt"), 3),
            new CliArchiveSource(Full("t.txt"), 1),
        ]);
    }

    [Fact]
    public void Search_RecursiveFolderName_TakesTheFolderWhole()
    {
        Directory.CreateDirectory(Full(@"dir2\sub"));

        CliArchiveSourceList list = Search([Mask("sub", CliRecursion.Recursive)]);

        list.Sources.Should().BeEquivalentTo([new CliArchiveSource(Full(@"dir\sub"), 2), new CliArchiveSource(Full(@"dir2\sub"), 2)]);
    }

    [Fact]
    public void Search_WildcardFolder_IsTakenWhole_NotEntered()
    {
        CliArchiveSourceList list = Search([Mask("d*")]);

        list.Sources.Should().Equal(new CliArchiveSource(Full("dir"), 1));
    }

    // 7za: "a o.zip dir *.txt -r" takes each item once.
    [Fact]
    public void Search_FolderAndARecursiveMaskInsideIt_TakeEachItemOnce()
    {
        CliArchiveSourceList list = Search([Mask("dir"), Mask("*.txt", CliRecursion.Recursive)]);

        list.Sources.Should().BeEquivalentTo([new CliArchiveSource(Full("dir"), 1), new CliArchiveSource(Full("t.txt"), 1)]);
    }

    [Fact]
    public void Search_FolderAndItsSubfolder_TakeTheFolderOnce()
    {
        CliArchiveSourceList list = Search([Mask(@"dir\sub"), Mask("dir")]);

        list.Sources.Should().Equal(new CliArchiveSource(Full("dir"), 1));
    }

    [Fact]
    public void Search_ExcludedItem_IsNotTaken_NorEntered()
    {
        AddLog();

        CliArchiveSourceList list = Search([Mask("*", CliRecursion.Recursive)], "dir");

        list.Sources.Should().Equal(new CliArchiveSource(Full("t.txt"), 1));
    }

    [Fact]
    public void Search_WildcardMatchingNothing_IsNoProblem()
    {
        CliArchiveSourceList list = Search([Mask("*.xyz")]);

        list.Sources.Should().BeEmpty();
        list.Problems.Should().BeEmpty();
    }

    [Fact]
    public void Search_UnderAMissingFolder_IsAProblem()
    {
        CliArchiveSourceList list = Search([Mask(@"nope\*.txt")]);

        list.Problems.Should().ContainSingle().Which.Name.Should().Be(@"nope\*.txt");
    }

    [Fact]
    public void Search_WildcardMatchingOff_LooksForTheNameAsTyped()
    {
        CliArchiveSourceList list = CliArchiveSources.Resolve(
            [new CliNameMask(@"dir\*", Include: true, CliRecursion.None, WildcardMatching: false)], null, false, _temp.Path, Full("o.zip"));

        list.Sources.Should().BeEmpty();
        list.Problems.Should().ContainSingle();
    }

    [Fact]
    public void Search_CaseSensitive_TakesOnlyTheSameCase()
    {
        CliArchiveSourceList list = CliArchiveSources.Resolve([Mask("*.TXT")], null, caseSensitive: true, _temp.Path, Full("o.zip"));

        list.Sources.Should().BeEmpty();
    }

    [Fact]
    public void Search_NeverTakesTheArchiveBeingWritten()
    {
        File.WriteAllText(Full("o.zip"), "old");

        CliArchiveSourceList list = Search([Mask("*.zip")]);

        list.Sources.Should().BeEmpty();
    }

    // T-F511 PR 3: the archive being written is judged by what it is (PathIdentity), not by its
    // spelling. Given as \\?\, the old archive was taken as a source of itself.
    [Fact]
    public void Search_TheArchiveSpelledAnotherWay_IsNotTaken()
    {
        File.WriteAllText(Full("o.zip"), "old");

        CliArchiveSourceList list = CliArchiveSources.Resolve(
            [Mask("*.zip")], null, caseSensitive: false, _temp.Path, @"\\?\" + Full("o.zip"));

        list.Sources.Should().BeEmpty();
    }

    // T-F511 PR 3: in a case-sensitive folder X.ZIP and x.zip are two files; writing X.ZIP left x.zip
    // out, as a name compared without case.
    [RequiresCaseSensitiveFolderFact]
    public void Search_CaseSensitiveFolder_AnotherFileInAnotherCase_IsTaken()
    {
        string folder = Full("cs");
        Directory.CreateDirectory(folder);
        CaseSensitiveFolder.Enable(folder).Should().BeTrue();
        File.WriteAllText(Path.Combine(folder, "x.zip"), "another file");

        CliArchiveSourceList list = CliArchiveSources.Resolve(
            [Mask(@"cs\*")], null, caseSensitive: false, _temp.Path, Path.Combine(folder, "X.ZIP"));

        list.Sources.Select(s => s.FullPath).Should().Equal(Path.Combine(folder, "x.zip"));
    }

    // T-F511 PR 3: A.TXT and a.txt in a case-sensitive folder are two sources.
    [RequiresCaseSensitiveFolderFact]
    public void Search_CaseSensitiveFolder_BothSpellingsAreTaken()
    {
        string folder = Full("cs");
        Directory.CreateDirectory(folder);
        CaseSensitiveFolder.Enable(folder).Should().BeTrue();
        File.WriteAllText(Path.Combine(folder, "A.TXT"), "UPPER");
        File.WriteAllText(Path.Combine(folder, "a.txt"), "lower");

        CliArchiveSourceList list = Search([Mask(@"cs\*")]);

        list.Sources.Select(s => s.FullPath).Should().BeEquivalentTo([Path.Combine(folder, "A.TXT"), Path.Combine(folder, "a.txt")]);
    }

    // T-F511 PR 3 (pins, never red): a folder "d." beside its twin "d". DirectoryWalker does not
    // enter it; before, listing "d." listed "d" under d's own names, and the duplicate went.
    [Fact]
    public void Search_FolderWithANameWindowsTrims_TwinsFileTakenOnce()
    {
        Directory.CreateDirectory(Full("d"));
        File.WriteAllText(Full(@"d\f.txt"), "twin");
        Directory.CreateDirectory(@"\\?\" + Path.Combine(_temp.Path, "d."));
        try
        {
            CliArchiveSourceList list = Search([Mask("*.txt", CliRecursion.Recursive)]);

            list.Sources.Select(s => s.FullPath).Where(path => path.EndsWith("f.txt", StringComparison.Ordinal))
                .Should().Equal(Full(@"d\f.txt"));
        }
        finally
        {
            Directory.Delete(@"\\?\" + Path.Combine(_temp.Path, "d."));
        }
    }

    [Fact]
    public void Search_DoesNotEnterALinkedFolder()
    {
        string target = Path.Combine(Path.GetTempPath(), "pakko-link-target-" + Path.GetRandomFileName());
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "outside.txt"), "o");
        try
        {
            Directory.CreateSymbolicLink(Full("linked"), target);
        }
        catch (IOException)
        {
            Directory.Delete(target, recursive: true);
            return; // creating a link needs Developer Mode or elevation: nothing to check here
        }
        try
        {
            CliArchiveSourceList list = Search([Mask("*.txt", CliRecursion.Recursive)]);

            list.Sources.Should().NotContain(source => source.FullPath.Contains("outside", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(Full("linked"));
            Directory.Delete(target, recursive: true);
        }
    }

    // Wave C's closing review, each against 7za 26.02.
    private CliArchiveSourceList ResolveExcluding(string exclude, params CliNameMask[] masks) =>
        CliArchiveSources.Resolve(
            masks, CliNameFilter.Create([new CliNameMask(exclude, Include: false, CliRecursion.None)], startFromAll: true),
            caseSensitive: false, _temp.Path, Full("o.zip"));

    [Fact]
    public void Review_ANamedFileUnderAnExcludedFolder_StaysASource()
    {
        CliArchiveSourceList list = ResolveExcluding("dir", Mask("dir"), Mask(Full("dir" + Path.DirectorySeparatorChar + "c.txt")));

        list.Sources.Should().Equal(new CliArchiveSource(Full("dir" + Path.DirectorySeparatorChar + "c.txt"), 1));
    }

    [Fact]
    public void Review_ANamedFileTheExcludeTakesOutOfItsFolder_StaysASource()
    {
        string file = Full("dir" + Path.DirectorySeparatorChar + "c.txt");

        CliArchiveSourceList list = ResolveExcluding("dir/c.txt", Mask("dir"), Mask(file));

        list.Sources.Should().BeEquivalentTo([new CliArchiveSource(Full("dir"), 1), new CliArchiveSource(file, 1)]);
    }

    [Fact]
    public void Review_ExcludedSource_IsDropped()
    {
        CliArchiveSourceList list = ResolveExcluding("dir", Mask("dir"));

        list.Sources.Should().BeEmpty();
    }

    [Fact]
    public void Review_DotWithWildcardMatchingOff_IsStillTheContents()
    {
        CliArchiveSourceList list = CliArchiveSources.Resolve(
            [new CliNameMask(".", Include: true, CliRecursion.None, WildcardMatching: false)], null, false, _temp.Path, Full("o.zip"));

        list.Sources.Should().BeEquivalentTo([new CliArchiveSource(Full("dir"), 1), new CliArchiveSource(Full("t.txt"), 1)]);
    }

    [Fact]
    public void Review_FileNamedWithATrailingSeparator_IsAProblem()
    {
        CliArchiveSourceList list = Resolve("t.txt" + Path.DirectorySeparatorChar);

        list.Sources.Should().BeEmpty();
        list.Problems.Should().Equal(new CliSourceProblem("t.txt" + Path.DirectorySeparatorChar, CliArchiveSources.NotAFolder));
    }

    [Fact]
    public void Review_MaskInADriveRoot_LeavesOutTheSystemsOwnEntries()
    {
        const FileAttributes OsOwned = FileAttributes.Hidden | FileAttributes.System;
        string root = Path.GetPathRoot(_temp.Path)!;
        // One listing: a file there can come and go (DumpStack.log.tmp), so nothing is asked twice.
        string[] osOwned = [.. new DirectoryInfo(root).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 })
            .Where(entry => (entry.Attributes & OsOwned) == OsOwned).Select(entry => entry.FullName)];

        CliArchiveSourceList list = Search([Mask(root + "*")]);

        list.Sources.Select(source => source.FullPath).Should().NotIntersectWith(osOwned);
    }

    [Fact]
    public void Resolve_DriveRoot_IsPassedOnWhole()
    {
        string root = Path.GetPathRoot(_temp.Path)!;

        CliArchiveSourceList list = Resolve(root + ".");

        list.Sources.Should().Equal(new CliArchiveSource(root, 1));
    }
}
