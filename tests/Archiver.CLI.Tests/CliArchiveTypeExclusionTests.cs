using Archiver.Core.Models;
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
    [InlineData("wim")]
    [InlineData("udf")]
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

    // T-F442: 7-Zip's names for the read-only formats tar.exe opens.
    [Theory]
    [InlineData("iso", ArchiveFormat.Iso)]
    [InlineData("cab", ArchiveFormat.Cab)]
    [InlineData("cpio", ArchiveFormat.Cpio)]
    [InlineData("ar", ArchiveFormat.Ar)]
    [InlineData("Z", ArchiveFormat.Compress)]
    [InlineData("lzip", ArchiveFormat.Lzip)]
    public void ReadOnlyTarFormats_HaveSevenZipsNames(string type, ArchiveFormat format)
    {
        CliArchiveTypeExclusion.FirstUnknown([type]).Should().BeNull();
        CliArchiveTypeExclusion.TypeName(format).Should().Be(type);
    }

    [Fact]
    public void Apply_ExcludedCab_IsLeftOutByItsBytes()
    {
        string dir = Directory.CreateTempSubdirectory("pakko-stx-").FullName;
        try
        {
            string cab = Path.Combine(dir, "named.zip");
            byte[] head = new byte[64];
            "MSCF"u8.CopyTo(head);
            head[24] = 3;
            head[25] = 1;
            File.WriteAllBytes(cab, head);

            (IReadOnlyList<string> kept, IReadOnlyList<string> refused) = CliArchiveTypeExclusion.Apply([cab], ["cab"]);

            kept.Should().BeEmpty();
            refused.Should().Equal(cab);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Apply_MissingFile_IsKeptForTheCommandToReport()
    {
        (IReadOnlyList<string> kept, _) = CliArchiveTypeExclusion.Apply(["missing.zip"], ["zip"]);

        kept.Should().Equal("missing.zip");
    }
}
