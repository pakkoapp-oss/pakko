using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F411, wave E: 7-Zip's switches that Core carries out (-w, -ssp, -mmt, -stl, -ssw, -sse, -spe,
// -aot, -sdel), as CliArgumentParser.Parse returns them. Measured against 7za 26.02 (docs/DECISIONS.md,
// "T-F411, wave E").
public sealed class CliCoreSwitchesTests
{
    // --- -w: 7-Zip's working folder (ArchiveCommandLine.cpp: an empty one is the system's temp) ---

    [Theory]
    [InlineData(@"-wC:\work", @"C:\work")]
    [InlineData("-w", "")]
    [InlineData(@"-Wd:\Mixed Case", @"d:\Mixed Case")]
    public void Archive_WorkingFolder_IsTaken(string token, string folder)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", token, "out.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Archive, result.ErrorMessage);
        result.WorkingFolder.Should().Be(folder);
    }

    [Fact]
    public void Archive_NoWorkingFolder_IsNull()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "out.zip", "f.txt"]);

        result.WorkingFolder.Should().BeNull();
    }

    [Fact]
    public void Archive_WorkingFolderTwice_IsAMultipleInstanceError()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-wa", "-wb", "out.zip", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("Multiple instances for switch");
    }

    [Theory]
    [InlineData("x")]
    [InlineData("t")]
    [InlineData("l")]
    public void WorkingFolder_OnACommandThatReadsAnArchive_IsNotOnThisCommand(string command)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, "-wC:\\work", "a.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("not supported on this command");
    }

    // --- -ssp: declined (Pakko cannot keep the sources' last access time through tar.exe) ---

    [Theory]
    [InlineData("a", "out.zip")]
    [InlineData("x", "a.zip")]
    public void PreserveAccessTime_IsDeclined(string command, string archive)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, "-ssp", archive, "f.txt"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("not supported by Pakko: -ssp");
    }
}
