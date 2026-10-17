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

    // --- T-F409: -y, -p and -p- on l and t (patool: "l -y -p- -- a.zip", "t -y -p- -- a.zip") ---

    [Theory]
    [InlineData("l", CliCommandType.List)]
    [InlineData("t", CliCommandType.Test)]
    public void PatoolLine_IsAccepted(string command, CliCommandType expected)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, "-y", "-p-", "--", "a.zip"]);

        result.Type.Should().Be(expected, result.ErrorMessage);
        result.ArchivePaths.Should().Equal("a.zip");
        result.AssumeYes.Should().BeTrue();
        result.Password.Should().Be("-", "7-Zip reads -p- as the password '-' (ArchiveCommandLine.cpp, kPassword)");
    }

    [Theory]
    [InlineData("-pSecret", "Secret", false)]
    [InlineData("-p", null, true)]
    public void List_TakesAPassword(string token, string? password, bool prompt)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["l", token, "a.zip"]);

        result.Type.Should().Be(CliCommandType.List, result.ErrorMessage);
        result.Password.Should().Be(password);
        result.PromptForPassword.Should().Be(prompt);
    }

    [Fact]
    public void List_BarePasswordWithStdin_IsRefused()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["l", "-p", "-si"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("a bare '-p' cannot be combined with '-si'");
    }

    [Fact]
    public void Hash_Password_IsStillNotOnThisCommand()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["h", "-pX", "a.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("not supported on this command").And.Contain("'l' (list)");
    }

    // --- T-F411: -sae / -sas (7-Zip's ArcNameMode; patool: "a -y -t7z -sae -- <arc> <files>") ---

    [Theory]
    [InlineData(new[] { "a", "-sae", "out", "f" }, true)]
    [InlineData(new[] { "a", "-sas", "out", "f" }, false)]
    [InlineData(new[] { "a", "-sae", "-sas", "out", "f" }, false)]
    [InlineData(new[] { "a", "out", "f" }, false)]
    public void Archive_NameMode_IsTaken(string[] args, bool exact)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(args);

        result.Type.Should().Be(CliCommandType.Archive, result.ErrorMessage);
        result.ArchiveNameExact.Should().Be(exact);
    }

    [Fact]
    public void Archive_PatoolLine_IsAccepted()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-y", "-t7z", "-sae", "--", "out.7z", "f.txt"]);

        result.Type.Should().Be(CliCommandType.Archive, result.ErrorMessage);
        result.ArchivePathArg.Should().Be("out.7z");
        result.ArchiveNameExact.Should().BeTrue();
    }

    [Theory]
    [InlineData("-saa", "not supported by Pakko: -saa")]
    [InlineData("-sax", "unknown -sa value")]
    [InlineData("-sa", "unknown -sa value")]
    public void Archive_OtherNameModes_AreRefused(string token, string reason)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", token, "out", "f"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain(reason);
    }

    [Fact]
    public void Extract_NameMode_IsNotOnThisCommand()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-sae", "a.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("not supported on this command");
    }

    [Fact]
    public void Info_DoubleDash_TakesNoArguments()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["i", "--", "x"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("'i' does not accept any arguments");
    }
}
