using Archiver.CLI;
using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F360: 7-Zip's -snz[0|1|2] on `pakko x` - whether the extracted files get the archive's
// download mark. Group Policy (EnforceMOTW) wins, with a warning when it overrides the switch.
public sealed class CliDownloadMarkTests
{
    // --- Happy path ---

    [Theory]
    [InlineData("-snz", true)]
    [InlineData("-snz1", true)]
    [InlineData("-snz0", false)]
    public void Extract_SnzSwitch_SetsTheChoice(string token, bool expected)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", token, "a.zip"]);

        result.Type.Should().Be(CliCommandType.Extract);
        result.ApplyDownloadMark.Should().Be(expected);
    }

    [Fact]
    public void Extract_NoSnz_NoChoice()
    {
        CliArgumentParser.Parse(["x", "a.zip"]).ApplyDownloadMark.Should().BeNull();
    }

    [Fact]
    public void Extract_LastSnzWins()
    {
        CliArgumentParser.Parse(["x", "-snz0", "-snz", "a.zip"]).ApplyDownloadMark.Should().BeTrue();
    }

    // --- Misuse & Fool ---

    [Fact]
    public void Extract_Snz2_RejectedAsNotSupported()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-snz2", "a.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("not supported by Pakko");
    }

    [Theory]
    [InlineData("-snz3")]
    [InlineData("-snzx")]
    [InlineData("-snz00")]
    public void Extract_UnknownSnzValue_Rejected(string token)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", token, "a.zip"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("-snz");
    }

    [Theory]
    [InlineData("t", "a.zip")]
    [InlineData("l", "a.zip")]
    [InlineData("a", "out.zip")]
    [InlineData("h", "file.txt")]
    public void OtherCommands_Snz_NotSupportedOnThisCommand(string command, string path)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, "-snz0", path]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("not supported on this command");
    }

    // --- Security & Boundary: the policy wins, and says so ---

    private static readonly GroupPolicyOptions NoPolicy = new();
    private static readonly GroupPolicyOptions PolicyMarks = new() { MotwMode = MotwMode.AllFiles, MotwModeSetByPolicy = true };
    private static readonly GroupPolicyOptions PolicyNoMark = new() { MotwMode = MotwMode.Disabled, MotwModeSetByPolicy = true };
    private static readonly GroupPolicyOptions PolicyUnsafeOnly = new() { MotwMode = MotwMode.UnsafeExtensionsOnly, MotwModeSetByPolicy = true };

    [Fact]
    public void Warning_NoSwitchOrNoPolicy_None()
    {
        CliDownloadMark.PolicyOverrideWarning(null, PolicyMarks).Should().BeNull();
        CliDownloadMark.PolicyOverrideWarning(false, NoPolicy).Should().BeNull();
        CliDownloadMark.PolicyOverrideWarning(true, NoPolicy).Should().BeNull();
    }

    [Fact]
    public void Warning_SwitchAgreesWithPolicy_None()
    {
        CliDownloadMark.PolicyOverrideWarning(true, PolicyMarks).Should().BeNull();
        CliDownloadMark.PolicyOverrideWarning(false, PolicyNoMark).Should().BeNull();
    }

    [Theory]
    [InlineData(false, "marks")]
    [InlineData(true, "no mark")]
    [InlineData(true, "unsafe")]
    public void Warning_PolicyOverridesTheSwitch_NamesThePolicy(bool requested, string policyName)
    {
        GroupPolicyOptions policy = policyName switch
        {
            "marks" => PolicyMarks,
            "no mark" => PolicyNoMark,
            _ => PolicyUnsafeOnly,
        };

        string? warning = CliDownloadMark.PolicyOverrideWarning(requested, policy);

        warning.Should().StartWith("pakko: warning: ").And.Contain("Group Policy").And.Contain("-snz");
    }
}
