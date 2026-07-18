using Archiver.CLI;
using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.CLI.Tests;

public sealed class CliEntryFormatterTests
{
    [Fact]
    public void FormatRow_FileWithCrcAndModified_RendersAllFields()
    {
        var entry = new ArchiveEntryInfo
        {
            Path = "docs/readme.txt",
            Size = 12345,
            CompressedSize = 4321,
            Crc32 = 0xA1B2C3D4,
            Modified = new DateTime(2026, 7, 18, 14, 3, 2, DateTimeKind.Utc),
            IsDirectory = false,
        };

        string row = CliEntryFormatter.FormatRow(entry);

        row.Should().Be("12345\t4321\ta1b2c3d4\t2026-07-18T14:03:02\tf\tdocs/readme.txt");
    }

    [Fact]
    public void FormatRow_DirectoryWithNullCrcAndModified_RendersDashSentinels()
    {
        var entry = new ArchiveEntryInfo
        {
            Path = "docs/",
            Size = 0,
            CompressedSize = 0,
            Crc32 = null,
            Modified = null,
            IsDirectory = true,
        };

        string row = CliEntryFormatter.FormatRow(entry);

        row.Should().Be("0\t0\t-\t-\td\tdocs/");
    }

    [Fact]
    public void Header_HasSixTabSeparatedColumns()
    {
        CliEntryFormatter.Header.Split('\t').Should().HaveCount(6);
    }
}
