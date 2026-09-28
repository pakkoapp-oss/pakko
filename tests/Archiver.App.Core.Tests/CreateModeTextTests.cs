using System.IO.Compression;
using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F199 step 4: the create-mode words that follow the list and the options (mockup boards 1, 2, 8).
public sealed class CreateModeTextTests
{
    [Fact]
    public void DestinationLabel_FollowsTheAccent()
    {
        CreateModeText.DestinationLabelKey(PrimaryAction.Compress).Should().Be("DestinationSaveLabel");
        CreateModeText.DestinationLabelKey(PrimaryAction.Extract).Should().Be("DestinationExtractLabel");
    }

    [Fact]
    public void DeleteAfter_SaysWhatGoesToTheRecycleBin()
    {
        CreateModeText.DeleteAfterKey(PrimaryAction.Compress).Should().Be("DeleteAfterCompressLabel");
        CreateModeText.DeleteAfterKey(PrimaryAction.Extract).Should().Be("DeleteAfterExtractLabel");
    }

    [Theory]
    [InlineData(ArchiveContainerFormat.Zip, "ZIP")]
    [InlineData(ArchiveContainerFormat.Tar, "TAR")]
    [InlineData(ArchiveContainerFormat.TarGz, "TAR.GZ")]
    [InlineData(ArchiveContainerFormat.TarLzma, "TAR.LZMA")]
    public void FormatName_IsTheExtensionInCapitals(ArchiveContainerFormat format, string expected) =>
        CreateModeText.FormatName(format).Should().Be(expected);

    // T-F264: the hint is the name Core writes when the box is blank, not a second rule.
    [Fact]
    public void AutoName_SingleArchive_IsCoresDefaultNamePlusExtension()
    {
        CreateModeText.AutoName([@"D:\Reports\2026\Report.docx"], ArchiveMode.SingleArchive, ArchiveContainerFormat.Zip)
            .Should().Be("Report.zip");
        CreateModeText.AutoName([@"D:\Reports\s2", @"D:\Reports\s4"], ArchiveMode.SingleArchive, ArchiveContainerFormat.TarGz)
            .Should().Be("Reports.tar.gz");
    }

    [Fact]
    public void AutoName_SeparateArchives_IsTheFirstItemsOwnArchive()
    {
        CreateModeText.AutoName([@"D:\Reports\a.txt", @"D:\Reports\b.txt"], ArchiveMode.SeparateArchives, ArchiveContainerFormat.Zip)
            .Should().Be("a.zip");
    }

    [Fact]
    public void AutoName_EmptyList_IsCoresFallback() =>
        CreateModeText.AutoName([], ArchiveMode.SingleArchive, ArchiveContainerFormat.Zip).Should().Be("archive.zip");

    [Fact]
    public void Summary_Zip_NamesCompressionAndPassword()
    {
        CreateModeText.SummaryKeys(ArchiveContainerFormat.Zip, CompressionLevel.Optimal, encrypt: false)
            .Should().Equal("SummaryCompressionNormal", "SummaryNoPassword");
        CreateModeText.SummaryKeys(ArchiveContainerFormat.Zip, CompressionLevel.SmallestSize, encrypt: true)
            .Should().Equal("SummaryCompressionBest", "SummaryWithPassword");
    }

    // A tar format cannot encrypt, and plain tar has no compression level (T-F105) — neither is claimed.
    [Fact]
    public void Summary_Tar_ClaimsNoPasswordState_AndPlainTarNoCompression()
    {
        CreateModeText.SummaryKeys(ArchiveContainerFormat.TarGz, CompressionLevel.Fastest, encrypt: true)
            .Should().Equal("SummaryCompressionFast");
        CreateModeText.SummaryKeys(ArchiveContainerFormat.Tar, CompressionLevel.Optimal, encrypt: true)
            .Should().BeEmpty();
    }

    [Fact]
    public void Summary_NoCompression_HasItsOwnWord() =>
        CreateModeText.SummaryKeys(ArchiveContainerFormat.Zip, CompressionLevel.NoCompression, encrypt: false)
            .Should().Equal("SummaryCompressionNone", "SummaryNoPassword");
}
