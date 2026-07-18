using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

public sealed class ArchiveFormatRegistryNamesTests
{
    [Theory]
    [InlineData(ArchiveFormat.Zip, "zip")]
    [InlineData(ArchiveFormat.Tar, "tar")]
    [InlineData(ArchiveFormat.GZip, "gzip")]
    [InlineData(ArchiveFormat.Bz2, "bz2")]
    [InlineData(ArchiveFormat.Xz, "xz")]
    [InlineData(ArchiveFormat.Zstd, "zstd")]
    [InlineData(ArchiveFormat.Lzma, "lzma")]
    [InlineData(ArchiveFormat.Rar, "rar")]
    [InlineData(ArchiveFormat.SevenZip, "sevenzip")]
    public void ToRegistryName_EachArchiveFormat_ReturnsExpectedName(ArchiveFormat format, string expected)
    {
        ArchiveFormatRegistryNames.ToRegistryName(format).Should().Be(expected);
    }

    [Fact]
    public void ToRegistryName_UnknownArchiveFormat_Throws()
    {
        var act = () => ArchiveFormatRegistryNames.ToRegistryName(ArchiveFormat.Unknown);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(ArchiveContainerFormat.Zip, "zip")]
    [InlineData(ArchiveContainerFormat.Tar, "tar")]
    [InlineData(ArchiveContainerFormat.TarGz, "gzip")]
    [InlineData(ArchiveContainerFormat.TarBz2, "bz2")]
    [InlineData(ArchiveContainerFormat.TarXz, "xz")]
    [InlineData(ArchiveContainerFormat.TarZst, "zstd")]
    [InlineData(ArchiveContainerFormat.TarLzma, "lzma")]
    public void ToRegistryName_EachContainerFormat_ReturnsExpectedName(ArchiveContainerFormat format, string expected)
    {
        ArchiveFormatRegistryNames.ToRegistryName(format).Should().Be(expected);
    }

    [Fact]
    public void ToRegistryName_ContainerFormatMapsToSameNameAsDetectedArchiveFormat()
    {
        // TarGz is created via ArchiveContainerFormat.TarGz but detected on extraction as
        // ArchiveFormat.GZip — both must map to the same registry name for AllowedFormats/
        // BlockedFormats to apply consistently regardless of which side observes it.
        ArchiveFormatRegistryNames.ToRegistryName(ArchiveContainerFormat.TarGz)
            .Should().Be(ArchiveFormatRegistryNames.ToRegistryName(ArchiveFormat.GZip));
    }
}
