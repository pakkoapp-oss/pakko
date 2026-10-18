using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F410: 7-Zip's name and archive filter switches as the parser takes them
// (ArchiveCommandLine.cpp, AddSwitchWildcardsToCensor; docs/DECISIONS.md's T-F410 entry).
public sealed class CliNameSwitchesTests
{
    [Theory]
    [InlineData("-xr!*.nsis", "*.nsis", false, false, CliRecursion.Recursive, null)]
    [InlineData("-ir!dir\\*", "dir\\*", true, false, CliRecursion.Recursive, null)]
    [InlineData("-x!a.txt", "a.txt", false, false, null, null)]
    [InlineData("-xr-!a.txt", "a.txt", false, false, CliRecursion.None, null)]
    [InlineData("-xr0!*.txt", "*.txt", false, false, CliRecursion.WildcardOnly, null)]
    [InlineData("-xw-!*.txt", "*.txt", false, false, null, false)]
    [InlineData("-XRW!*.txt", "*.txt", false, false, CliRecursion.Recursive, true)]
    [InlineData("-i@list.txt", "list.txt", true, true, null, null)]
    [InlineData("-xr@list.txt", "list.txt", false, true, CliRecursion.Recursive, null)]
    public void IncludeExclude_ReadsModifiersAndName(
        string token, string text, bool include, bool isListFile, CliRecursion? recursion, bool? wildcardMatching)
    {
        // -X with capitals: the switch letter is matched as typed (T-F411 owns capitals), so lower it.
        string typed = token[..2].ToLowerInvariant() + token[2..];
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "a.zip", typed]);

        result.Type.Should().Be(CliCommandType.Extract, result.ErrorMessage);
        result.NameSwitches.Names.Should().Equal(new CliWildcardSwitch(text, include, isListFile, recursion, wildcardMatching));
    }

    [Theory]
    [InlineData("-x")]
    [InlineData("-x!")]
    [InlineData("-xr")]
    [InlineData("-x?a.txt")]
    [InlineData("-xrr!a.txt")]
    [InlineData("-xm!a.txt")]
    [InlineData("-ai")]
    public void IncludeExclude_Malformed_IsACommandLineError(string token)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "a.zip", token]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("-r", CliRecursion.Recursive)]
    [InlineData("-r-", CliRecursion.None)]
    [InlineData("-r0", CliRecursion.WildcardOnly)]
    public void Recursion_TheLastOneWins(string token, CliRecursion expected)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-r", "a.zip", token]);

        result.NameSwitches.Recursion.Should().Be(expected);
    }

    [Fact]
    public void MatchingSwitches_AreTaken()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["l", "-spd", "-ssc", "-scsUTF-16LE", "-xtd", "-xtf", "a.zip"]);

        result.Type.Should().Be(CliCommandType.List, result.ErrorMessage);
        result.NameSwitches.Should().BeEquivalentTo(new
        {
            WildcardMatching = false, CaseSensitive = true, ListFileCharset = "UTF-16LE", ExcludeFolders = true, ExcludeFiles = true,
        });
    }

    [Fact]
    public void ListFileCharset_Unknown_IsACommandLineError() =>
        CliArgumentParser.Parse(["x", "-scsKOI8", "a.zip"]).Type.Should().Be(CliCommandType.Invalid);

    [Fact]
    public void ListFileName_BeforeDoubleDash_IsAList_AfterIt_IsAName()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "a.zip", "@names.txt", "--", "@literal"]);

        result.FilterNames.Should().Equal(new CliFilterName("@names.txt", true), new CliFilterName("@literal", false));
    }

    [Fact]
    public void NoArchiveName_TakesEveryNameAsAFilter()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-an", "-ai!*.zip", "dir"]);

        result.Type.Should().Be(CliCommandType.Extract, result.ErrorMessage);
        result.ArchivePaths.Should().BeEmpty();
        result.FilterNames.Should().Equal(new CliFilterName("dir", false));
        result.NameSwitches.Archives.Should().Equal(new CliWildcardSwitch("*.zip", true, false));
    }

    [Fact]
    public void NoArchiveName_WithoutArchiveInclude_IsACommandLineError() =>
        CliArgumentParser.Parse(["x", "-an", "dir"]).ErrorMessage.Should().Contain("-ai");

    [Fact]
    public void ArchiveSwitches_WithStdin_AreACommandLineError() =>
        CliArgumentParser.Parse(["x", "-si", "-ai!*.zip"]).ErrorMessage.Should().Contain("-si");

    [Fact]
    public void Test_TakesArchiveSwitches()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["t", "a.zip", "-ai!b.zip", "-ax!c.zip"]);

        result.Type.Should().Be(CliCommandType.Test, result.ErrorMessage);
        result.ArchivePaths.Should().Equal("a.zip");
    }

    [Theory]
    [InlineData("-x!*.nsis")]
    [InlineData("-r")]
    public void Test_NameFilter_IsTaken(string token) =>
        CliArgumentParser.Parse(["t", "a.zip", token]).Type.Should().Be(CliCommandType.Test);

    // T-F410: 'a' takes -x and -r since wave C (CliArgumentParserTests).
    [Theory]
    [InlineData("h", "-ssc")]
    [InlineData("h", "-x!*.tmp")]
    [InlineData("r", "-ai!*.zip")]
    public void NameSwitch_OnAnotherCommand_SaysWhereFiltersWork(string command, string token) =>
        CliArgumentParser.Parse([command, token, "out.zip", "file.txt"]).ErrorMessage.Should().Contain("name filters");

    [Fact]
    public void RecoveryData_OnArchiveCreation_IsNotARecursionSwitch()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["a", "-rr", "out.zip", "file.txt"]);

        result.Type.Should().Be(CliCommandType.Archive, result.ErrorMessage);
        result.RecoveryPercent.Should().Be(5);
    }

    // --- the real callers' lines (docs/CLI.md, "Real callers") ---

    [Fact]
    public void ScoopLine_IsAccepted()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "a.7z", "-oC:\\dest", "-xr!*.nsis", "-y", "-ir!dir\\*", "-aoa"]);

        result.Type.Should().Be(CliCommandType.Extract, result.ErrorMessage);
        result.ArchivePaths.Should().Equal("a.7z");
        result.NameSwitches.Names.Should().Equal(
            new CliWildcardSwitch("*.nsis", false, false, CliRecursion.Recursive),
            new CliWildcardSwitch("dir\\*", true, false, CliRecursion.Recursive));
    }

    [Fact]
    public void ChocolateyLine_WithAFolder_IsAccepted()
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["x", "-aoa", "-bd", "-bb1", "-oC:\\dest", "-y", "C:\\pkg\\a.zip", "tools"]);

        result.Type.Should().Be(CliCommandType.Extract, result.ErrorMessage);
        result.ArchivePaths.Should().Equal("C:\\pkg\\a.zip");
        result.FilterNames.Should().Equal(new CliFilterName("tools", false));
    }
}
