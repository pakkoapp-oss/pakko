using System.IO.Compression;
using FluentAssertions;

namespace Archiver.CLI.Tests.Subprocess;

/// <summary>
/// T-F410: 7-Zip's name filters through the real exe, on the archive the 7za.exe 26.02
/// measurement used (docs/DECISIONS.md's T-F410 entry). Expected sets are what 7za left on disk.
/// </summary>
public sealed class CliNameFilterSubprocessTests
{
    private static readonly string[] Entries =
    [
        "a.txt", "b.nsis", "dir/", "dir/c.txt", "dir/sub/", "dir/sub/d.nsis", "dir/sub/e.TXT", "dir/sub/y.nsis",
        "emptydir/", "Other/", "Other/dir/", "Other/dir/x.nsis", "Other/f.txt",
    ];

    // Scoop's Expand-7zipArchive: "x <arc> -o<dir> -xr!*.nsis -y".
    [Fact]
    public void ScoopLine_ExcludesEveryNsisFile()
    {
        string zip = Zip();
        string dest = Path.Combine(CliFixtureFiles.CreateScratchDir(), "out");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("x", zip, $"-o{dest}", "-xr!*.nsis", "-y");

        exitCode.Should().Be(0, stdErr);
        OnDisk(dest).Should().BeEquivalentTo(
            "a.txt", "dir", "dir/c.txt", "dir/sub", "dir/sub/e.TXT", "emptydir", "Other", "Other/dir", "Other/f.txt");
    }

    [Fact]
    public void ScoopLine_OnATar_ExcludesTheSame()
    {
        string tar = Tar();
        string dest = Path.Combine(CliFixtureFiles.CreateScratchDir(), "out");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("x", tar, $"-o{dest}", "-xr!*.nsis", "-y");

        exitCode.Should().Be(0, stdErr);
        OnDisk(dest).Should().BeEquivalentTo(
            "a.txt", "dir", "dir/c.txt", "dir/sub", "dir/sub/e.TXT", "emptydir", "Other", "Other/dir", "Other/f.txt");
    }

    // Scoop's "-ir!<dir>\*": everything under the folder (its own entry is not matched, the folder
    // is still made for what is in it).
    [Fact]
    public void ScoopIncludeLine_TakesOneFolder()
    {
        string dest = Path.Combine(CliFixtureFiles.CreateScratchDir(), "out");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("x", Zip(), $"-o{dest}", "-ir!dir\\*", "-y");

        exitCode.Should().Be(0, stdErr);
        OnDisk(dest).Should().BeEquivalentTo("dir", "dir/c.txt", "dir/sub", "dir/sub/d.nsis", "dir/sub/e.TXT", "dir/sub/y.nsis");
    }

    // Chocolatey's Get-ChocolateyUnzip with a folder: "x -aoa -bd -bb1 -o<dir> -y <file> <folder>";
    // the "- <name>" lines are its list of installed files.
    [Fact]
    public void ChocolateyLine_WithAFolder_ExtractsAndNamesOnlyThatFolder()
    {
        string dest = Path.Combine(CliFixtureFiles.CreateScratchDir(), "out");

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("x", "-aoa", "-bd", "-bb1", $"-o{dest}", "-y", Zip(), "dir");

        exitCode.Should().Be(0, stdErr);
        OnDisk(dest).Should().BeEquivalentTo("dir", "dir/c.txt", "dir/sub", "dir/sub/d.nsis", "dir/sub/e.TXT", "dir/sub/y.nsis");
        Lines(stdOut).Where(l => l.StartsWith("- ", StringComparison.Ordinal)).Select(l => l[2..].TrimEnd('\\').Replace('\\', '/'))
            .Should().BeEquivalentTo(OnDisk(dest));
    }

    [Fact]
    public void Extract_NothingMatches_SaysSoAndCreatesNothing()
    {
        string dest = Path.Combine(CliFixtureFiles.CreateScratchDir(), "out");

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("x", Zip(), $"-o{dest}", "nothing.zzz");

        exitCode.Should().Be(0, stdErr);
        stdOut.Should().Contain("No files to process");
        Directory.Exists(dest).Should().BeFalse();
    }

