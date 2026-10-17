using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F407, T-F409, T-F408, T-F411's -sae: what real callers of 7z.exe pass (patool, Chocolatey;
// docs/CLI.md, "7-Zip compatibility matrix"), as CliArgumentParser.Parse returns it.
public sealed class CliSevenZipCallerTests
{
    // --- T-F407: "--" ends the switches (7-Zip's CommandLineParser.cpp, ParseStrings) ---

    [Fact]
    public void Extract_NameAfterDoubleDash_IsAnArchiveEvenWithALeadingDash()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-y", "--", "-weird.zip"]);

        result.Type.Should().Be(CliCommandType.Extract, result.ErrorMessage);
        result.ArchivePaths.Should().Equal("-weird.zip");
        result.AssumeYes.Should().BeTrue();
    }

    [Fact]
    public void Extract_SwitchesBeforeDoubleDash_StillApply()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-y", "-aou", @"-oC:\dest", "--", "a.zip"]);

        result.Type.Should().Be(CliCommandType.Extract, result.ErrorMessage);
        result.OutputDirectory.Should().Be(@"C:\dest");
        result.ArchivePaths.Should().Equal("a.zip");
    }

    [Fact]
    public void Archive_NamesAfterDoubleDash_AreTheArchiveAndItsSources()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "--", "out.zip", "-file.txt", "--"]);

        result.Type.Should().Be(CliCommandType.Archive, result.ErrorMessage);
        result.ArchivePathArg.Should().Be("out.zip");
        result.SourcePaths.Should().Equal("-file.txt", "--");
    }

    [Theory]
    [InlineData("l", CliCommandType.List)]
    [InlineData("t", CliCommandType.Test)]
    [InlineData("h", CliCommandType.Hash)]
    [InlineData("r", CliCommandType.Repair)]
    public void EveryCommand_TakesANameAfterDoubleDash(string command, CliCommandType expected)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, "--", "-a.zip"]);

        result.Type.Should().Be(expected, result.ErrorMessage);
    }

    [Fact]
    public void Extract_DoubleDashWithNoName_StillNeedsAnArchive()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-y", "--"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("requires at least one archive path");
    }

    [Fact]
    public void ConsoleCharsetSwitch_AfterDoubleDash_IsAName()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "--", "-sccBOGUS"]);

        result.Type.Should().Be(CliCommandType.Extract, result.ErrorMessage);
        result.ArchivePaths.Should().Equal("-sccBOGUS");
    }

    // T-F294's split check guards switches; after "--" "-pX" and ".1" are two names.
    [Fact]
    public void ShellSplitCheck_AfterDoubleDash_DoesNotApply()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "--", "-pX", ".1"]);

        result.Type.Should().Be(CliCommandType.Extract, result.ErrorMessage);
        result.ArchivePaths.Should().Equal("-pX", ".1");
    }

    [Fact]
    public void Info_DoubleDash_TakesNoArguments()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["i", "--", "x"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("'i' does not accept any arguments");
    }
}
