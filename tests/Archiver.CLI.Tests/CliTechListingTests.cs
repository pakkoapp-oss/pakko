using Archiver.CLI;
using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.CLI.Tests;

/// <summary>
/// T-F412: "l -slt" prints 7-Zip's technical listing (CPP/7zip/UI/Console/List.cpp, 26.02): the
/// archive block, "----------", then one "Name = value" block per entry with a blank line after
/// it. Only the fields 7-Zip prints for the format and Pakko knows, in 7-Zip's order; a value
/// Pakko does not have is left empty, as 7-Zip leaves it ("CRC = " for a folder).
/// </summary>
public sealed class CliTechListingTests
{
    private static readonly DateTime When = new(2026, 10, 24, 16, 57, 47, DateTimeKind.Local);

    [Fact]
    public void ArchiveHeader_IsSevenZipsBlock()
    {
        CliTechListing.ArchiveHeader(@"C:\d\s.zip", ArchiveFormat.Zip, 428, terminal: false).Should().Equal(
            "", @"Listing archive: C:\d\s.zip", "", "--", @"Path = C:\d\s.zip", "Type = zip", "Physical Size = 428", "", "----------");
    }

    [Theory]
    [InlineData(ArchiveFormat.Zip, "zip")]
    [InlineData(ArchiveFormat.SevenZip, "7z")]
    [InlineData(ArchiveFormat.Tar, "tar")]
    [InlineData(ArchiveFormat.GZip, "gzip")]
    [InlineData(ArchiveFormat.Bz2, "bzip2")]
    [InlineData(ArchiveFormat.Xz, "xz")]
    [InlineData(ArchiveFormat.Zstd, "zstd")]
    [InlineData(ArchiveFormat.Lzma, "lzma")]
    [InlineData(ArchiveFormat.Rar, "rar")]
    public void ArchiveHeader_NamesTheOuterFormatAsSevenZipDoes(ArchiveFormat format, string type)
    {
        CliTechListing.ArchiveHeader("a", format, 1, terminal: false).Should().Contain("Type = " + type);
    }

    [Fact]
    public void ZipFile_PrintsPathFolderSizesModifiedEncryptedCrc()
    {
        var entry = new ArchiveEntryInfo
        {
            Path = "src/a.txt", Size = 5, CompressedSize = 5, Crc32 = 0x3610A686, Modified = When, Encryption = EntryEncryption.None,
        };

        CliTechListing.EntryBlock(entry, ArchiveFormat.Zip, terminal: false).Should().Equal(
            @"Path = src\a.txt", "Folder = -", "Size = 5", "Packed Size = 5", "Modified = 2026-10-24 16:57:47",
            "Encrypted = -", "CRC = 3610A686", "");
    }

    [Fact]
    public void ZipFolder_LeavesTheCrcEmpty_AndAnEncryptedFileIsPlus()
    {
        var folder = new ArchiveEntryInfo { Path = "src/sub", IsDirectory = true, CompressedSize = 0, Modified = When, Encryption = EntryEncryption.None };
        var secret = new ArchiveEntryInfo { Path = "s.txt", Size = 1, CompressedSize = 40, Crc32 = 0, Modified = When, Encryption = EntryEncryption.Aes256 };

        CliTechListing.EntryBlock(folder, ArchiveFormat.Zip, terminal: false).Should().Contain(["Folder = +", "CRC = "]);
        CliTechListing.EntryBlock(secret, ArchiveFormat.Zip, terminal: false).Should().Contain(["Encrypted = +", "CRC = 00000000"]);
    }

    [Theory]
    [InlineData(ArchiveFormat.Tar)]
    [InlineData(ArchiveFormat.GZip)]
    [InlineData(ArchiveFormat.Rar)]
    public void TarFamilyEntry_PrintsPathFolderSizeModified_ToTheMinute(ArchiveFormat format)
    {
        var entry = new ArchiveEntryInfo { Path = "sub/b.txt", Size = 5, Modified = When };

        CliTechListing.EntryBlock(entry, format, terminal: false).Should().Equal(
            @"Path = sub\b.txt", "Folder = -", "Size = 5", "Modified = 2026-10-24 16:57", "");
    }

