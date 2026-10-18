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
}