    [Fact]
    public void ExtractToStdout_NothingMatches_IsNoErrorAndWritesNothing()
    {
        // 7za: exit 0, no data (its -so check counts only an extraction that happened).
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("x", "-so", Zip(), "nothing.zzz");

        exitCode.Should().Be(0, stdErr);
        stdOut.Should().BeEmpty();
    }

    [Fact]
    public void ExtractToStdout_FilterLeavesOneFile_WritesIt()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("x", "-so", Zip(), "dir/c.txt");

        exitCode.Should().Be(0, stdErr);
        stdOut.Should().Be("dir/c.txt");
    }

    [Fact]
    public void List_WithAFolder_ListsOnlyWhatIsUnderIt()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("l", "-ba", Zip(), "dir/sub");

        exitCode.Should().Be(0, stdErr);
        Lines(stdOut).Should().HaveCount(4).And.OnlyContain(l => l.Contains("dir/sub"));
    }

    [Fact]
    public void Extract_ArchiveWildcard_ExtractsEveryMatch()
    {
        string folder = CliFixtureFiles.CreateScratchDir();
        File.Copy(Zip(), Path.Combine(folder, "one.zip"));
        File.Copy(CliFixtureFiles.ValidZip, Path.Combine(folder, "two.zip"));
        string dest = Path.Combine(CliFixtureFiles.CreateScratchDir(), "out");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("x", "-y", $"-o{dest}", Path.Combine(folder, "*.zip"));

        exitCode.Should().Be(0, stdErr);
        File.Exists(Path.Combine(dest, "Other", "f.txt")).Should().BeTrue("one.zip was extracted");
        File.ReadAllText(Path.Combine(dest, "a.txt")).Should().Be("hello world", "two.zip came second, -y overwrote");
    }

    [Fact]
    public void Extract_ArchiveWildcardMatchesNothing_IsACommandLineError()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("x", Path.Combine(CliFixtureFiles.CreateScratchDir(), "none*.zip"));

        exitCode.Should().Be(7);
        stdErr.Should().Contain("Cannot find archive");
    }

    [Fact]
    public void Extract_MissingListFile_IsACommandLineError()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("x", Zip(), "@" + Path.Combine(CliFixtureFiles.CreateScratchDir(), "none.txt"));

        exitCode.Should().Be(7);
        stdErr.Should().Contain("listfile");
    }

    [Fact]
    public void Test_WithANameFilter_IsRefused()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("t", Zip(), "-xr!*.nsis");

        exitCode.Should().Be(7);
        stdErr.Should().Contain("'t'");
    }

    private static string Zip()
    {
        string path = Path.Combine(CliFixtureFiles.CreateScratchDir(), "t.zip");
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (string entry in Entries)
        {
            ZipArchiveEntry created = archive.CreateEntry(entry);
            if (!entry.EndsWith('/'))
            {
                using var writer = new StreamWriter(created.Open());
                writer.Write(entry);
            }
        }
        return path;
    }

    // The same tree, archived by the real tar.exe.
    private static string Tar()
    {
        string source = CliFixtureFiles.CreateScratchDir();
        foreach (string entry in Entries)
        {
            string path = Path.Combine(source, entry);
            if (entry.EndsWith('/'))
                Directory.CreateDirectory(path);
            else
                File.WriteAllText(path, entry);
        }
        string tar = Path.Combine(CliFixtureFiles.CreateScratchDir(), "t.tar");
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            @"C:\Windows\System32\tar.exe", ["-cf", tar, "-C", source, "a.txt", "b.nsis", "dir", "emptydir", "Other"]) { UseShellExecute = false })!;
        process.WaitForExit();
        process.ExitCode.Should().Be(0);
        return tar;
    }

    private static IEnumerable<string> OnDisk(string dest) =>
        Directory.EnumerateFileSystemEntries(dest, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(dest, p).Replace('\\', '/'));

    private static string[] Lines(string text) => text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
}
