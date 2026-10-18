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
