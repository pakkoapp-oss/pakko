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

        row.Should().Be("12345\t4321\ta1b2c3d4\t2026-07-18T14:03:02\tf\t?\tdocs/readme.txt");
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

        row.Should().Be("0\t0\t-\t-\td\t?\tdocs/");
    }

    [Fact]
    public void FormatRow_UnknownCompressedSize_RendersDash()
    {
        // T-F214: tar-family entries have no per-entry packed size.
        var entry = new ArchiveEntryInfo { Path = "a.txt", Size = 5, CompressedSize = null };

        CliEntryFormatter.FormatRow(entry).Should().StartWith("5	-	");
    }

    [Fact]
    public void Header_HasSevenTabSeparatedColumnsWithPathLast()
    {
        CliEntryFormatter.Header.Split('\t').Should().HaveCount(7).And.EndWith("Path");
    }

    // T-F221 item 7 (T-F199): 7-Zip marks encrypted entries; '?' when the format cannot say.
    [Theory]
    [InlineData(EntryEncryption.None, "-")]
    [InlineData(EntryEncryption.ZipCrypto, "ZipCrypto")]
    [InlineData(EntryEncryption.Aes128, "AES-128")]
    [InlineData(EntryEncryption.Aes192, "AES-192")]
    [InlineData(EntryEncryption.Aes256, "AES-256")]
    [InlineData(EntryEncryption.Unknown, "+")]
    [InlineData(null, "?")]
    public void FormatRow_EncryptionColumn(EntryEncryption? encryption, string expected)
    {
        var entry = new ArchiveEntryInfo { Path = "a.txt", Encryption = encryption };

        CliEntryFormatter.FormatRow(entry).Split('\t')[5].Should().Be(expected);
    }
}
