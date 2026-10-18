using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F410: 7-Zip's -stx{Type} (measured on 7za 26.02: an excluded type is "Cannot open the file as
// archive", exit 2; a type it does not know, "Unsupported archive type", exit 2; "-stx" alone,
// "Too short switch", exit 7).
public sealed class CliArchiveTypeExclusionTests
{
    [Theory]
    [InlineData("x")]
    [InlineData("l")]
    [InlineData("t")]
    public void Stx_IsTakenOnTheReadingCommands(string command)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, "a.zip", "-stxzip", "-stx7z"]);

        result.ErrorMessage.Should().BeNull();
        result.NameSwitches.ExcludedTypes.Should().Equal("zip", "7z");
    }

    [Fact]
    public void Stx_WithStdin_IsTaken() =>
        CliArgumentParser.Parse(["t", "-si", "-stxzip"]).ErrorMessage.Should().BeNull();

    [Fact]
    public void Stx_WithoutAType_IsTooShort()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["t", "a.zip", "-stx"]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("Too short switch");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("h")]
    [InlineData("r")]
    public void Stx_OnAnotherCommand_SaysWhereItWorks(string command) =>
        CliArgumentParser.Parse([command, "a.zip", "-stxzip"]).ErrorMessage.Should().Contain("-stx");

    [Theory]
    [InlineData("zip")]
    [InlineData("ZIP")]
    [InlineData("rar5")]
    public void FirstUnknown_KnownTypeInAnyCase_IsNull(string type) =>
        CliArchiveTypeExclusion.FirstUnknown([type]).Should().BeNull();

    [Theory]
    [InlineData("gz")]
    [InlineData("tgz")]
    [InlineData("cab")]
    public void FirstUnknown_AnotherName_IsIt(string type) =>
        CliArchiveTypeExclusion.FirstUnknown(["zip", type]).Should().Be(type);

    [Fact]
    public void Apply_NoExcludedType_KeepsEveryPathUnread()
    {
        string[] paths = ["missing-a.zip", "missing-b.zip"];

        (IReadOnlyList<string> kept, IReadOnlyList<string> refused) = CliArchiveTypeExclusion.Apply(paths, []);

        kept.Should().BeSameAs(paths);
        refused.Should().BeEmpty();
    }

    [Fact]
    public void Apply_MissingFile_IsKeptForTheCommandToReport()
    {
        (IReadOnlyList<string> kept, _) = CliArchiveTypeExclusion.Apply(["missing.zip"], ["zip"]);

        kept.Should().Equal("missing.zip");
    }
}
