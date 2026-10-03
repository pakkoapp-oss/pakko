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

    // T-F293: a CRC failure is not a conflict, so the overwrite switches would not help.
    [Fact]
    public void Extract_CrcFailure_NoOverwriteHint()
    {
        string dir = CliFixtureFiles.CreateScratchDir();
        string zipPath = Path.Combine(dir, "bad.zip");
        using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("doc.txt", CompressionLevel.NoCompression).Open()))
            writer.Write("plain stored content");
        byte[] bytes = File.ReadAllBytes(zipPath);
        int data = bytes.AsSpan().IndexOf("plain stored content"u8);
        bytes[data] ^= 0xFF;
        File.WriteAllBytes(zipPath, bytes);

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("x", $"-o{Path.Combine(dir, "out")}", zipPath);

        exitCode.Should().Be(2, stdErr);
        stdErr.Should().Contain("CRC").And.NotContain("-aoa");
    }

    [RequiresTarExe]
    public void Extract_TarOntoExistingFilesWhenPiped_HintsAtOverwriteSwitches()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        CliProcessRunner.Run("x", $"-o{destDir}", CliFixtureFiles.ValidTarGz!).ExitCode.Should().Be(0);

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("x", $"-o{destDir}", CliFixtureFiles.ValidTarGz!);

        exitCode.Should().Be(1, stdErr);
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
    [InlineData("-ttar", ".gz", ".gz")]       // T-F294: a leading dot is an extension too, as in 7-Zip
    [InlineData("-tzip", "backup.out", "backup.out")]
    [InlineData("-tzip", "plain", "plain.zip")]
    public void Archive_ExplicitName_IsWrittenThe7ZipWay(string type, string name, string expectedFile)
    {
        string dir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", type, Path.Combine(dir, name), CliFixtureFiles.SourceFileA);

        exitCode.Should().Be(0, stdErr);
        Directory.GetFiles(dir).Select(Path.GetFileName).Should().Equal(expectedFile);
    }

    // T-F294/T-F296: the name says tar.gz but no -t was given — a ZIP under that name is refused.
    [Fact]
    public void Archive_TarNameWithoutType_IsACommandLineErrorAndWritesNothing()
    {
        string dir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", Path.Combine(dir, "out.tar.gz"), CliFixtureFiles.SourceFileA);

        exitCode.Should().Be(7);
        stdErr.Should().Contain("-ttar.gz");
        Directory.GetFiles(dir).Should().BeEmpty();
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
