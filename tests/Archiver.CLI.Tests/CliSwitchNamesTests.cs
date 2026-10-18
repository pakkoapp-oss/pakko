using Archiver.CLI;
using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F411: switch names, the command and repeated switches as 7-Zip reads them (CommandLineParser.cpp,
// 26.02), each case measured against the vendored 7za first.
public sealed class CliSwitchNamesTests
{
    // --- Happy path ---

    [Theory]
    [InlineData("X", CliCommandType.Extract)]
    [InlineData("L", CliCommandType.List)]
    [InlineData("T", CliCommandType.Test)]
    public void Command_InCapitals_IsTaken(string command, CliCommandType expected)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, "a.zip"]);

        result.Type.Should().Be(expected, result.ErrorMessage);
        result.ArchivePaths.Should().Equal("a.zip");
    }

    [Fact]
    public void Command_A_InCapitals_IsTaken()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["A", "out.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Archive, result.ErrorMessage);
        result.ArchivePathArg.Should().Be("out.zip");
    }

    [Fact]
    public void SwitchBeforeTheCommand_IsTaken()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["-y", "x", "a.zip", "-oout"]);

        result.Type.Should().Be(CliCommandType.Extract, result.ErrorMessage);
        result.AssumeYes.Should().BeTrue();
        result.OutputDirectory.Should().Be("out");
        result.ArchivePaths.Should().Equal("a.zip");
    }

    [Fact]
    public void DoubleDashFirst_TheCommandIsTheFirstNameAfterIt()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["--", "x", "a.zip"]);

        result.Type.Should().Be(CliCommandType.Extract, result.ErrorMessage);
        result.ArchivePaths.Should().Equal("a.zip");
    }

    [Fact]
    public void SwitchThenDoubleDash_EverythingAfterItIsAName()
    {
        // 7za: "-y -- x a.zip -ooq" extracts nothing ("No files to process"): -ooq is a name.
        ParsedCliCommand result = CliArgumentParser.Parse(["-y", "--", "x", "a.zip", "-ooq"]);

        result.Type.Should().Be(CliCommandType.Extract, result.ErrorMessage);
        result.OutputDirectory.Should().BeNull();
        result.FilterNames.Select(n => n.Text).Should().Equal("-ooq");
    }

    [Fact]
    public void SwitchNames_InCapitals_AreTaken()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "a.zip", "-Y", @"-OC:\Out", "-BB1", "-XR!*.tmp", "-SNZ0"]);

        result.Type.Should().Be(CliCommandType.Extract, result.ErrorMessage);
        result.AssumeYes.Should().BeTrue();
        result.OutputDirectory.Should().Be(@"C:\Out");
        result.Output.LogLevel.Should().Be(1u);
        result.ApplyDownloadMark.Should().BeFalse();
        result.NameSwitches.Names.Should().ContainSingle().Which.Text.Should().Be("*.tmp");
    }

    [Theory]
    [InlineData("-MX=9")]
    [InlineData("-mX9")]
    [InlineData("-mx9")]
    [InlineData("-mx")]
    [InlineData("-mx10")]
    [InlineData("-mx=99")]
    [InlineData("-mx=99999999999")]
    [InlineData("-mx=")] // 7za: exit 0, the same archive as -mx
    public void CompressionLevel_EverySpelling7ZipTakes_IsTheHighest(string token)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", token, "out.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Archive, result.ErrorMessage);
        result.CompressionLevel.Should().Be(CliArgumentParser.Parse(["a", "-mx=9", "out.zip", "f.txt"]).CompressionLevel);
    }

    [Fact]
    public void CompressionLevel_WithoutEquals_IsThatLevel() =>
        CliArgumentParser.Parse(["a", "-mx0", "out.zip", "f.txt"]).CompressionLevel
            .Should().Be(CliArgumentParser.Parse(["a", "-mx=0", "out.zip", "f.txt"]).CompressionLevel);

    [Theory]
    [InlineData("-tZIP", ArchiveContainerFormat.Zip)]
    [InlineData("-T7Z", ArchiveContainerFormat.SevenZip)]
    [InlineData("-tTar.Gz", ArchiveContainerFormat.TarGz)]
    public void ArchiveType_InCapitals_IsTaken(string token, ArchiveContainerFormat expected)
    {
        string name = expected switch { ArchiveContainerFormat.SevenZip => "out.7z", ArchiveContainerFormat.TarGz => "out.tar.gz", _ => "out.zip" };
        ParsedCliCommand result = CliArgumentParser.Parse(["a", token, name, "f.txt"]);

        result.Type.Should().Be(CliCommandType.Archive, result.ErrorMessage);
        result.ArchiveFormat.Should().Be(expected);
    }

    [Fact]
    public void MethodProperty_InCapitals_IsTaken()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-t7z", "-M0=LZMA", "out.7z", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Archive, result.ErrorMessage);
        result.SevenZipMethod.Should().Be(SevenZipMethod.Lzma);
    }

    [Fact]
    public void MultiSwitches_GivenTwice_AreTaken()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "a.zip", "-x!a.txt", "-x!d", "-ir!*.txt", "-i!b"]);

        result.Type.Should().Be(CliCommandType.Extract, result.ErrorMessage);
        result.NameSwitches.Names.Should().HaveCount(4);
    }

    [Fact]
    public void MethodSwitch_GivenTwice_TheLastLevelIsTaken()
    {
        // -m is Multi in 7-Zip; "7za a m.zip f -mx1 -mx=9" is exit 0.
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-mx1", "-mx=9", "out.zip", "f.txt"]);

        result.CompressionLevel.Should().Be(CliArgumentParser.Parse(["a", "-mx=9", "out.zip", "f.txt"]).CompressionLevel);
    }

    // --- Security & Boundary ---

    [Fact]
    public void ValuesAfterTheName_KeepTheirCase()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["X", "a.zip", "-PAbC", @"-OC:\MiXed\Dir"]);

        result.Password.Should().Be("AbC");
        result.OutputDirectory.Should().Be(@"C:\MiXed\Dir");
    }

    [Fact]
    public void ANameMask_KeepsItsCase()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "a.zip", "-I!ReadMe.TXT"]);

        result.NameSwitches.Names.Should().ContainSingle().Which.Text.Should().Be("ReadMe.TXT");
    }

    [Theory]
    [InlineData("-\u0131!a.txt")] // dotless i: 'I' under ToUpperInvariant
    [InlineData("-\u0130!a.txt")] // capital I with a dot: 'i' under ToLowerInvariant
    [InlineData("-\u017Fo")] // long s: 'S' under ToUpperInvariant, so "-so" to a fold that is not ASCII-only
    public void NonAsciiLetterInASwitchName_MatchesNoSwitch(string token)
    {
        // 7-Zip compares ASCII letters only: measured, 7za says "Unknown switch".
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "a.zip", token]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.NameSwitches.Names.Should().BeEmpty();
    }

    [Theory]
    [InlineData("\u0130")] // capital I with a dot
    [InlineData("\u0445")] // Cyrillic small ha, which looks like x
    public void NonAsciiCommand_IsUnknown(string command)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, "a.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("unknown command").And.Contain(command);
    }

    [Theory]
    [InlineData("-rr5", 5)]
    [InlineData("-RR20", 20)]
    public void RecoverySwitch_IsNotReadAsRecursion(string token, int percent)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-r", token, "out.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Archive, result.ErrorMessage);
        result.RecoveryPercent.Should().Be(percent);
    }

    [Fact]
    public void LongestNameWins_ScrcIsNotScc()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["h", "-SCRCsha256", "-SCCutf-8", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Hash, result.ErrorMessage);
        result.HashAlgorithm.Should().Be(HashAlgorithmKind.Sha256);
        result.ConsoleCodePage.Should().Be(CliConsoleCharset.Utf8);
    }

    [Fact]
    public void ToLowerAscii_LeavesEveryNonAsciiCharacter()
    {
        CliSwitchNames.ToLowerAscii("AZaz09-\u0130\u0131\u0410").Should().Be("azaz09-\u0130\u0131\u0410");
    }

    // --- Misuse & Fool ---

    [Theory]
    [InlineData("-AOA")]
    [InlineData("-aoA")]
    public void PostfixLetter_IsCaseSensitive(string token)
    {
        // 7za: "Incorrect switch postfix", exit 7.
        CliArgumentParser.Parse(["x", "a.zip", token]).Type.Should().Be(CliCommandType.Invalid);
    }

    [Theory]
    [InlineData("Xx a.zip")]
    [InlineData("a.zip x")]
    public void NotACommand_IsUnknown(string line)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(line.Split(' '));

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("unknown command");
    }

    [Theory]
    [InlineData("-y")]
    [InlineData("--")]
    [InlineData("-y --")]
    public void NoCommand_IsACommandLineError(string line)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(line.Split(' '));

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("no command");
    }

    [Theory]
    [InlineData("-y", "-Y")]
    [InlineData("-oa", "-ob")]
    [InlineData("-tzip", "-tZIP")]
    [InlineData("-sae", "-sas")]
    [InlineData("-spd", "-spd")]
    [InlineData("-ssc", "-ssc-")]
    public void SwitchTakenOnce_GivenTwice_IsAMultipleInstanceError(string first, string second)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", first, second, "out.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().StartWith("Multiple instances for switch: -");
    }

    [Fact]
    public void Scrc_GivenTwice_IsRefused()
    {
        // 7-Zip computes both; Pakko computes one, so it refuses rather than drop one silently.
        ParsedCliCommand result = CliArgumentParser.Parse(["h", "-scrcCRC32", "-scrcSHA256", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("one hash method");
    }

    [Fact]
    public void ARepeatedSwitchAfterDoubleDash_IsAName()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-y", "a.zip", "--", "-y"]);

        result.Type.Should().Be(CliCommandType.Extract, result.ErrorMessage);
        result.FilterNames.Select(n => n.Text).Should().Equal("-y");
    }

    [Theory]
    [InlineData("-H")]
    [InlineData("-?")]
    [InlineData("--HELP")]
    public void HelpSwitch_AnywhereAndInAnyCase_IsHelp(string token)
    {
        // 7za: "x a.zip -H" prints the help and exits 0.
        CliArgumentParser.Parse(["x", "a.zip", token]).Type.Should().Be(CliCommandType.Help);
        CliArgumentParser.Parse([token]).Type.Should().Be(CliCommandType.Help);
    }

    [Theory]
    [InlineData("-hx")]
    [InlineData("-HELP")]
    public void NotAHelpSwitch_IsNotHelp(string token)
    {
        CliArgumentParser.Parse(["x", "a.zip", token]).Type.Should().Be(CliCommandType.Invalid);
    }

    [Fact]
    public void HelpSwitchAfterDoubleDash_IsAName()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "a.zip", "--", "-h"]);

        result.Type.Should().Be(CliCommandType.Extract, result.ErrorMessage);
        result.FilterNames.Select(n => n.Text).Should().Equal("-h");
    }

    // --- Error path ---

    [Theory]
    [InlineData("-p secret x a.zip")]
    [InlineData("-P secret x a.zip")]
    [InlineData("-p -- secret x a.zip")]
    public void AWordAfterABarePassword_IsNeverPrintedAsTheCommand(string line)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(line.Split(' '));

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().NotContain("secret").And.Contain("bare -p");
    }

    [Fact]
    public void ACommandAfterABarePassword_IsTaken()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["-p", "x", "a.zip"]);

        result.Type.Should().Be(CliCommandType.Extract, result.ErrorMessage);
        result.PromptForPassword.Should().BeTrue();
    }

    [Fact]
    public void MultipleInstanceError_NamesTheSecondToken()
    {
        CliArgumentParser.Parse(["x", "a.zip", "-oFirst", "-oSecond"]).ErrorMessage
            .Should().Be("Multiple instances for switch: -oSecond");
    }

    [Fact]
    public void MultipleInstanceError_NeverPrintsAPassword()
    {
        string? message = CliArgumentParser.Parse(["x", "a.zip", "-pS3cret", "-PS3cret2"]).ErrorMessage;

        message.Should().Be("Multiple instances for switch: -p");
    }

    [Theory]
    [InlineData("-mxa")]
    [InlineData("-mx=-1")]
    public void CompressionLevel_NotANumber_IsACommandLineError(string token)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", token, "out.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("-mx takes a level 0-9");
    }
}
