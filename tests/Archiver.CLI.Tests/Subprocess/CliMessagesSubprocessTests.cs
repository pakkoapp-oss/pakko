using System.IO.Compression;
using FluentAssertions;

namespace Archiver.CLI.Tests.Subprocess;

// T-F221: what pakko says when something is wrong, and that an archive gets exactly the name typed.
public sealed class CliMessagesSubprocessTests
{
    private static string EncryptedZipPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "encrypted_aes256.zip");

    [Theory]
    [InlineData("x")]
    [InlineData("t")]
    [InlineData("l")]
    public void MissingArchive_SaysItDoesNotExist(string command)
    {
        string missing = Path.Combine(CliFixtureFiles.CreateScratchDir(), "nosuch.zip");

        (int exitCode, _, string stdErr) = command == "x"
            ? CliProcessRunner.Run("x", $"-o{CliFixtureFiles.CreateScratchDir()}", missing)
            : CliProcessRunner.Run(command, missing);

        exitCode.Should().Be(2);
        stdErr.Should().Contain("does not exist").And.NotContain("recognized archive format").And.NotContain("Could not find file");
    }

    [Fact]
    public void Hash_MissingFile_SaysItDoesNotExist()
    {
        string missing = Path.Combine(CliFixtureFiles.CreateScratchDir(), "nope.bin");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("h", missing);

        exitCode.Should().Be(2);
        stdErr.Should().Contain("does not exist").And.NotContain("Could not find file");
    }

    [Fact]
    public void Test_EncryptedWithoutPassword_HintsAtMinusP()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("t", EncryptedZipPath);

        exitCode.Should().Be(2);
        stdErr.Should().Contain("password-protected").And.Contain("-p<password>");
    }

    [Fact]
    public void Test_EncryptedWithWrongPassword_SaysOnlyThatThePasswordIsWrong()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("t", "-pWrongPassword", EncryptedZipPath);

        exitCode.Should().Be(2);
        stdErr.Should().Contain("incorrect password").And.NotContain("password-protected and cannot");
    }

    [Fact]
    public void Extract_OntoExistingFilesWhenPiped_HintsAtOverwriteSwitches()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        CliProcessRunner.Run("x", $"-o{destDir}", CliFixtureFiles.ValidZip).ExitCode.Should().Be(0);

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("x", $"-o{destDir}", CliFixtureFiles.ValidZip);

        exitCode.Should().Be(1);
        stdErr.Should().Contain("-aoa");
    }

    [Theory]
    [InlineData("l")]
    [InlineData("t")]
    [InlineData("x")]
    public void EmptyStdin_SaysSoWithoutTheStagingPath(string command)
    {
        string[] args = command == "x" ? ["x", "-si", $"-o{CliFixtureFiles.CreateScratchDir()}"] : [command, "-si"];

        (int exitCode, _, string stdErr) = CliProcessRunner.RunWithBinaryStdio([], args);

        exitCode.Should().Be(2);
        stdErr.Should().Contain("stdin was empty").And.NotContain("stdin.bin").And.NotContain("Archiver.CLI.Stdin");
    }

    [Theory]
    [InlineData("l")]
    [InlineData("t")]
    public void NotAnArchiveOnStdin_NamesStdinNotTheStagingFile(string command)
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.RunWithBinaryStdio("just some text"u8.ToArray(), command, "-si");

        exitCode.Should().Be(2);
        stdErr.Should().Contain("(stdin)").And.NotContain("stdin.bin").And.NotContain("Central Directory");
    }

    [Theory]
    [InlineData("-ttar", "out.gz", "out.gz")]
    [InlineData("-ttar", "out", "out.tar")]
    [InlineData("-tzip", "backup.out", "backup.out")]
    [InlineData("-tzip", "plain", "plain.zip")]
    public void Archive_ExplicitName_IsWrittenThe7ZipWay(string type, string name, string expectedFile)
    {
        string dir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", type, Path.Combine(dir, name), CliFixtureFiles.SourceFileA);

        exitCode.Should().Be(0, stdErr);
        Directory.GetFiles(dir).Select(Path.GetFileName).Should().Equal(expectedFile);
    }

    [Fact]
    public void Hash_Folder_PrintsPathsRelativeToTheFoldersParent()
    {
        string root = CliFixtureFiles.CreateScratchDir();
        string folder = Path.Combine(root, "docs");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(Path.Combine(folder, "sub", "a.txt"), "a");

        (int exitCode, string stdOut, _) = CliProcessRunner.Run("h", folder);

        exitCode.Should().Be(0);
        stdOut.Should().Contain(Path.Combine("docs", "sub", "a.txt")).And.NotContain(root);
    }
}
