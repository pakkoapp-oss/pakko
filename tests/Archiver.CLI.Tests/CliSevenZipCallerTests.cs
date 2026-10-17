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

    // --- T-F408: output switches (7-Zip's ArchiveCommandLine.cpp: ba, bd, bt, bb, bso, bse, bsp) ---

    [Fact]
    public void ChocolateyLine_IsAccepted()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-aoa", "-bd", "-bb1", @"-oC:\dest", "-y", "a.zip"]);

        result.Type.Should().Be(CliCommandType.Extract, result.ErrorMessage);
        result.Output.DisablePercents.Should().BeTrue();
        result.Output.LogLevel.Should().Be(1u);
    }

    [Fact]
    public void NoOutputSwitch_IsTheDefault()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["l", "a.zip"]);

        result.Output.Should().Be(new CliOutputOptions());
        result.Output.LogLevel.Should().Be(0u);
        result.Output.OutStream.Should().BeNull("not given: messages go where they always went");
        result.Output.ErrStream.Should().BeNull();
        result.Output.PercentStream.Should().BeNull();
    }

    [Theory]
    [InlineData("-bb", 1u)]
    [InlineData("-bb0", 0u)]
    [InlineData("-bb3", 3u)]
    [InlineData("-bb5", 5u)]
    public void LogLevel_TakesAnyNumber(string token, uint level)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", token, "a.zip"]);

        result.Type.Should().Be(CliCommandType.Extract, result.ErrorMessage);
        result.Output.LogLevel.Should().Be(level);
    }

    [Theory]
    [InlineData("-bbx")]
    [InlineData("-bb-1")]
    [InlineData("-bb99999999999")]
    public void LogLevel_NotANumber_IsRefusedAs7ZipDoes(string token)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", token, "a.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("Unsupported switch postfix -bb");
    }

    [Theory]
    [InlineData("-bso0", CliOutputTarget.Off, null, null)]
    [InlineData("-bso2", CliOutputTarget.StdErr, null, null)]
    [InlineData("-bse1", null, CliOutputTarget.StdOut, null)]
    [InlineData("-bse0", null, CliOutputTarget.Off, null)]
    [InlineData("-bsp0", null, null, CliOutputTarget.Off)]
    [InlineData("-bsp1", null, null, CliOutputTarget.StdOut)]
    public void StreamSwitch_ChoosesTheStream(string token, CliOutputTarget? output, CliOutputTarget? errors, CliOutputTarget? percent)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["t", token, "a.zip"]);

        result.Type.Should().Be(CliCommandType.Test, result.ErrorMessage);
        result.Output.OutStream.Should().Be(output);
        result.Output.ErrStream.Should().Be(errors);
        result.Output.PercentStream.Should().Be(percent);
    }

    [Theory]
    [InlineData("-bso")]
    [InlineData("-bso3")]
    [InlineData("-bse12")]
    public void StreamSwitch_WithoutOneOf012_IsRefused(string token)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["t", token, "a.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain(token[..4]).And.Contain("0, 1 or 2");
    }

    [Theory]
    [InlineData("h")]
    [InlineData("a")]
    [InlineData("r")]
    [InlineData("i")]
    public void OutputSwitches_ApplyToEveryCommand(string command)
    {
        string[] args = command switch
        {
            "a" => ["a", "-ba", "-bt", "-bd", "-bb2", "out.zip", "f"],
            "i" => ["i", "-ba", "-bt", "-bd", "-bb2"],
            _ => [command, "-ba", "-bt", "-bd", "-bb2", "a.zip"],
        };

        ParsedCliCommand result = CliArgumentParser.Parse(args);

        result.Type.Should().NotBe(CliCommandType.Invalid, result.ErrorMessage);
        result.Output.Should().Be(new CliOutputOptions { DisableHeaders = true, ShowTime = true, DisablePercents = true, LogLevel = 2 });
    }

    [Fact]
    public void OutputSwitch_LastOneWins()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-bb3", "-bb0", "-bso0", "-bso1", "a.zip"]);

        result.Output.LogLevel.Should().Be(0u);
        result.Output.OutStream.Should().Be(CliOutputTarget.StdOut);
    }

    [Fact]
    public void OutputSwitch_AfterDoubleDash_IsAName()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "--", "-bb1"]);

        result.ArchivePaths.Should().Equal("-bb1");
        result.Output.LogLevel.Should().Be(0u);
    }

    // 7-Zip turns stdout messages off under -so (StdOutMode); asking for them there would put text
    // into the archive's bytes.
    [Theory]
    [InlineData("x", "a.zip")]
    [InlineData("a", "out.zip")]
    public void StdOutData_WithMessagesOnStdOut_IsRefused(string command, string name)
    {
        string[] args = command == "a" ? ["a", "-so", "-bso1", name, "f"] : [command, "-so", "-bso1", name];

        ParsedCliCommand result = CliArgumentParser.Parse(args);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("-bso1").And.Contain("-so");
    }

    [Fact]
    public void StdOutData_WithErrorsOnStdOut_IsRefused()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-so", "-bse1", "a.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("-bse1").And.Contain("-so");
    }

    [Theory]
    [InlineData(null, null, false, "out", "err")]
    [InlineData(null, null, true, "null", "err")]
    [InlineData(CliOutputTarget.StdErr, null, true, "err", "err")]
    [InlineData(CliOutputTarget.Off, CliOutputTarget.Off, false, "null", "null")]
    [InlineData(CliOutputTarget.StdErr, CliOutputTarget.StdOut, false, "err", "out")]
    public void Writers_FollowTheStreamSwitches(CliOutputTarget? messages, CliOutputTarget? errors, bool stdoutCarriesData, string expectedMessages, string expectedErrors)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var options = new CliOutputOptions { OutStream = messages, ErrStream = errors };

        (TextWriter m, TextWriter e) = options.Writers(stdout, stderr, stdoutCarriesData);

        Name(m).Should().Be(expectedMessages);
        Name(e).Should().Be(expectedErrors);

        string Name(TextWriter w) => w switch
        {
            _ when ReferenceEquals(w, stdout) => "out",
            _ when ReferenceEquals(w, stderr) => "err",
            _ when ReferenceEquals(w, TextWriter.Null) => "null",
            _ => "?",
        };
    }

    [Fact]
    public void Info_DoubleDash_TakesNoArguments()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["i", "--", "x"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("'i' does not accept any arguments");
    }
}
