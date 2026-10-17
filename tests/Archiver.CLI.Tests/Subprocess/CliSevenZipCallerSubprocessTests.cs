using System.IO.Compression;
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

    // T-F409: patool's own list and test lines.
    [Theory]
    [InlineData("l")]
    [InlineData("t")]
    public void PatoolLine_OnAPlainZip_ExitsZero(string command)
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run(command, "-y", "-p-", "--", CliFixtureFiles.ValidZip);

        exitCode.Should().Be(0, stdErr);
    }

    [Fact]
    public void List_EncryptedZipWithAPassword_ListsTheNames()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("l", "-ptestpassword", EncryptedZipPath);

        exitCode.Should().Be(0, stdErr);
        stdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCountGreaterThan(1, "the header and at least one entry");
    }

    // patool's "-p-" is the password "-" (as in 7-Zip): on an encrypted ZIP it is a wrong password.
    [Fact]
    public void Test_PatoolLineOnAnEncryptedZip_IsAWrongPassword()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("t", "-y", "-p-", "--", EncryptedZipPath);

        exitCode.Should().Be(2);
        stdErr.Should().Contain("incorrect password");
    }

    // T-F411: -sae writes the name exactly as typed; without it a name with no extension gets the type's.
    [Theory]
    [InlineData("-sae", "noext")]
    [InlineData("-sas", "noext.zip")]
    public void Archive_NameMode_DecidesTheFileName(string nameMode, string expectedName)
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(scratchDir, "a", "-tzip", nameMode, "noext", CliFixtureFiles.SourceFileA);

        exitCode.Should().Be(0, stdErr);
        Directory.GetFiles(scratchDir).Select(Path.GetFileName).Should().Equal(expectedName);
        using ZipArchive archive = ZipFile.OpenRead(Path.Combine(scratchDir, expectedName));
        archive.Entries.Should().ContainSingle();
    }

    [SevenZipWriterFact]
    public void Archive_PatoolSevenZipLine_WritesThe7z()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(scratchDir, "a", "-y", "-t7z", "-sae", "--", "out.7z", CliFixtureFiles.SourceFileA);

        exitCode.Should().Be(0, stdErr);
        File.ReadAllBytes(Path.Combine(scratchDir, "out.7z")).AsSpan(0, 2).ToArray().Should().Equal((byte)'7', (byte)'z');
    }

    // --- T-F408: output switches ---

    [Fact]
    public void List_MessagesOff_WritesNothingToStdOut()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("l", "-bso0", CliFixtureFiles.ValidZip);

        exitCode.Should().Be(0, stdErr);
        stdOut.Should().BeEmpty();
    }

    [Fact]
    public void List_MessagesToStdErr_MovesTheListing()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("l", "-bso2", CliFixtureFiles.ValidZip);

        exitCode.Should().Be(0, stdErr);
        stdOut.Should().BeEmpty();
        stdErr.Should().Contain("a.txt");
    }

    [Theory]
    [InlineData("-bse0", false, false)]
    [InlineData("-bse1", true, false)]
    [InlineData("-bse2", false, true)]
    public void Extract_ErrorStream_DecidesWhereTheErrorGoes(string token, bool onStdOut, bool onStdErr)
    {
        string missing = Path.Combine(CliFixtureFiles.CreateScratchDir(), "missing.zip");

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("x", token, $"-o{CliFixtureFiles.CreateScratchDir()}", missing);

        exitCode.Should().Be(2, "the exit code is the same whatever stream the message goes to");
        stdOut.Contains("pakko: error:").Should().Be(onStdOut, stdOut);
        stdErr.Contains("pakko: error:").Should().Be(onStdErr, stdErr);
    }

    [Fact]
    public void List_NoHeaders_PrintsEntryRowsOnly()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("l", "-ba", CliFixtureFiles.ValidZip, CliFixtureFiles.ValidZip);

        exitCode.Should().Be(0, stdErr);
        string[] lines = stdOut.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        lines.Should().NotBeEmpty().And.OnlyContain(line => !line.StartsWith('#') && !line.StartsWith("Size\t", StringComparison.Ordinal));
    }

    [Fact]
    public void Test_ShowTime_PrintsSevenZipsTimeLines()
    {
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("t", "-bt", CliFixtureFiles.ValidZip);

        exitCode.Should().Be(0, stdErr);
        stdOut.Should().MatchRegex(@"Kernel  Time = +\d+\.\d{3} = +\d+%")
            .And.MatchRegex(@"Global  Time = +\d+\.\d{3} = +\d+%");
    }

    // 7-Zip has no stdout messages under -so (StdOutMode): the data comes out byte for byte.
    [Theory]
    [InlineData("-bt")]
    [InlineData("-bb1")]
    [InlineData("-bb3")]
    public void Extract_ToStdOut_WithOutputSwitches_GivesTheSameBytes(string token)
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string zip = Path.Combine(scratchDir, "one.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            archive.CreateEntryFromFile(CliFixtureFiles.SourceFileA, "one.txt");

        (int exitCode, byte[] withSwitch, string stdErr) = CliProcessRunner.RunWithBinaryStdio([], "x", "-so", token, zip);

        exitCode.Should().Be(0, stdErr);
        withSwitch.Should().Equal(File.ReadAllBytes(CliFixtureFiles.SourceFileA));
    }

    private static string EncryptedZipPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "encrypted_aes256.zip");
}
