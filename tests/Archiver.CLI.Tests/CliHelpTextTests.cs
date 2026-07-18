using Archiver.CLI;
using FluentAssertions;

namespace Archiver.CLI.Tests;

public sealed class CliHelpTextTests
{
    [Theory]
    [InlineData("x")]
    [InlineData("t")]
    [InlineData("l")]
    [InlineData("a")]
    [InlineData("i")]
    public void Text_MentionsEveryCommandLetter(string command)
    {
        CliHelpText.Text.Should().Contain($"\n  {command}   ");
    }

    [Theory]
    [InlineData("-o<dir>")]
    [InlineData("-y")]
    [InlineData("-ao{a|s|u}")]
    [InlineData("-t<type>")]
    [InlineData("-mx=<0-9>")]
    [InlineData("-si")]
    [InlineData("-so")]
    public void Text_MentionsEverySupportedSwitch(string switchToken)
    {
        CliHelpText.Text.Should().Contain(switchToken);
    }

    // T-F116: PowerShell 5.1's native '|' silently corrupts binary data between two executables —
    // the shell-compatibility caveat must be documented in --help, not just CLI.md, since a user
    // typing -so/-si directly needs it before building a broken script.
    [Fact]
    public void Text_MentionsCmdWrapperForBinaryPipeCompatibility()
    {
        CliHelpText.Text.Should().Contain("cmd /c");
    }

    [Fact]
    public void Text_MentionsEveryDeliberatelyUnsupportedCommand()
    {
        foreach (string command in new[] { "u ", "d ", "rn ", "b ", "e " })
            CliHelpText.Text.Should().Contain(command);
    }

    // Guards the printed compression-level table against silent drift from the real mapper.
    [Fact]
    public void CompressionLevelTable_MatchesMapperOutputAtBoundaries()
    {
        CliHelpText.Text.Should().Contain("0    -> Store");
        CliHelpText.Text.Should().Contain("1-2  -> Fastest");
        CliHelpText.Text.Should().Contain("3-6 -> Optimal");
        CliHelpText.Text.Should().Contain("7-9 -> SmallestSize");
    }

    [Fact]
    public void Text_MentionsAllFourExitCodes()
    {
        CliHelpText.Text.Should().Contain("0 ok").And.Contain("1 ok with warnings")
            .And.Contain("2 operation failed").And.Contain("7 command-line error");
    }
}
