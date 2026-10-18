using System.Diagnostics;
using System.IO.Compression;
using FluentAssertions;

namespace Archiver.CLI.Tests.Subprocess;

/// <summary>
/// T-F414: 'a' through the real exe, against 7za 26.02 (docs/DECISIONS.md, "T-F414 and T-F410, wave
/// C"): a relative source is stored as typed, a missing one is a warning (exit 1) beside the rest,
/// and with nothing left there is no archive (exit 2).
/// </summary>
public sealed class CliArchiveSourcesSubprocessTests
{
    private static string MakeTree()
    {
        string root = CliFixtureFiles.CreateScratchDir();
        Directory.CreateDirectory(Path.Combine(root, "dir", "sub"));
        File.WriteAllText(Path.Combine(root, "dir", "sub", "s.txt"), "s");
        File.WriteAllText(Path.Combine(root, "t.txt"), "t");
        return root;
    }

    private static string[] ZipNames(string zipPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        return [.. archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void Archive_RelativeFolder_IsStoredUnderThePathAsTyped()
    {
        string root = MakeTree();
        string zip = Path.Combine(CliFixtureFiles.CreateScratchDir(), "o.zip");

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(root, "a", zip, @"dir\sub");

        exitCode.Should().Be(0, stdErr);
        ZipNames(zip).Should().Equal("dir/sub/s.txt");
    }

    [Fact]
    public void Archive_AbsoluteFolder_IsStoredUnderItsOwnName()
    {
        string root = MakeTree();
        string zip = Path.Combine(CliFixtureFiles.CreateScratchDir(), "o.zip");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", zip, Path.Combine(root, "dir", "sub"));

        exitCode.Should().Be(0, stdErr);
        ZipNames(zip).Should().Equal("sub/s.txt");
    }

    [RequiresTarExe]
    public void Archive_RelativeFolderToTar_IsStoredUnderThePathAsTyped()
    {
        string root = MakeTree();
        string tar = Path.Combine(CliFixtureFiles.CreateScratchDir(), "o.tar");

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(root, "a", "-ttar", tar, @"dir\sub");

        exitCode.Should().Be(0, stdErr);
        TarNames(tar).Should().Contain("dir/sub/s.txt").And.OnlyContain(name => name.StartsWith("dir/sub", StringComparison.Ordinal));
    }

    [Fact]
    public void Archive_OneSourceMissing_WarnsExitsOne_AndArchivesTheRest()
    {
        string root = MakeTree();
        string zip = Path.Combine(CliFixtureFiles.CreateScratchDir(), "o.zip");

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(root, "a", zip, "missing.txt", "t.txt");

        exitCode.Should().Be(1, stdErr);
        stdErr.Should().Contain("pakko: warning: missing.txt:").And.NotContain("error");
        ZipNames(zip).Should().Equal("t.txt");
    }

    [Fact]
    public void Archive_EverySourceMissing_ExitsTwo_NoArchive()
    {
        string root = MakeTree();
        string zip = Path.Combine(CliFixtureFiles.CreateScratchDir(), "o.zip");

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(root, "a", zip, "missing.txt");

        exitCode.Should().Be(2, stdErr);
        stdErr.Should().Contain("pakko: warning: missing.txt:").And.Contain("nothing to archive");
        File.Exists(zip).Should().BeFalse();
    }

    // The case Core refuses: two sources from different folders, one landing inside the other.
    [Fact]
    public void Archive_AbsoluteFolderAndARelativeFileUnderTheSameName_ExitsTwo()
    {
        string root = MakeTree();
        string other = Path.Combine(CliFixtureFiles.CreateScratchDir(), "dir");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "o.txt"), "o");
        string zip = Path.Combine(CliFixtureFiles.CreateScratchDir(), "o.zip");

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(root, "a", zip, other, @"dir\sub\s.txt");

        exitCode.Should().Be(2, stdErr);
        stdErr.Should().Contain("would be stored inside");
        File.Exists(zip).Should().BeFalse();
    }

    // T-F410: 'a' with name filters, each line against 7za 26.02's own result for the same tree.
    private static string MakeFilterTree()
    {
        string root = MakeTree();
        File.WriteAllText(Path.Combine(root, "dir", "c.txt"), "c");
        File.WriteAllText(Path.Combine(root, "dir", "c.log"), "l");
        return root;
    }

