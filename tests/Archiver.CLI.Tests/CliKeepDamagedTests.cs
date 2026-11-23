using Archiver.CLI;
using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F496: -kb on `pakko x` keeps what a damaged ZIP entry holds, as name.damaged. Pakko's own
// switch (7-Zip has none on 'k'; WinRAR's -kb is the same idea), off unless given.
public sealed class CliKeepDamagedTests
{
    // --- Happy path ---

    [Fact]
    public void Extract_Kb_SetsTheChoice()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-kb", "a.zip"]);

        result.Type.Should().Be(CliCommandType.Extract);
        result.KeepDamagedFiles.Should().BeTrue();
    }

    [Fact]
    public void Extract_NoKb_KeepsNothing()
    {
        CliArgumentParser.Parse(["x", "a.zip"]).KeepDamagedFiles.Should().BeFalse();
    }

    // --- Misuse & Fool ---

    [Fact]
    public void Extract_KbTwice_IsAMultipleInstanceError()
    {
        CliArgumentParser.Parse(["x", "-kb", "-kb", "a.zip"]).ErrorMessage.Should().Be("Multiple instances for switch: -kb");
    }

    [Theory]
    [InlineData("-kb1")]
    [InlineData("-kb-")]
    public void Extract_KbWithAValue_IsRefused(string token)
    {
        CliArgumentParser.Parse(["x", token, "a.zip"]).ErrorMessage.Should().Contain("-kb takes no value");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("t")]
    [InlineData("l")]
    [InlineData("h")]
    public void OtherCommands_Kb_NotSupportedOnThisCommand(string command)
    {
        CliArgumentParser.Parse([command, "-kb", "a.zip"]).ErrorMessage.Should().Contain("not supported on this command").And.Contain("-kb");
    }

    // A kept file is told apart by its name, and -so writes none.
    [Fact]
    public void Extract_KbWithSo_IsRefused()
    {
        CliArgumentParser.Parse(["x", "-kb", "-so", "a.zip"]).ErrorMessage.Should().Contain("'-kb' cannot be combined with '-so'");
    }

    // --- Security & Boundary: the policy wins, and says so ---

    [Fact]
    public void Policy_ForbidsKb_Warns()
    {
        CliDownloadMark.KeepDamagedForbiddenWarning(true, new GroupPolicyOptions { DisableKeepDamagedFiles = true })
            .Should().Contain("DisableKeepDamagedFiles");
    }

    [Fact]
    public void ExtractPolicyWarnings_BothOverridden_NamesBothPolicies()
    {
        var policy = new GroupPolicyOptions { DisableKeepDamagedFiles = true, MotwMode = MotwMode.AllFiles, MotwModeSetByPolicy = true };

        string[] warnings = [.. CliDownloadMark.ExtractPolicyWarnings(applyDownloadMark: false, keepDamaged: true, policy)];

        warnings.Should().HaveCount(2);
        warnings[1].Should().Contain("DisableKeepDamagedFiles");
    }

    [Fact]
    public void ExtractPolicyWarnings_NothingOverridden_None()
    {
        CliDownloadMark.ExtractPolicyWarnings(null, keepDamaged: false, new GroupPolicyOptions()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Policy_NothingToOverride_NoWarning(bool requested, bool forbidden)
    {
        CliDownloadMark.KeepDamagedForbiddenWarning(requested, new GroupPolicyOptions { DisableKeepDamagedFiles = forbidden })
            .Should().BeNull();
    }
}
