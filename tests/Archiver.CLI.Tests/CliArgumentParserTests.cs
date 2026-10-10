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

    // --- Version ---

    [Fact]
    public void DashDashVersion_ReturnsVersion()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["--version"]);

        result.Type.Should().Be(CliCommandType.Version);
    }

    [Fact]
    public void DashV_ReturnsVersion()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["-v"]);

        result.Type.Should().Be(CliCommandType.Version);
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

    // --- Valid: h (T-F128/T-F09 follow-up) ---

    [Fact]
    public void Hash_SingleFile_DefaultsToCrc32()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["h", "document.txt"]);

        result.Type.Should().Be(CliCommandType.Hash);
        result.SourcePaths.Should().Equal("document.txt");
        result.HashAlgorithm.Should().Be(HashAlgorithmKind.Crc32);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Hash_MultipleFiles_ReturnsAllPaths()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["h", "a.txt", "b.txt"]);

        result.Type.Should().Be(CliCommandType.Hash);
        result.SourcePaths.Should().Equal("a.txt", "b.txt");
    }

    [Fact]
    public void Hash_OneFolder_ReturnsFolderPath()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["h", @"C:\MyFolder"]);

        result.Type.Should().Be(CliCommandType.Hash);
        result.SourcePaths.Should().Equal(@"C:\MyFolder");
    }

    [Fact]
    public void Hash_ScrcCrc32_ReturnsCrc32()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["h", "-scrcCRC32", "document.txt"]);

        result.HashAlgorithm.Should().Be(HashAlgorithmKind.Crc32);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Hash_ScrcSha256_ReturnsSha256()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["h", "-scrcSHA256", "document.txt"]);

        result.HashAlgorithm.Should().Be(HashAlgorithmKind.Sha256);
    }

    [Fact]
    public void Hash_ScrcLowerCase_IsCaseInsensitive()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["h", "-scrcsha256", "document.txt"]);

        result.HashAlgorithm.Should().Be(HashAlgorithmKind.Sha256);
    }

    [Fact]
    public void Hash_ScrcUnknownMethod_ReturnsInvalidNamingSupportedMethods()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["h", "-scrcSHA1", "document.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("not supported by Pakko");
    }

    [Fact]
    public void Hash_ScrcMissingValue_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["h", "-scrc", "document.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
    }

    [Fact]
    public void Hash_NoPaths_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["h"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("at least one file or folder path");
    }

    [Fact]
    public void Hash_StdinFlag_SetsReadFromStdin()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["h", "-si"]);

        result.Type.Should().Be(CliCommandType.Hash);
        result.ReadFromStdin.Should().BeTrue();
        result.SourcePaths.Should().BeEmpty();
    }

    [Fact]
    public void Hash_StdinCombinedWithPath_ReturnsInvalid()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["h", "-si", "document.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
    }

    [Fact]
    public void Hash_ScrcSwitchOnUnsupportedCommand_ReturnsInvalidNamingCommand()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-scrcSHA256", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("only meaningful for 'h'");
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
    public void Extract_RecurseSwitch_ReturnsInvalidNamingUnsupportedSwitch()
    {
        // Case 3 (a real 7z switch, deliberately unsupported on any command) — kept as a
        // still-genuinely-unsupported switch after T-F191 gave '-p' a real meaning on 'x'/'t'.
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-r0", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("not supported");
    }

    // --- T-F191: -p{pwd} ---

    [Theory]
    [InlineData("x")]
    [InlineData("t")]
    public void PasswordSwitch_OnExtractOrTest_IsParsedAndDoesNotError(string command)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, "-pSecret123", "archive.zip"]);

        result.Type.Should().Be(command == "x" ? CliCommandType.Extract : CliCommandType.Test);
        result.Password.Should().Be("Secret123");
    }

    // T-F193: a bare -p means "ask me", as in real 7z — it was rejected before.
    [Theory]
    [InlineData("x")]
    [InlineData("t")]
    public void PasswordSwitch_Bare_OnExtractOrTest_RequestsAPrompt(string command)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, "-p", "archive.zip"]);

        result.Type.Should().Be(command == "x" ? CliCommandType.Extract : CliCommandType.Test);
        result.PromptForPassword.Should().BeTrue();
        result.Password.Should().BeNull();
    }

    [Theory]
    [InlineData("x")]
    [InlineData("t")]
    public void PasswordSwitch_Bare_WithStdinArchive_ReturnsInvalid(string command)
    {
        // -si already consumes stdin for the archive bytes, so nothing is left to type into.
        ParsedCliCommand result = CliArgumentParser.Parse([command, "-p", "-si"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("-si");
    }

    [Fact]
    public void PasswordSwitch_BareThenValued_ValueWins()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-p", "-pSecret", "archive.zip"]);

        result.Password.Should().Be("Secret");
        result.PromptForPassword.Should().BeFalse();
    }

    [Theory]
    [InlineData("l")]
    [InlineData("h")]
    public void PasswordSwitch_OnCommandThatNeedsNoPassword_ReturnsInvalid(string command)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, "-pSecret123", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("not supported");
    }

    // --- T-F193: -p on 'a' (AES-256 creation) ---

    [Fact]
    public void Archive_PasswordSwitch_IsParsed()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "out.zip", "-pSecret123", "file.txt"]);

        result.Type.Should().Be(CliCommandType.Archive);
        result.Password.Should().Be("Secret123");
        result.SourcePaths.Should().Equal("file.txt");
    }

    [Fact]
    public void Archive_BarePasswordSwitch_RequestsAPrompt()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-p", "out.zip", "file.txt"]);

        result.Type.Should().Be(CliCommandType.Archive);
        result.PromptForPassword.Should().BeTrue();
    }

    [Theory]
    [InlineData("-ttar")]
    [InlineData("-ttar.gz")]
    [InlineData("-ttar.zst")]
    public void Archive_PasswordWithTarFormat_ReturnsInvalidRegardlessOfOrder(string typeSwitch)
    {
        ParsedCliCommand before = CliArgumentParser.Parse(["a", "-pSecret", typeSwitch, "out.tar", "file.txt"]);
        ParsedCliCommand after = CliArgumentParser.Parse(["a", typeSwitch, "out.tar", "file.txt", "-p"]);

        before.Type.Should().Be(CliCommandType.Invalid);
        before.ErrorMessage.Should().Contain("ZIP");
        after.Type.Should().Be(CliCommandType.Invalid);
    }

    [Fact]
    public void Archive_PasswordWithExplicitZipFormat_IsParsed()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-tzip", "-pSecret", "out.zip", "file.txt"]);

        result.Type.Should().Be(CliCommandType.Archive);
    }

    [Fact]
    public void Archive_NonAsciiPassword_IsParsedAndLeftToTheEncryptRuleAtRunTime()
    {
        // The parser stays format-agnostic; Program.cs applies EncryptionPasswordRule so x/t keep
        // accepting any password for decryption.
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-pпароль", "out.zip", "file.txt"]);

        result.Type.Should().Be(CliCommandType.Archive);
        result.Password.Should().Be("пароль");
    }

    [Theory]
    [InlineData("-mem=AES256")]
    [InlineData("-mem=aes256")]
    public void Archive_EncryptionMethodAes256_IsAcceptedAsNoOp(string token)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-pSecret", token, "out.zip", "file.txt"]);

        result.Type.Should().Be(CliCommandType.Archive);
        result.Password.Should().Be("Secret");
    }

    [Theory]
    [InlineData("-mem=ZipCrypto")]
    [InlineData("-mem=zipcrypto")]
    [InlineData("-mem=AES128")]
    [InlineData("-mem=AES192")]
    public void Archive_WeakerEncryptionMethod_ReturnsInvalidExplainingAesOnly(string token)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-pSecret", token, "out.zip", "file.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("not supported by Pakko").And.Contain("AES-256");
    }

    [Theory]
    [InlineData("-mem=")]
    [InlineData("-mem=Blowfish")]
    public void Archive_UnknownEncryptionMethod_ReturnsInvalid(string token)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-pSecret", token, "out.zip", "file.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("-mem");
    }

    [Fact]
    public void Archive_OtherMethodParameter_ReturnsInvalidWithoutClaimingItBelongsToA()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-mhe=on", "out.zip", "file.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().NotContain("only meaningful for 'a'");
    }

    [Fact]
    public void PasswordSwitch_GivenTwice_LastOneWins()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-pFirst", "-pSecond", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Extract);
        result.Password.Should().Be("Second");
    }

    [Fact]
    public void NoPasswordSwitch_ArchivePathsStillParsed()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Extract);
        result.Password.Should().BeNull();
        result.PromptForPassword.Should().BeFalse();
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

    // T-F222: --help lists -ao{a|s|u}; the errors offered -aot as valid too, which is then refused.
    [Theory]
    [InlineData("-ao")]
    [InlineData("-aox")]
    [InlineData("-aoaa")]
    public void Extract_BadOverwriteMode_ListsOnlyTheModesPakkoAccepts(string token)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", token, "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("a, s, or u").And.NotContain("or t");
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

    // --- -scc{charset} (T-F238): 7-Zip's console charset switch, accepted on every command ---

    [Theory]
    [InlineData("-sccUTF-8", CliConsoleCharset.Utf8)]
    [InlineData("-sccutf-8", CliConsoleCharset.Utf8)]
    [InlineData("-sccWIN", CliConsoleCharset.Ansi)]
    [InlineData("-sccDOS", CliConsoleCharset.Oem)]
    public void List_SccCharset_SetsConsoleCodePage(string token, int expected)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["l", token, "archive.zip"]);

        result.Type.Should().Be(CliCommandType.List);
        result.ConsoleCodePage.Should().Be(expected);
        result.ArchivePaths.Should().Equal("archive.zip");
    }

    [Theory]
    [InlineData("x")]
    [InlineData("t")]
    [InlineData("l")]
    [InlineData("h")]
    public void EveryPathCommand_AcceptsScc(string command)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, "somefile", "-sccUTF-8"]);

        result.Type.Should().NotBe(CliCommandType.Invalid, result.ErrorMessage);
        result.ConsoleCodePage.Should().Be(CliConsoleCharset.Utf8);
    }

    [Fact]
    public void Archive_AcceptsScc()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-sccUTF-8", "out.zip", "file.txt"]);

        result.Type.Should().Be(CliCommandType.Archive);
        result.ConsoleCodePage.Should().Be(CliConsoleCharset.Utf8);
        result.ArchivePathArg.Should().Be("out.zip");
    }

    [Fact]
    public void Info_AcceptsScc()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["i", "-sccUTF-8"]);

        result.Type.Should().Be(CliCommandType.Info);
        result.ConsoleCodePage.Should().Be(CliConsoleCharset.Utf8);
    }

    [Fact]
    public void Scc_LastOccurrenceWins()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["l", "-sccDOS", "-sccUTF-8", "archive.zip"]);

        result.ConsoleCodePage.Should().Be(CliConsoleCharset.Utf8);
    }

    [Fact]
    public void NoScc_LeavesConsoleCodePageUnset()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["l", "archive.zip"]);

        result.ConsoleCodePage.Should().BeNull();
    }

    [Theory]
    [InlineData("-scc")]
    [InlineData("-sccKOI8")]
    [InlineData("-scc1251")]
    [InlineData("-sccUTF-16LE")]
    public void Scc_UnsupportedCharset_IsInvalidNamingSupportedOnes(string token)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["l", token, "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("UTF-8").And.Contain("WIN").And.Contain("DOS");
    }

    // --- T-F294/T-F296: PowerShell splits "-name.rest" into "-name" and ".rest" ---

    [Theory]
    [InlineData("a", "-ttar", ".gz", "t.tar.gz", "src")]
    [InlineData("a", "t2.tar.gz", "-ttar", ".gz", "src")]
    [InlineData("a", "-ttar", ".gz", "t3.tar.gz", "a.txt")]
    public void SplitTypeSwitch_IsACommandLineError(params string[] args)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(args);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("'-ttar'").And.Contain("'.gz'").And.Contain("'-ttar.gz'").And.Contain(@".\.gz");
    }

    [Theory]
    [InlineData("x", "-pSecret", ".1", "a.zip")]
    [InlineData("a", "-pSecret", ".1", "out.zip", "src")]
    [InlineData("t", "-pZq9", ".b.c", "a.zip")]
    public void SplitPassword_IsACommandLineErrorThatDoesNotEchoThePassword(params string[] args)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(args);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("-p<password>").And.NotContain(args[1][2..]).And.NotContain(args[2]);
    }

    [Theory]
    [InlineData("x", "-y", @".\a.zip")]
    [InlineData("x", "-y", @"..\a.zip")]
    [InlineData("x", "-y", "./a.zip")]
    [InlineData("x", @"-oC:\out.d", "a.zip")]
    [InlineData("a", "-ttar", @".\.gz", "src")]
    [InlineData("h", "-scrcSHA256", ".gitignore")]   // a complete switch, then a dotfile: cmd passes this
    [InlineData("x", "-y", ".hidden.zip")]
    [InlineData("a", "-y", ".backup.zip", "src")]
    public void DotLeadingPathThatIsNotASplitPiece_IsAccepted(params string[] args)
    {
        CliArgumentParser.Parse(args).Type.Should().NotBe(CliCommandType.Invalid);
    }

    [Theory]
    [InlineData("-ttgz", ArchiveContainerFormat.TarGz)]
    [InlineData("-ttbz2", ArchiveContainerFormat.TarBz2)]
    [InlineData("-ttxz", ArchiveContainerFormat.TarXz)]
    [InlineData("-ttzst", ArchiveContainerFormat.TarZst)]
    public void Archive_DotFreeTypeAlias_MapsToContainerFormat(string switchToken, ArchiveContainerFormat expected)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", switchToken, "out", "file1.txt"]);

        result.ArchiveFormat.Should().Be(expected);
    }

    // "-oout.d" in PowerShell: the destination would silently be "out".
    [Fact]
    public void SplitOutputDirectory_IsACommandLineError()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-oout", ".d", "a.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("'-oout'").And.Contain("'.d'");
    }

    // --- T-F294/T-F296: the archive type and the name's own extension must agree ---

    [Theory]
    [InlineData("out.tar.gz")]
    [InlineData("out.tar")]
    [InlineData("OUT.TAR.XZ")]
    [InlineData("backup.tgz")]
    [InlineData("backup.tbz2")]
    [InlineData("backup.txz")]
    [InlineData("backup.tzst")]
    public void Archive_TarNameWithoutTypeSwitch_IsACommandLineError(string name)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", name, "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain(name).And.Contain("no -t");
    }

    [Theory]
    [InlineData("-ttar", "out.zip")]
    [InlineData("-ttar.gz", "out.tar")]
    [InlineData("-tzip", "out.tar.gz")]
    [InlineData("-ttgz", "out.tar.bz2")]
    [InlineData("-ttar", "out.tgz")]
    public void Archive_TypeSwitchContradictsName_IsACommandLineError(string typeSwitch, string name)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", typeSwitch, name, "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain(name).And.Contain(typeSwitch);
    }

    [Theory]
    [InlineData("-ttar.gz", "out.tar.gz")]
    [InlineData("-ttgz", "OUT.TAR.GZ")]
    [InlineData("-ttgz", "out.tgz")]
    [InlineData("-ttar.gz", "out.tgz")]
    [InlineData("-ttxz", "out.txz")]
    [InlineData("-ttar", "out.tar")]
    [InlineData("-ttar", "out.gz")]      // T-F221 item 6: a name that is no archive type is written as typed
    [InlineData("-tzip", "backup.out")]
    [InlineData("-tzip", "out.zip")]
    [InlineData("-ttar", "out")]
    public void Archive_TypeSwitchAgreesWithNameOrNameHasNoArchiveType_IsAccepted(string typeSwitch, string name)
    {
        CliArgumentParser.Parse(["a", typeSwitch, name, "file1.txt"]).Type.Should().Be(CliCommandType.Archive);
    }

    [Fact]
    public void Archive_ZipNameWithoutTypeSwitch_IsAccepted()
    {
        CliArgumentParser.Parse(["a", "out.zip", "file1.txt"]).Type.Should().Be(CliCommandType.Archive);
    }

    // --- T-F275: -rr[N], PAR2 recovery data next to the archive ---

    [Theory]
    [InlineData("-rr", 5)]
    [InlineData("-rr1", 1)]
    [InlineData("-rr10", 10)]
    [InlineData("-rr100", 100)]
    public void Archive_RecoverySwitch_SetsThePercent(string token, int expected)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", token, "out.zip", "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Archive);
        result.RecoveryPercent.Should().Be(expected);
    }

    [Fact]
    public void Archive_NoRecoverySwitch_NoRecoveryData() =>
        CliArgumentParser.Parse(["a", "out.zip", "file1.txt"]).RecoveryPercent.Should().Be(0);

    [Fact]
    public void Archive_RecoverySwitchTwice_LastWins() =>
        CliArgumentParser.Parse(["a", "-rr20", "-rr", "out.zip", "file1.txt"]).RecoveryPercent.Should().Be(5);

    [Theory]
    [InlineData("-rr0")]
    [InlineData("-rr101")]
    [InlineData("-rrx")]
    [InlineData("-rr5%")]
    [InlineData("-rr-5")]
    [InlineData("-rr99999999999")]
    public void Archive_RecoverySwitchOutOfRange_IsACommandLineError(string token)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", token, "out.zip", "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("-rr").And.Contain("1").And.Contain("100");
    }

    [Fact]
    public void Archive_RecoveryWithStdout_IsACommandLineError()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-rr", "-so", "out.zip", "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("-rr").And.Contain("-so");
    }

    [Theory]
    [InlineData("-ttar.gz", "out.tar.gz")]
    [InlineData("-tzip", "out.zip")]
    public void Archive_RecoveryWithAnyType_IsAccepted(string typeSwitch, string name) =>
        CliArgumentParser.Parse(["a", "-rr", typeSwitch, name, "file1.txt"]).RecoveryPercent.Should().Be(5);

    [Fact]
    public void Archive_RecoveryWithPassword_IsAccepted() =>
        CliArgumentParser.Parse(["a", "-rr", "-psecret", "out.zip", "file1.txt"]).Type.Should().Be(CliCommandType.Archive);

    [Theory]
    [InlineData("x")]
    [InlineData("t")]
    [InlineData("l")]
    public void RecoverySwitch_OnAnotherCommand_SaysItIsOnlyForA(string command)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, "-rr", "archive.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("-rr").And.Contain("'a'");
    }

    [Fact]
    public void RecurseSwitch_StillHasItsOwnMessage() =>
        CliArgumentParser.Parse(["x", "-r", "archive.zip"]).ErrorMessage.Should().Contain("recurse");
}
