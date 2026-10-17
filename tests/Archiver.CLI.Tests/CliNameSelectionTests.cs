using System.Text;
using Archiver.CLI.Tests.Subprocess;
using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F410: the archives on disk (7-Zip's arcCensor) and the filter for the entries inside them,
// with list files read as 7-Zip's ReadNamesFromListFile2 reads them.
public sealed class CliNameSelectionTests
{
    private readonly string _dir = CliFixtureFiles.CreateScratchDir();

    // --- list files ---

    [Fact]
    public void ReadListFile_TrimsLinesStripsQuotesAndSkipsTheBomAndEmptyLines()
    {
        string list = Write("list.txt", [0xEF, 0xBB, 0xBF, .. "\"dir\\sub\"\r\n\r\n  a.txt  \r\nlast"u8]);

        CliNameSelection.ReadListFile(list, null).Should().Equal("dir\\sub", "a.txt", "last");
    }

    [Fact]
    public void ReadListFile_NotUtf8_IsRefused()
    {
        string list = Write("bad.txt", [(byte)'a', 0xFF, (byte)'\n']);

        CliNameSelection.ReadListFile(list, null).Should().BeNull();
    }

    [Fact]
    public void ReadListFile_Utf16_ReadsWithScs()
    {
        string list = Write("utf16.txt", Encoding.Unicode.GetBytes("a.txt\r\nb.txt"));

        CliNameSelection.ReadListFile(list, "UTF-16LE").Should().Equal("a.txt", "b.txt");
    }

    [Fact]
    public void ReadListFile_Missing_IsRefused() =>
        CliNameSelection.ReadListFile(Path.Combine(_dir, "none.txt"), null).Should().BeNull();

    // --- the entry filter ---

    [Fact]
    public void BuildEntryFilter_NothingFilters_IsNull()
    {
        (CliNameFilter? filter, string? error) = CliNameSelection.BuildEntryFilter(CliArgumentParser.Parse(["x", "a.zip"]));

        error.Should().BeNull();
        filter.Should().BeNull();
    }

    [Fact]
    public void BuildEntryFilter_SwitchRecursionOverridesTheGlobalOne()
    {
        // -r- globally, but -xr! recurses its own name.
        CliNameFilter filter = Filter("x", "-r-", "a.zip", "-xr!*.nsis");

        filter.Includes("dir/sub/d.nsis", isFolder: false).Should().BeFalse();
        filter.Includes("dir/c.txt", isFolder: false).Should().BeTrue();
    }

    [Fact]
    public void BuildEntryFilter_GlobalRecursion_AppliesToASwitchThatDoesNotSay()
    {
        CliNameFilter filter = Filter("x", "-r", "a.zip", "-x!*.nsis");

        filter.Includes("dir/sub/d.nsis", isFolder: false).Should().BeFalse();
    }

    [Fact]
    public void BuildEntryFilter_ListFileAfterTheArchive_IncludesItsNames()
    {
        Write("names.txt", "dir\r\n"u8.ToArray());
        CliNameFilter filter = Filter("x", "a.zip", "@" + Path.Combine(_dir, "names.txt"));

        filter.Includes("dir/c.txt", isFolder: false).Should().BeTrue();
        filter.Includes("a.txt", isFolder: false).Should().BeFalse();
    }

    [Fact]
    public void BuildEntryFilter_MissingListFile_IsAnError()
    {
        (_, string? error) = CliNameSelection.BuildEntryFilter(CliArgumentParser.Parse(["x", "a.zip", "@" + Path.Combine(_dir, "none.txt")]));

        error.Should().Contain("listfile");
    }

    // --- the archives ---

    [Fact]
    public void ResolveArchives_NoWildcard_KeepsTheNamesAsTyped()
    {
        (IReadOnlyList<string> paths, string? error) = Resolve("x", "missing.zip", "-ai!other.zip");

        error.Should().BeNull();
        paths.Should().Equal("missing.zip", "other.zip");
    }

    [Fact]
    public void ResolveArchives_Wildcard_FindsTheFilesInThatFolderOnly()
    {
        Archives();

        (IReadOnlyList<string> paths, _) = Resolve("x", "arcs\\*.zip");

        paths.Select(Path.GetFileName).Should().Equal("one.zip", "two.zip");
    }

    [Fact]
    public void ResolveArchives_RecursiveInclude_GoesIntoSubfolders_AndExcludeRemoves()
    {
        Archives();

        (IReadOnlyList<string> paths, _) = Resolve("x", "-an", "-air!arcs\\*.zip", "-ax!arcs\\two.zip");

        paths.Select(Path.GetFileName).Should().Equal("three.zip", "one.zip");
    }

    [Fact]
    public void ResolveArchives_RecursiveWalk_DoesNotFollowAJunctionLoop()
    {
        Archives();
        // A junction back to its own parent: following it would find every archive again, and again.
        string loop = Path.Combine(_dir, "arcs", "deep", "loop");
        using (var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/d", "/c", "mklink", "/J", loop, Path.Combine(_dir, "arcs")])
            { UseShellExecute = false, RedirectStandardOutput = true })!)
        {
            mklink.WaitForExit();
            mklink.ExitCode.Should().Be(0);
        }

        (IReadOnlyList<string> paths, _) = Resolve("x", "-an", "-air!arcs\\*.zip");

        paths.Select(Path.GetFileName).Should().Equal("three.zip", "one.zip", "two.zip");
    }

    [Fact]
    public void ResolveArchives_WildcardMatchesNothing_CannotFindArchive()
    {
        (IReadOnlyList<string> paths, string? error) = Resolve("x", "none*.zip");

        paths.Should().BeEmpty();
        error.Should().Be("Cannot find archive");
    }

    [Fact]
    public void ResolveArchives_Stdin_HasNoArchiveName() =>
        Resolve("x", "-si", "dir").Paths.Should().BeEmpty();

    private void Archives()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "arcs", "deep"));
        foreach (string name in new[] { "arcs\\one.zip", "arcs\\two.zip", "arcs\\deep\\three.zip", "arcs\\note.txt" })
            File.WriteAllText(Path.Combine(_dir, name), "x");
    }

    private (IReadOnlyList<string> Paths, string? Error) Resolve(params string[] args) =>
        CliNameSelection.ResolveArchives(CliArgumentParser.Parse(args), _dir);

    private static CliNameFilter Filter(params string[] args)
    {
        ParsedCliCommand command = CliArgumentParser.Parse(args);
        command.Type.Should().NotBe(CliCommandType.Invalid, command.ErrorMessage);
        (CliNameFilter? filter, string? error) = CliNameSelection.BuildEntryFilter(command);
        error.Should().BeNull();
        return filter!;
    }

    private string Write(string name, byte[] bytes)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
