using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

public sealed class ArchiveNamingTests
{
    [Theory]
    [InlineData(@"C:\Docs\browse_test.tar.gz", "browse_test")]
    [InlineData(@"C:\Docs\browse_test.tar.bz2", "browse_test")]
    [InlineData(@"C:\Docs\browse_test.tar.xz", "browse_test")]
    [InlineData(@"C:\Docs\browse_test.tar.zst", "browse_test")]
    [InlineData(@"C:\Docs\browse_test.tar.lzma", "browse_test")]
    [InlineData(@"C:\Docs\BROWSE_TEST.TAR.GZ", "BROWSE_TEST")]
    public void GetBaseName_CompoundTarExtension_StripsBothComponents(string archivePath, string expected)
    {
        ArchiveNaming.GetBaseName(archivePath).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"C:\Docs\archive.zip", "archive")]
    [InlineData(@"C:\Docs\archive.7z", "archive")]
    [InlineData(@"C:\Docs\archive.rar", "archive")]
    [InlineData(@"C:\Docs\archive.tar", "archive")]
    [InlineData(@"C:\Docs\archive.tgz", "archive")]
    [InlineData(@"C:\Docs\archive.tbz2", "archive")]
    public void GetBaseName_SingleExtension_StripsLastSegmentOnly(string archivePath, string expected)
    {
        ArchiveNaming.GetBaseName(archivePath).Should().Be(expected);
    }

    [Theory]
    [InlineData(ArchiveContainerFormat.Zip, ".zip")]
    [InlineData(ArchiveContainerFormat.Tar, ".tar")]
    [InlineData(ArchiveContainerFormat.TarGz, ".tar.gz")]
    [InlineData(ArchiveContainerFormat.TarBz2, ".tar.bz2")]
    [InlineData(ArchiveContainerFormat.TarXz, ".tar.xz")]
    [InlineData(ArchiveContainerFormat.TarZst, ".tar.zst")]
    [InlineData(ArchiveContainerFormat.TarLzma, ".tar.lzma")]
    public void GetExtension_EachContainerFormat_ReturnsExpectedExtension(ArchiveContainerFormat format, string expected)
    {
        ArchiveNaming.GetExtension(format).Should().Be(expected);
    }
}