    [Fact]
    public void TarEntryWithTheDateOnly_PrintsTheDay_AndAnUnreadableDateIsEmpty()
    {
        var old = new ArchiveEntryInfo { Path = "o", Modified = new DateTime(2020, 2, 3), ModifiedHasTime = false };
        var unknown = new ArchiveEntryInfo { Path = "u", Modified = null };

        CliTechListing.EntryBlock(old, ArchiveFormat.Tar, terminal: false).Should().Contain("Modified = 2020-02-03");
        CliTechListing.EntryBlock(unknown, ArchiveFormat.Tar, terminal: false).Should().Contain("Modified = ");
    }

    [Fact]
    public void SevenZipEntry_HasNoFolderField()
    {
        // 7-Zip's 7z handler lists Path, Size, Packed Size, Modified, Attributes, CRC, Encrypted,
        // Method, Block; through tar.exe Pakko knows Path, Size and Modified.
        var entry = new ArchiveEntryInfo { Path = "src/a.txt", Size = 5, Modified = When };

        CliTechListing.EntryBlock(entry, ArchiveFormat.SevenZip, terminal: false).Should().Equal(
            @"Path = src\a.txt", "Size = 5", "Modified = 2026-10-24 16:57", "");
    }

    [Fact]
    public void Path_LineBreaks_AreReplaced_AsSevenZipDoesForRedirectedOutput()
    {
        var entry = new ArchiveEntryInfo { Path = "nl" + (char)0x0A + "cr" + (char)0x0D + "tab" + (char)0x09 + "x", Modified = When };

        CliTechListing.EntryBlock(entry, ArchiveFormat.Tar, terminal: false)[0].Should().Be("Path = nl_cr_tab" + (char)0x09 + "x");
    }

    [Theory]
    [InlineData("-slt")]
    [InlineData("-SLT")]
    public void Parse_SltOnList_AsksForTheTechnicalListing(string token)
    {
        ParsedCliCommand result = CliArgumentParser.Parse(["l", token, "a.zip"]);

        result.Type.Should().Be(CliCommandType.List, result.ErrorMessage);
        result.TechListing.Should().BeTrue();
        CliArgumentParser.Parse(["l", "a.zip"]).TechListing.Should().BeFalse();
    }

    [Theory]
    [InlineData("x", "a.zip", "-slt")]
    [InlineData("t", "a.zip", "-slt")]
    [InlineData("l", "a.zip", "-slt1")]
    [InlineData("l", "a.zip", "-slt-")]
    public void Parse_SltElsewhereOrWithASuffix_IsRefused(string command, string archive, string token)
    {
        ParsedCliCommand result = CliArgumentParser.Parse([command, archive, token]);

        result.Type.Should().Be(CliCommandType.Invalid);
        result.ErrorMessage.Should().Contain("-slt");
    }

    [Theory]
    [InlineData(0x09)]
    [InlineData(0x1B)]
    [InlineData(0x7F)]
    [InlineData(0x9B)]
    [InlineData(0x202E)]
    [InlineData(0x2066)]
    public void ForConsole_OnATerminal_ReplacesTerminalControls(int code)
    {
        CliTechListing.ForConsole("a" + (char)code + "b", terminal: true).Should().Be("a_b");
    }

    [Theory]
    [InlineData(0x7E)]
    [InlineData(0xA0)]
    [InlineData(0x2029)]
    [InlineData(0x2065)]
    public void ForConsole_OnATerminal_KeepsPrintableCharacters(int code)
    {
        string text = "a" + (char)code + "b";
        CliTechListing.ForConsole(text, terminal: true).Should().Be(text);
    }
}
