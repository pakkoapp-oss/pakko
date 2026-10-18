using System.IO.Compression;
using FluentAssertions;

namespace Archiver.CLI.Tests.Subprocess;

/// <summary>
/// T-F411, wave E: 7-Zip's switches that Core carries out, through the real exe, against what 7za
/// 26.02 does with the same line (docs/DECISIONS.md, "T-F411, wave E").
/// </summary>
public sealed class CliCoreSwitchesSubprocessTests
{
    // --- -w ---

    [Fact]
    public void Archive_WorkingFolderThatExists_WritesTheArchive()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string workDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(scratchDir, "a", $"-w{workDir}", "out.zip", CliFixtureFiles.SourceFileA);

        exitCode.Should().Be(0, stdErr);
        File.Exists(Path.Combine(scratchDir, "out.zip")).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(workDir).Should().BeEmpty("Pakko writes its temporary archive next to the destination");
    }

    // 7za: "-wC:\nonexistent" on 'a' is exit 2 and writes nothing.
    [Fact]
    public void Archive_WorkingFolderThatIsMissing_IsExitTwoAndWritesNothing()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string missing = Path.Combine(scratchDir, "no-such-folder");

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(scratchDir, "a", $"-w{missing}", "out.zip", CliFixtureFiles.SourceFileA);

        exitCode.Should().Be(2);
        stdErr.Should().Contain("the working folder does not exist");
        Directory.EnumerateFileSystemEntries(scratchDir).Should().BeEmpty();
    }

    [Fact]
    public void Archive_BareWorkingFolder_WritesTheArchive()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(scratchDir, "a", "-w", "out.zip", CliFixtureFiles.SourceFileA);

        exitCode.Should().Be(0, stdErr);
        using ZipArchive archive = ZipFile.OpenRead(Path.Combine(scratchDir, "out.zip"));
        archive.Entries.Should().ContainSingle();
    }

    // --- -stl ---

    [Fact]
    public void Archive_ArchiveTime_IsTheSourcesNewest()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string source = Path.Combine(scratchDir, "old.txt");
        File.WriteAllText(source, "old");
        var sourceTime = new DateTime(2020, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(source, sourceTime);

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(scratchDir, "a", "-stl", "out.zip", "old.txt");

        exitCode.Should().Be(0, stdErr);
        File.GetLastWriteTimeUtc(Path.Combine(scratchDir, "out.zip")).Should().Be(sourceTime);
    }

    // --- -sse ---

    // 7za: a file without read access is exit 2 and no archive with -sse; exit 1 and the rest without.
    [Fact]
    public void Archive_LockedFileWithStopOnUnreadable_IsExitTwoAndNoArchive()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        File.WriteAllText(Path.Combine(scratchDir, "a.txt"), "a");
        string locked = Path.Combine(scratchDir, "b.txt");
        File.WriteAllText(locked, "b");

        int exitCode;
        string stdErr;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            (exitCode, _, stdErr) = CliProcessRunner.RunIn(scratchDir, "a", "-sse", "out.zip", "a.txt", "b.txt");

        exitCode.Should().Be(2);
        stdErr.Should().Contain("b.txt").And.Contain("-sse stops at a file that cannot be read");
        File.Exists(Path.Combine(scratchDir, "out.zip")).Should().BeFalse();
    }

    // --- -aot ---

    // 7za: the file already there becomes a_1.txt and the extracted one takes a.txt; Pakko numbers
    // it as -aou does ("a (1).txt").
    [Fact]
    public void Extract_RenameExisting_KeepsBothFiles()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        File.WriteAllText(Path.Combine(destDir, "a.txt"), "old");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("x", CliFixtureFiles.ValidZip, $"-o{destDir}", "-aot");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(Path.Combine(destDir, "a.txt")).Should().Be("hello world");
        File.ReadAllText(Path.Combine(destDir, "a (1).txt")).Should().Be("old");
    }

    // --- -spe ---

    // 7za: "x src.zip -o<dir>\src -spe" puts src's content straight into <dir>\src; "-o." in a folder
    // named src does not (the name is "." as typed); without -spe the root stays.
    [Theory]
    [InlineData(true, @"src", false)]
    [InlineData(true, @"src\", false)]
    [InlineData(true, @"SRC", false)]
    [InlineData(false, @"src", true)]
    public void Extract_DropRootNamedLikeOutput_PutsTheRootsContentInOutput(bool drop, string outputName, bool rootKept)
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string zip = Path.Combine(scratchDir, "any.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("src/d/b.txt").Open());
            writer.Write("b");
        }
        string output = Path.Combine(scratchDir, "out", outputName);
        string[] args = drop ? ["x", zip, $"-o{output}", "-spe"] : ["x", zip, $"-o{output}"];

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(scratchDir, args);

        exitCode.Should().Be(0, stdErr);
        string outDir = Path.Combine(scratchDir, "out", outputName.TrimEnd('\\'));
        File.Exists(Path.Combine(outDir, "src", "d", "b.txt")).Should().Be(rootKept);
        File.Exists(Path.Combine(outDir, "d", "b.txt")).Should().Be(!rootKept);
    }

    [Fact]
    public void Extract_DropRootWithDotOutput_KeepsTheRoot()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string zip = Path.Combine(scratchDir, "any.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            archive.CreateEntry("src/a.txt").Open().Dispose();
        string cwd = Path.Combine(scratchDir, "src");
        Directory.CreateDirectory(cwd);

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(cwd, "x", zip, "-o.", "-spe");

        exitCode.Should().Be(0, stdErr);
        File.Exists(Path.Combine(cwd, "src", "a.txt")).Should().BeTrue();
    }

    // --- -sdel (7za 26.02: files and folders deleted after 'a'; "s\*" leaves an empty "s") ---

    private static string CreateTree(string root, string name = "src")
    {
        string src = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(src, "d"));
        File.WriteAllText(Path.Combine(src, "a.txt"), "a");
        File.WriteAllText(Path.Combine(src, "d", "b.txt"), "b");
        return src;
    }

    private static string[] Names(string zip)
    {
        using ZipArchive archive = ZipFile.OpenRead(zip);
        return [.. archive.Entries.Select(e => e.FullName)];
    }

    [Fact]
    public void Archive_DeleteSourcesAfter_DeletesTheArchivedFolder()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        CreateTree(scratchDir);

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(scratchDir, "a", "-sdel", "out.zip", "src");

        exitCode.Should().Be(0, stdErr);
        Directory.Exists(Path.Combine(scratchDir, "src")).Should().BeFalse();
        Names(Path.Combine(scratchDir, "out.zip")).Should().Contain(["src/a.txt", "src/d/b.txt"]);
    }

    [Fact]
    public void Archive_DeleteSourcesAfterAMask_DeletesWhatItFoundAndKeepsTheFolder()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        CreateTree(scratchDir);

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(scratchDir, "a", "-sdel", "out.zip", @"src\*");

        exitCode.Should().Be(0, stdErr);
        Directory.Exists(Path.Combine(scratchDir, "src")).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(Path.Combine(scratchDir, "src")).Should().BeEmpty();
    }

    [Fact]
    public void Archive_DeleteSourcesAfterARecursiveMask_DeletesOnlyTheMatches()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string src = CreateTree(scratchDir);
        File.WriteAllText(Path.Combine(src, "d", "keep.log"), "log");

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(src, "a", "-sdel", "-r", Path.Combine(scratchDir, "out.zip"), "*.txt");

        exitCode.Should().Be(0, stdErr);
        File.Exists(Path.Combine(src, "a.txt")).Should().BeFalse();
        File.Exists(Path.Combine(src, "d", "b.txt")).Should().BeFalse();
        File.Exists(Path.Combine(src, "d", "keep.log")).Should().BeTrue();
    }

    // 7za deletes the files it stored and keeps the excluded one; Pakko keeps the whole source
    // (Core does not report which files a filter left out) and says so.
    [Fact]
    public void Archive_DeleteSourcesAfterWithAnExclude_DeletesNothingAndSaysSo()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        CreateTree(scratchDir);

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(scratchDir, "a", "-sdel", "-xr!b.txt", "out.zip", "src");

        exitCode.Should().Be(1);
        stdErr.Should().Contain("-sdel kept").And.Contain("not all of it went into the archive");
        File.Exists(Path.Combine(scratchDir, "src", "a.txt")).Should().BeTrue();
        File.Exists(Path.Combine(scratchDir, "src", "d", "b.txt")).Should().BeTrue();
    }

    // ".." names the current folder's parent's items, the current folder among them.
    [Fact]
    public void Archive_DeleteSourcesAfterAFolderHoldingTheCurrentOne_IsRefusedBeforeAnythingIsWritten()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string cwd = CreateTree(scratchDir);
        string archive = Path.Combine(CliFixtureFiles.CreateScratchDir(), "out.zip");

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(cwd, "a", "-sdel", archive, "..");

        exitCode.Should().Be(7);
        stdErr.Should().Contain("current folder");
        File.Exists(archive).Should().BeFalse();
        File.Exists(Path.Combine(cwd, "a.txt")).Should().BeTrue();
    }

    // "." is the current folder's items (7-Zip's universal item), not the folder: they go, it stays.
    [Fact]
    public void Archive_DeleteSourcesAfterDot_DeletesTheItemsAndKeepsTheCurrentFolder()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string cwd = CreateTree(scratchDir);
        string archive = Path.Combine(CliFixtureFiles.CreateScratchDir(), "out.zip");

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(cwd, "a", "-sdel", archive, ".");

        exitCode.Should().Be(0, stdErr);
        Directory.Exists(cwd).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(cwd).Should().BeEmpty();
        Names(archive).Should().Contain(["a.txt", "d/b.txt"]);
    }

    [Fact]
    public void Archive_DeleteSourcesAfterAFileThatCouldNotBeRead_DeletesNothing()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string src = CreateTree(scratchDir);

        int exitCode;
        string stdErr;
        using (new FileStream(Path.Combine(src, "a.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            (exitCode, _, stdErr) = CliProcessRunner.RunIn(scratchDir, "a", "-sdel", "out.zip", "src");

        exitCode.Should().Be(2);
        stdErr.Should().Contain("-sdel deleted nothing");
        File.Exists(Path.Combine(src, "d", "b.txt")).Should().BeTrue();
    }

    // 7za: a missing source is a warning (exit 1) and the rest is archived and deleted.
    [Fact]
    public void Archive_DeleteSourcesAfterWithAMissingSource_DeletesTheRest()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        CreateTree(scratchDir);

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(scratchDir, "a", "-sdel", "out.zip", "src", "nothere.txt");

        exitCode.Should().Be(1, stdErr);
        Directory.Exists(Path.Combine(scratchDir, "src")).Should().BeFalse();
    }

    [Fact]
    public void Archive_DeleteSourcesAfterWithAJunctionInside_LeavesTheJunctionAndItsTarget()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string src = CreateTree(scratchDir);
        string target = CreateTree(scratchDir, "target");
        string junction = Path.Combine(src, "j");
        using (var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/d", "/c", "mklink", "/J", junction, target])
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!)
        {
            mklink.WaitForExit();
            if (mklink.ExitCode != 0)
                return; // no junctions on this volume
        }
        try
        {
            (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(scratchDir, "a", "-sdel", "out.zip", "src");

            exitCode.Should().BeLessThan(2, stdErr);
            Directory.Exists(junction).Should().BeTrue();
            File.Exists(Path.Combine(target, "a.txt")).Should().BeTrue();
            File.Exists(Path.Combine(target, "d", "b.txt")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    // --- -ssw ---

    [Fact]
    public void Archive_FileHeldOpenForWriting_IsStoredOnlyWithTheSwitch()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string held = Path.Combine(scratchDir, "log.txt");
        File.WriteAllText(held, "line one");

        int withoutCode, withCode;
        string withErr;
        using (new FileStream(held, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            (withoutCode, _, _) = CliProcessRunner.RunIn(scratchDir, "a", "without.zip", "log.txt");
            (withCode, _, withErr) = CliProcessRunner.RunIn(scratchDir, "a", "-ssw", "with.zip", "log.txt");
        }

        withoutCode.Should().NotBe(0);
        File.Exists(Path.Combine(scratchDir, "without.zip")).Should().BeFalse("its only file could not be read");
        withCode.Should().Be(0, withErr);
        using ZipArchive archive = ZipFile.OpenRead(Path.Combine(scratchDir, "with.zip"));
        archive.Entries.Should().ContainSingle(e => e.FullName == "log.txt");
    }
}