    [Theory]
    [InlineData(new[] { "dir", "-xr!*.log" }, new[] { "dir/c.txt", "dir/sub/s.txt" })]
    [InlineData(new[] { "dir", "-xr!sub" }, new[] { "dir/c.log", "dir/c.txt" })]
    [InlineData(new[] { "dir", @"-x!dir\sub" }, new[] { "dir/c.log", "dir/c.txt" })]
    [InlineData(new[] { "dir", "-x!c.txt" }, new[] { "dir/c.log", "dir/c.txt", "dir/sub/s.txt" })]
    [InlineData(new[] { "*.txt", "-r" }, new[] { "dir/c.txt", "dir/sub/s.txt", "t.txt" })]
    [InlineData(new[] { "c*.txt", "-r", @"-x!dir\c.txt" }, new string[0])]
    [InlineData(new[] { @"dir\*.txt" }, new[] { "dir/c.txt" })]
    [InlineData(new[] { @".\dir\*.txt" }, new[] { "c.txt" })]
    [InlineData(new[] { "t.txt", @"-i!dir\*.log" }, new[] { "dir/c.log", "t.txt" })]
    [InlineData(new[] { "dir", "*.txt", "-r" }, new[] { "dir/c.log", "dir/c.txt", "dir/sub/s.txt", "t.txt" })]
    public void Archive_WithNameFilters_StoresWhat7ZipStores(string[] arguments, string[] expected)
    {
        string root = MakeFilterTree();
        string zip = Path.Combine(CliFixtureFiles.CreateScratchDir(), "o.zip");

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(root, ["a", zip, .. arguments]);

        if (expected.Length == 0)
        {
            exitCode.Should().Be(2, stdErr);
            File.Exists(zip).Should().BeFalse();
            return;
        }
        exitCode.Should().Be(0, stdErr);
        ZipNames(zip).Should().Equal(expected);
    }

    [Fact]
    public void Archive_ExcludeLeavingOutEverything_ExitsTwo_NoArchive()
    {
        string root = MakeFilterTree();
        string zip = Path.Combine(CliFixtureFiles.CreateScratchDir(), "n.zip");

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(root, "a", zip, "-xr!*", "dir");

        exitCode.Should().Be(2, stdErr);
        stdErr.Should().Contain("nothing to archive");
        File.Exists(zip).Should().BeFalse();
    }

    // The mask never takes the archive being written, also when the name gets its extension.
    [Fact]
    public void Archive_MaskBesideTheArchive_DoesNotTakeIt()
    {
        string root = MakeFilterTree();
        File.WriteAllText(Path.Combine(root, "out.zip"), "an old archive");

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(root, "a", "-y", "out", "*");

        exitCode.Should().Be(0, stdErr);
        ZipNames(Path.Combine(root, "out.zip")).Should().NotContain("out.zip").And.Contain("t.txt");
    }

    [Fact]
    public void Archive_AbsoluteExclude_LeavesNothingOut_As7Zip()
    {
        string root = MakeFilterTree();
        string zip = Path.Combine(CliFixtureFiles.CreateScratchDir(), "o.zip");

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(root, "a", zip, "dir", "-x!" + Path.Combine(root, "dir", "c.txt"));

        exitCode.Should().Be(0, stdErr);
        ZipNames(zip).Should().Contain("dir/c.txt");
    }

    [Fact]
    public void Archive_ListFileOfSources_IsRead()
    {
        string root = MakeFilterTree();
        File.WriteAllLines(Path.Combine(root, "lst.txt"), [Path.Combine("dir", "sub"), "t.txt"]);
        string zip = Path.Combine(CliFixtureFiles.CreateScratchDir(), "o.zip");

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(root, "a", zip, "@lst.txt");

        exitCode.Should().Be(0, stdErr);
        ZipNames(zip).Should().Equal("dir/sub/s.txt", "t.txt");
    }

    [Fact]
    public void Archive_MissingListFile_ExitsSeven()
    {
        string root = MakeFilterTree();
        string zip = Path.Combine(CliFixtureFiles.CreateScratchDir(), "o.zip");

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(root, "a", zip, "@nope.txt");

        exitCode.Should().Be(7, stdErr);
        File.Exists(zip).Should().BeFalse();
    }

    [RequiresTarExe]
    public void Archive_TarWithAnExclude_LeavesItOut()
    {
        string root = MakeFilterTree();
        string tar = Path.Combine(CliFixtureFiles.CreateScratchDir(), "o.tar");

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(root, "a", "-ttar", tar, "dir", "-xr!*.log");

        exitCode.Should().Be(0, stdErr);
        TarNames(tar).Should().Contain("dir/c.txt").And.NotContain(name => name.EndsWith(".log", StringComparison.Ordinal));
    }

    private static string[] TarNames(string tarPath)
    {
        var info = new ProcessStartInfo(@"C:\Windows\System32\tar.exe") { RedirectStandardOutput = true, UseShellExecute = false };
        info.ArgumentList.Add("-tf");
        info.ArgumentList.Add(tarPath);
        using Process tar = Process.Start(info)!;
        string output = tar.StandardOutput.ReadToEnd();
        tar.WaitForExit();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
