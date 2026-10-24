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
    [InlineData("-sae", "noext", "noext")]
    [InlineData("-sas", "noext", "noext.zip")]
    [InlineData("-saa", "noext", "noext.zip")]
    [InlineData("-saa", "named.zip", "named.zip.zip")]
    [InlineData("-sas", "named.zip", "named.zip")]
    public void Archive_NameMode_DecidesTheFileName(string nameMode, string typedName, string expectedName)
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(scratchDir, "a", "-tzip", nameMode, typedName, CliFixtureFiles.SourceFileA);

        exitCode.Should().Be(0, stdErr);
        Directory.GetFiles(scratchDir).Select(Path.GetFileName).Should().Equal(expectedName);
        using ZipArchive archive = ZipFile.OpenRead(Path.Combine(scratchDir, expectedName));
        archive.Entries.Should().ContainSingle();
    }

    // T-F411: 7-Zip's spellings (measured on 7za 26.02): a switch before the command, both in capitals.
    [Fact]
    public void Extract_SwitchesAndCommandInCapitals_Extracts()
    {
        string destDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("-Y", "X", CliFixtureFiles.ValidZip, $"-O{destDir}");

        exitCode.Should().Be(0, stdErr);
        File.ReadAllText(Path.Combine(destDir, "a.txt")).Should().Be("hello world");
    }

    [Fact]
    public void Extract_OutputGivenTwice_IsExitSevenAndWritesNothing()
    {
        string first = CliFixtureFiles.CreateScratchDir();
        string second = CliFixtureFiles.CreateScratchDir();

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("x", CliFixtureFiles.ValidZip, $"-o{first}", $"-o{second}", "-y");

        exitCode.Should().Be(7);
        stdErr.Should().Contain("Multiple instances for switch");
        Directory.EnumerateFileSystemEntries(first).Concat(Directory.EnumerateFileSystemEntries(second)).Should().BeEmpty();
    }

    [Fact]
    public void Archive_TarWithSaa_AddsTheExtensionAgain()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, _, string stdErr) = CliProcessRunner.RunIn(scratchDir, "a", "-TTAR", "-saa", "n.tar", CliFixtureFiles.SourceFileA);

        exitCode.Should().Be(0, stdErr);
        Directory.GetFiles(scratchDir).Select(Path.GetFileName).Should().Equal("n.tar.tar");
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
        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("l", "-ba", CliFixtureFiles.ValidZip, $"-ai!{CliFixtureFiles.ValidZip}");

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

    // Chocolatey's Get-ChocolateyUnzip: "x -aoa -bd -bb1 -o<dir> -y <file>", then each "- <name>"
    // line joined to <dir> is recorded as an installed file.
    [Fact]
    public void Extract_ChocolateyLine_NamesEveryItemWritten()
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();
        string zip = Path.Combine(scratchDir, "pkg.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(CliFixtureFiles.SourceFileA, "tools/a.txt");
            archive.CreateEntryFromFile(CliFixtureFiles.SourceFileB, "tools/sub/b.txt");
            archive.CreateEntryFromFile(CliFixtureFiles.SourceFileA, "readme.txt");
        }
        string destDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("x", "-aoa", "-bd", "-bb1", $"-o{destDir}", "-y", zip);

        exitCode.Should().Be(0, stdErr);
        string[] named = [.. Lines(stdOut).Where(l => l.StartsWith("- ", StringComparison.Ordinal)).Select(l => l[2..])];
        string[] onDisk = [.. Directory.EnumerateDirectories(destDir, "*", SearchOption.AllDirectories).Select(d => Path.GetRelativePath(destDir, d) + @"\")
            .Concat(Directory.EnumerateFiles(destDir, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(destDir, f)))];
        named.Should().BeEquivalentTo(onDisk).And.Contain(@"tools\sub\b.txt").And.Contain(@"tools\sub\");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("-bb0")]
    public void Extract_WithoutLogLevel_NamesNothing(string? token)
    {
        string destDir = CliFixtureFiles.CreateScratchDir();
        string[] args = token is null ? ["x", $"-o{destDir}", CliFixtureFiles.ValidZip] : ["x", token, $"-o{destDir}", CliFixtureFiles.ValidZip];

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run(args);

        exitCode.Should().Be(0, stdErr);
        Lines(stdOut).Should().NotContain(l => l.StartsWith("- ", StringComparison.Ordinal));
    }

    private static string[] Lines(string text) => text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);

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

    // T-F417: as the vendored 7za 26.02 (measured): a name that takes only the plain entry needs
    // no password, stdin closed; one that takes the encrypted entry still fails without one.
    [Theory]
    [InlineData("t")]
    [InlineData("x")]
    public void FilteredToThePlainEntryOfAMixedZip_AsksForNoPassword(string command)
    {
        string scratchDir = CliFixtureFiles.CreateScratchDir();

        (int exitCode, _, string stdErr) = command == "x"
            ? CliProcessRunner.Run(command, "-o" + scratchDir, MixedZipPath, "readme.txt")
            : CliProcessRunner.Run(command, MixedZipPath, "readme.txt");

        exitCode.Should().Be(0, stdErr);
        if (command == "x")
            Directory.GetFiles(scratchDir, "*", SearchOption.AllDirectories).Select(Path.GetFileName).Should().Equal("readme.txt");
    }

    [Fact]
    public void Test_FilteredToTheEncryptedEntryOfAMixedZip_WithoutAPassword_Fails()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("t", MixedZipPath, "compressible.txt");

        exitCode.Should().Be(2);
        stdErr.Should().Contain("password");
    }

    private static string EncryptedZipPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "encrypted_aes256.zip");
    private static string MixedZipPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "mixed_encrypted_and_plain.zip");
}
