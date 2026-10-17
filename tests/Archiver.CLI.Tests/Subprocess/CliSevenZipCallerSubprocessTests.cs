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
        using var archive = System.IO.Compression.ZipFile.OpenRead(Path.Combine(scratchDir, expectedName));
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

    private static string EncryptedZipPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "encrypted_aes256.zip");
}
