using Archiver.CLI;
using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.CLI.Tests;

public sealed class CliArgumentParserTests
{
    // --- Help / bare invocation ---

    [Fact]
    public void NoArguments_ReturnsHelp()
    {
        ParsedCliCommand result = CliArgumentParser.Parse([]);

        result.Type.Should().Be(CliCommandType.Help);
    }

    [Fact]
    public void DashDashHelp_ReturnsHelp()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["--help"]);

        result.Type.Should().Be(CliCommandType.Help);
    }

    [Fact]
    public void DashH_ReturnsHelp()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["-h"]);

        result.Type.Should().Be(CliCommandType.Help);
    }

    // --- Valid: x ---

    [Fact]
    public void Extract_SingleArchive_ReturnsExtract()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Extract);
        result.ArchivePaths.Should().Equal("archive.zip");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Extract_MultipleArchives_ReturnsAllPaths()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "a.zip", "b.zip"]);

        result.ArchivePaths.Should().Equal("a.zip", "b.zip");
    }

    [Fact]
    public void Extract_OutputDirectorySwitch_SetsOutputDirectory()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-oC:\\dest", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Extract);
        result.OutputDirectory.Should().Be("C:\\dest");
        result.ArchivePaths.Should().Equal("archive.zip");
    }

    [Fact]
    public void Extract_AssumeYesSwitch_SetsAssumeYes()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-y", "archive.zip"]);

        result.AssumeYes.Should().BeTrue();
    }

    [Theory]
    [InlineData("-aoa", ConflictBehavior.Overwrite)]
    [InlineData("-aos", ConflictBehavior.Skip)]
    [InlineData("-aou", ConflictBehavior.Rename)]
    public void Extract_OverwriteModeSwitch_MapsToConflictBehavior(string switchToken, ConflictBehavior expected)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", switchToken, "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Extract);
        result.OverwriteMode.Should().Be(expected);
    }

    [Fact]
    public void Extract_SwitchesInterspersedAroundPaths_ParsedCorrectly()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-y", "a.zip", "-oC:\\dest", "b.zip"]);

        result.Type.Should().Be(CliCommandType.Extract);
        result.AssumeYes.Should().BeTrue();
        result.OutputDirectory.Should().Be("C:\\dest");
        result.ArchivePaths.Should().Equal("a.zip", "b.zip");
    }

    [Fact]
    public void Extract_NoArchivePaths_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    // --- Valid: t ---

    [Fact]
    public void Test_SingleArchive_ReturnsTest()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["t", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Test);
        result.ArchivePaths.Should().Equal("archive.zip");
    }

    [Fact]
    public void Test_MultipleArchives_ReturnsAllPaths()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["t", "a.zip", "b.zip"]);

        result.ArchivePaths.Should().Equal("a.zip", "b.zip");
    }

    [Fact]
    public void Test_NoArchivePaths_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["t"]);

        result.Type.Should().Be(CliCommandType.Invalid);
    }

    // --- Valid: i ---

    [Fact]
    public void Info_NoArguments_ReturnsInfo()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["i"]);

        result.Type.Should().Be(CliCommandType.Info);
    }

    // --- Valid: a ---

    [Fact]
    public void Archive_NameAndOneSource_ReturnsArchive()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "out.zip", "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Archive);
        result.ArchivePathArg.Should().Be("out.zip");
        result.SourcePaths.Should().Equal("file1.txt");
        result.ArchiveFormat.Should().Be(ArchiveContainerFormat.Zip);
    }

    [Fact]
    public void Archive_NameAndMultipleSources_ReturnsAllSources()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "out.zip", "file1.txt", "file2.txt"]);

        result.SourcePaths.Should().Equal("file1.txt", "file2.txt");
    }

    [Theory]
    [InlineData("-tzip", ArchiveContainerFormat.Zip)]
    [InlineData("-ttar", ArchiveContainerFormat.Tar)]
    [InlineData("-ttar.gz", ArchiveContainerFormat.TarGz)]
    [InlineData("-ttar.bz2", ArchiveContainerFormat.TarBz2)]
    [InlineData("-ttar.xz", ArchiveContainerFormat.TarXz)]
    [InlineData("-ttar.zst", ArchiveContainerFormat.TarZst)]
    [InlineData("-ttar.lzma", ArchiveContainerFormat.TarLzma)]
    public void Archive_TypeSwitch_MapsToContainerFormat(string switchToken, ArchiveContainerFormat expected)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", switchToken, "out", "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Archive);
        result.ArchiveFormat.Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(9)]
    public void Archive_CompressionLevelSwitch_BoundaryValues_Accepted(int mx)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", $"-mx={mx}", "out.zip", "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Archive);
        result.CompressionLevel.Should().NotBeNull();
    }

    [Fact]
    public void Archive_AssumeYesSwitch_SetsAssumeYes()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-y", "out.zip", "file1.txt"]);

        result.AssumeYes.Should().BeTrue();
    }

    [Fact]
    public void Archive_MissingSourceFiles_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "out.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
    }

    [Fact]
    public void Archive_NoArguments_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a"]);

        result.Type.Should().Be(CliCommandType.Invalid);
    }

    // --- Valid: l ---

    [Fact]
    public void List_SingleArchive_ReturnsList()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["l", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.List);
        result.ArchivePaths.Should().Equal("archive.zip");
    }

    [Fact]
    public void List_MultipleArchives_ReturnsAllPaths()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["l", "a.zip", "b.zip"]);

        result.ArchivePaths.Should().Equal("a.zip", "b.zip");
    }

    [Fact]
    public void List_NoArchivePaths_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["l"]);

        result.Type.Should().Be(CliCommandType.Invalid);
    }

    // --- Invalid: case 1 (unparseable / typo) ---

    [Fact]
    public void UnknownCommand_ReturnsInvalidWithIncorrectCommandLineMessage()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["q", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("Incorrect command line");
    }

    [Fact]
    public void UnknownSwitch_OnSupportedCommand_ReturnsInvalidWithIncorrectCommandLineMessage()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-qqq", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("Incorrect command line");
    }

    // --- Invalid: case 2 (deliberately unsupported command) ---

    [Theory]
    [InlineData("u")]
    [InlineData("d")]
    [InlineData("rn")]
    [InlineData("b")]
    [InlineData("e")]
    public void DeliberatelyUnsupportedCommand_ReturnsInvalidNamingReason(string command)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("not supported by Pakko");
    }

    // --- Invalid: case 3 (unsupported switch on a supported command) ---

    [Fact]
    public void Extract_PasswordSwitch_ReturnsInvalidNamingEncryptionGap()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-psecret", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("encryption");
    }

    [Fact]
    public void Extract_ArchiveTypeSwitch_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-tzip", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
    }

    [Fact]
    public void Test_AssumeYesSwitch_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["t", "-y", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
    }

    [Fact]
    public void Archive_SevenZipType_ReturnsInvalidNamingExtractOnlyGap()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-t7z", "out", "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("extract-only");
    }

    [Fact]
    public void Archive_RarType_ReturnsInvalidNamingExtractOnlyGap()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-trar", "out", "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("extract-only");
    }

    [Fact]
    public void Archive_CompressionLevelOutOfRange_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-mx=10", "out.zip", "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
    }

    [Fact]
    public void List_AnySwitch_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["l", "-y", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
    }

    // --- Adversarial inputs ---

    [Fact]
    public void Extract_OutputSwitchWithNoValue_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-o", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
    }

    [Fact]
    public void Extract_OverwriteSwitchWithNoModeLetter_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-ao", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
    }

    [Fact]
    public void Archive_CompressionLevelNonNumeric_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-mx=abc", "out.zip", "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
    }

    [Fact]
    public void Extract_OverwriteModeT_ReturnsInvalidNamingNoEquivalent()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-aot", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("no equivalent");
    }

    // --- T-F116: -si / -so ---

    [Fact]
    public void Extract_SiSwitch_SetsReadFromStdinWithNoArchivePath()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-si"]);

        result.Type.Should().Be(CliCommandType.Extract);
        result.ReadFromStdin.Should().BeTrue();
        result.ArchivePaths.Should().BeEmpty();
    }

    [Fact]
    public void Extract_SiSwitchWithExplicitArchivePath_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-si", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("-si");
    }

    [Fact]
    public void Extract_SoSwitch_SetsWriteToStdout()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-so", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Extract);
        result.WriteToStdout.Should().BeTrue();
    }

    [Fact]
    public void Extract_SoSwitchWithOutputDirectory_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-so", "-oC:\\dest", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("-o");
    }

    [Fact]
    public void Test_SiSwitch_SetsReadFromStdinWithNoArchivePath()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["t", "-si"]);

        result.Type.Should().Be(CliCommandType.Test);
        result.ReadFromStdin.Should().BeTrue();
        result.ArchivePaths.Should().BeEmpty();
    }

    [Fact]
    public void Test_SoSwitch_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["t", "-so", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
    }

    [Fact]
    public void List_SiSwitch_SetsReadFromStdinWithNoArchivePath()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["l", "-si"]);

        result.Type.Should().Be(CliCommandType.List);
        result.ReadFromStdin.Should().BeTrue();
        result.ArchivePaths.Should().BeEmpty();
    }

    [Fact]
    public void List_SoSwitch_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["l", "-so", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
    }

    [Fact]
    public void Archive_SoSwitch_SetsWriteToStdout()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-so", "out.zip", "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Archive);
        result.WriteToStdout.Should().BeTrue();
    }

    [Fact]
    public void Archive_SiSwitch_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-si", "out.zip", "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
    }

    [Fact]
    public void Info_SiSwitch_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["i", "-si"]);

        result.Type.Should().Be(CliCommandType.Invalid);
    }

    [Fact]
    public void Info_SoSwitch_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["i", "-so"]);

        result.Type.Should().Be(CliCommandType.Invalid);
    }
}
