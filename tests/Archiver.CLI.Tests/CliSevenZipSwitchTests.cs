using System.IO.Compression;
using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F342 (the CLI wave): 'a -t7z' and '-m0=<method>', as CliArgumentParser.Parse returns them
// after CliCommandValidator's rules across switches.
public sealed class CliSevenZipSwitchTests
{
    [Fact]
    public void Archive_SevenZipType_IsTheSevenZipFormatWithNoMethodNamed()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-t7z", "out", "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Archive);
        result.ArchiveFormat.Should().Be(ArchiveContainerFormat.SevenZip);
        result.SevenZipMethod.Should().BeNull("no -m0 was given, and the default is the caller's to pick");
    }

    [Theory]
    [InlineData("-m0=lzma2", SevenZipMethod.Lzma2)]
    [InlineData("-m0=LZMA2", SevenZipMethod.Lzma2)]
    [InlineData("-m0=lzma", SevenZipMethod.Lzma)]
    [InlineData("-m0=Lzma", SevenZipMethod.Lzma)]
    [InlineData("-m0=bzip2", SevenZipMethod.BZip2)]
    [InlineData("-m0=BZip2", SevenZipMethod.BZip2)]
    [InlineData("-m0=deflate", SevenZipMethod.Deflate)]
    [InlineData("-m0=Deflate", SevenZipMethod.Deflate)]
    [InlineData("-m0=copy", SevenZipMethod.Copy)]
    [InlineData("-m0=COPY", SevenZipMethod.Copy)]
    public void Archive_MethodSwitch_NamesTheMethodWhateverTheCase(string methodSwitch, SevenZipMethod expected)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-t7z", methodSwitch, "out.7z", "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Archive);
        result.SevenZipMethod.Should().Be(expected);
    }

    [Fact]
    public void Archive_MethodSwitchBeforeTheType_IsAccepted()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-m0=bzip2", "out.7z", "file1.txt", "-t7z"]);

        result.Type.Should().Be(CliCommandType.Archive);
        result.SevenZipMethod.Should().Be(SevenZipMethod.BZip2);
    }

    [Fact]
    public void Archive_TwoMethodSwitches_TheLastOneWins()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-t7z", "-m0=bzip2", "-m0=deflate", "out.7z", "file1.txt"]);

        result.SevenZipMethod.Should().Be(SevenZipMethod.Deflate);
    }

    [Theory]
    [InlineData("-mx=0", CompressionLevel.NoCompression)]
    [InlineData("-mx=1", CompressionLevel.Fastest)]
    [InlineData("-mx=5", CompressionLevel.Optimal)]
    [InlineData("-mx=9", CompressionLevel.SmallestSize)]
    public void Archive_SevenZipWithALevel_UsesTheSameFourBuckets(string levelSwitch, CompressionLevel expected)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-t7z", levelSwitch, "out.7z", "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Archive);
        result.CompressionLevel.Should().Be(expected);
    }

    [Fact]
    public void Archive_SevenZipWithRecoveryDataAndStdout_AreEachAccepted()
    {
        CliArgumentParser.Parse(["a", "-t7z", "-rr5", "out.7z", "file1.txt"]).RecoveryPercent.Should().Be(5);
        CliArgumentParser.Parse(["a", "-t7z", "-so", "out.7z", "file1.txt"]).WriteToStdout.Should().BeTrue();
    }

    // The parser has no "--": a file named like a switch is given with its folder, as for every switch.
    [Fact]
    public void Archive_ASourceNamedLikeTheMethodSwitchGivenWithItsFolder_IsASource()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-t7z", "out.7z", @".\-m0=lzma"]);

        result.Type.Should().Be(CliCommandType.Archive);
        result.SourcePaths.Should().Equal(@".\-m0=lzma");
        result.SevenZipMethod.Should().BeNull();
    }

    [Theory]
    [InlineData("-m0", "-m0=")]
    [InlineData("-m0=", "-m0=")]
    [InlineData("-m0=lzma2:d=64m", "parameters")]
    [InlineData("-m0=ppmd", "tar.exe")]
    [InlineData("-m0=PPMD", "tar.exe")]
    [InlineData("-m0=zstd", "tar.exe")]
    [InlineData("-m0=brotli", "unknown -m0 value")]
    [InlineData("-m1=lzma", "-m1")]
    public void Archive_MethodSwitchPakkoCannotHonour_IsRefusedWithItsReason(string methodSwitch, string reason)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-t7z", methodSwitch, "out.7z", "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain(reason);
    }

    [Theory]
    [InlineData("out.zip")]
    [InlineData("-tzip", "out.zip")]
    [InlineData("-ttar.gz", "out.tar.gz")]
    public void Archive_MethodSwitchWithoutTheSevenZipType_IsRefused(params string[] typeAndName)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-m0=lzma2", .. typeAndName, "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("-m0").And.Contain("-t7z");
    }

    [Fact]
    public void Archive_ANameEndingInSevenZipWithoutAType_IsRefusedWithTheSwitchToAdd()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "backup.7z", "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("backup.7z").And.Contain("-t7z");
    }

    [Theory]
    [InlineData("-tzip", "out.7z")]
    [InlineData("-ttar", "out.7z")]
    [InlineData("-t7z", "out.zip")]
    [InlineData("-t7z", "out.tar.gz")]
    public void Archive_ATypeAndANameThatDisagree_AreRefused(string typeSwitch, string name)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", typeSwitch, name, "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain(name).And.Contain(typeSwitch);
    }

    [Theory]
    [InlineData("-p")]
    [InlineData("-pSecret-1")]
    public void Archive_APasswordForSevenZip_IsRefusedAsSevenZipEncryption(string passwordSwitch)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-t7z", passwordSwitch, "out.7z", "file1.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("7z").And.Contain("encrypt");
        result.ErrorMessage.Should().NotContain("tar-family", "7z has its own answer, not the tar one");
    }

    [Theory]
    [InlineData("x")]
    [InlineData("t")]
    [InlineData("l")]
    public void OtherCommands_MethodSwitch_IsNotSupportedThere(string command)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, "-m0=lzma2", "archive.7z"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("not supported on this command");
    }

    [Theory]
    [InlineData(true, "create, extract, list")]
    [InlineData(false, "extract, list")]
    public void Info_SevenZipLine_SaysCreateOnlyWhereTheWriterIsThere(bool writerAvailable, string expected)
    {
        CliInfoText.SevenZipActions(writerAvailable).Should().Be(expected);
    }

    // T-F403: the methods line lists what this tar.exe can write, from Core's one rule.
    [Fact]
    public void Info_SevenZipMethodsLine_ListsTheWritableMethods()
    {
        CliInfoText.SevenZipMethodsLine([SevenZipMethod.Lzma2, SevenZipMethod.Lzma, SevenZipMethod.Deflate, SevenZipMethod.Copy])
            .Should().Be("  methods for 7z: LZMA2, LZMA, Deflate, Copy");
    }
}
