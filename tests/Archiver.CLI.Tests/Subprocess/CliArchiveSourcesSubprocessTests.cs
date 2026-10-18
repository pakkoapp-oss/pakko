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
