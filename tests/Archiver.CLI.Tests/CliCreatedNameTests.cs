using Archiver.CLI;
using FluentAssertions;

namespace Archiver.CLI.Tests;

/// <summary>
/// T-F325: 'a' never renames on its own (a taken name is an error, or -y replaces it), so an
/// archive that landed under another name means the name was taken while this run was writing
/// (T-F321) - and a script that uses the name it passed must be told.
/// </summary>
public sealed class CliCreatedNameTests
{
    [Fact]
    public void LandedUnderTheRequestedName_NoLine()
    {
        CliCreatedName.TakenLine(@"C:\out\docs.zip", [@"C:\out\docs.zip"]).Should().BeNull();
    }

    [Fact]
    public void LandedUnderANumberedName_SaysWhereAndWhy()
    {
        CliCreatedName.TakenLine(@"C:\out\docs.zip", [@"C:\out\docs (1).zip"]).Should()
            .Be("pakko: warning: created 'docs (1).zip': the name 'docs.zip' was taken while compressing");
    }

    // T-F421: Core numbers an archive whose name is one of its own sources; nobody else took it.
    [Fact]
    public void RequestedNameIsASource_SaysSoInsteadOfAnotherRun()
    {
        CliCreatedName.TakenLine(@"C:\out\a.txt", [@"C:\out\a (1).txt"], [@"C:\OUT\A.TXT", @"C:\out\b.txt"]).Should()
            .Be("pakko: warning: created 'a (1).txt': the name 'a.txt' is one of the files being archived");
    }

    [Fact]
    public void RequestedNameIsNoSource_StillSaysAnotherRunTookIt()
    {
        CliCreatedName.TakenLine(@"C:\out\docs.zip", [@"C:\out\docs (1).zip"], [@"C:\out\b.txt"]).Should()
            .Be("pakko: warning: created 'docs (1).zip': the name 'docs.zip' was taken while compressing");
    }

    [Fact]
    public void NameDiffersOnlyByCase_NoLine()
    {
        CliCreatedName.TakenLine(@"C:\out\Docs.ZIP", [@"C:\out\docs.zip"]).Should().BeNull();
    }

    // Windows drops a trailing dot or space from a file name; that is not a rename.
    [Theory]
    [InlineData(@"C:\out\docs.zip.")]
    [InlineData(@"C:\out\docs.zip ")]
    public void NameDiffersOnlyByWhatWindowsTrims_NoLine(string requested)
    {
        CliCreatedName.TakenLine(requested, [@"C:\out\docs.zip"]).Should().BeNull();
    }

    // T-F419: Core reports the created path as it was given, trailing dot and all.
    [Theory]
    [InlineData(@"C:\out\docs.zip.")]
    [InlineData(@"C:\out\docs.zip ")]
    public void CreatedPathKeepsWhatWindowsTrims_NoLine(string path)
    {
        CliCreatedName.TakenLine(path, [path]).Should().BeNull();
    }

    [Fact]
    public void NothingCreated_NoLine()
    {
        CliCreatedName.TakenLine(@"C:\out\docs.zip", []).Should().BeNull();
    }
}
