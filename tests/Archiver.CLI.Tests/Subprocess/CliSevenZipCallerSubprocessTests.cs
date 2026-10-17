using FluentAssertions;

namespace Archiver.CLI.Tests.Subprocess;

/// <summary>
/// T-F407, T-F409, T-F408, T-F411's -sae: the command lines real callers of 7z.exe pass (patool's
/// p7zip.py, Chocolatey's Get-ChocolateyUnzip; docs/CLI.md, "Real callers"), through the real exe.
/// </summary>
public sealed class CliSevenZipCallerSubprocessTests
{
    [Fact]
    public void Extract_ArchiveNamedWithADashAfterDoubleDash_IsExtracted()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        File.Copy(CliFixtureFiles.ValidZip, Path.Combine(scratchDir, "-dash.zip"));
        string destDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(scratchDir, "x", "-y", $"-o{destDir}", "--", "-dash.zip");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(Path.Combine(destDir, "a.txt")).Should().Be("hello world");
    }
}
