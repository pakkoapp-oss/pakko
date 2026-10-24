using Archiver.CLI;
using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F413: 7-Zip's -tgzip, -tbzip2 and -txz compress one file without tar (patool's
// `a -txz -- <file>.xz <file>`). The names are 7-Zip's format names; 7za 26.02 refuses -tgz and
// -tbz2 ("Unsupported archive type"), so they stay unknown here too.
public sealed class CliSingleFileTypeTests
{
    private static ParsedCliCommand Parse(params string[] args) => CliArgumentParser.Parse(args);

    // --- Happy path ---

    [Theory]
    [InlineData("-tgzip", ArchiveContainerFormat.GZip)]
    [InlineData("-tGZIP", ArchiveContainerFormat.GZip)]
    [InlineData("-tbzip2", ArchiveContainerFormat.BZip2)]
    [InlineData("-txz", ArchiveContainerFormat.Xz)]
    [InlineData("-tXZ", ArchiveContainerFormat.Xz)]
    public void TypeSwitch_MapsToTheSingleFileFormat(string type, ArchiveContainerFormat expected)
    {
        ParsedCliCommand result = Parse("a", type, "out", "file1.txt");

        result.Type.Should().Be(CliCommandType.Archive, result.ErrorMessage);
        result.ArchiveFormat.Should().Be(expected);
    }

    // patool names the output; a gzip of a tar is a .tar.gz, as `7z a -tgzip x.tar.gz x.tar` makes.
    [Theory]
    [InlineData("-tgzip", "out.gz")]
    [InlineData("-tgzip", "OUT.GZ")]
    [InlineData("-tgzip", "x.tar.gz")]
    [InlineData("-tgzip", "x.tgz")]
    [InlineData("-tbzip2", "out.bz2")]
    [InlineData("-tbzip2", "x.tar.bz2")]
    [InlineData("-tbzip2", "x.tbz2")]
    [InlineData("-txz", "out.xz")]
    [InlineData("-txz", "x.tar.xz")]
    [InlineData("-txz", "x.txz")]
    [InlineData("-txz", "notes.txt.xz")]
    [InlineData("-tgzip", "backup.out")]
    public void NameAgreesWithTheType_IsAccepted(string type, string name) =>
        Parse("a", type, name, "file1.txt").Type.Should().Be(CliCommandType.Archive);

    // --- Misuse: the name says another format ---

    [Theory]
    [InlineData("-tgzip", "out.zip")]
    [InlineData("-tgzip", "out.7z")]
    [InlineData("-tgzip", "out.tar")]
    [InlineData("-tgzip", "out.tar.xz")]
    [InlineData("-txz", "out.tar.gz")]
    [InlineData("-tbzip2", "out.txz")]
    [InlineData("-tgzip", "out.xz")]
    [InlineData("-txz", "out.gz")]
    [InlineData("-tbzip2", "out.gz")]
    public void NameSaysAnotherFormat_IsACommandLineError(string type, string name)
    {
        ParsedCliCommand result = Parse("a", type, name, "file1.txt");

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain(name).And.Contain(type);
    }

    // T-F221 item 6 (the user's decision) stands: a bare .gz is no archive type of a name, so
    // another -t writes it as typed, and so does no -t at all.
    [Theory]
    [InlineData("-ttar", "out.gz")]
    [InlineData("-tzip", "out.xz")]
    [InlineData("-t7z", "out.bz2")]
    public void BareStreamExtension_WithAnArchiveType_IsWrittenAsTyped(string type, string name) =>
        Parse("a", type, name, "file1.txt").Type.Should().Be(CliCommandType.Archive);

    [Fact]
    public void BareStreamExtension_WithoutType_IsAZipAsBefore()
    {
        ParsedCliCommand result = Parse("a", "out.gz", "file1.txt");

        result.Type.Should().Be(CliCommandType.Archive);
        result.ArchiveFormat.Should().Be(ArchiveContainerFormat.Zip);
    }

    [Theory]
    [InlineData("-tgz")]
    [InlineData("-tbz2")]
    [InlineData("-tbzip")]
    public void SevenZipRefusedAliases_AreUnknown(string type)
    {
        ParsedCliCommand result = Parse("a", type, "out", "file1.txt");

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("unknown -t value").And.Contain("gzip, bzip2, xz");
    }

    [Theory]
    [InlineData("-tgzip")]
    [InlineData("-txz")]
    public void Password_IsACommandLineError(string type)
    {
        ParsedCliCommand result = Parse("a", type, "-psecret", "out", "file1.txt");

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("only ZIP archives can be password-protected");
    }

    // 7-Zip refuses two files at run time (exit 2, "The parameter is incorrect"); so does Core.
    [Fact]
    public void TwoSources_AreLeftToCore() =>
        Parse("a", "-tgzip", "out.gz", "a.txt", "b.txt").Type.Should().Be(CliCommandType.Archive);
}
