using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F403: one answer to "can this system's tar.exe make this archive", asked by Core before
// tar.exe starts, by the App's Format and method lists and by the CLI. A codec tar.exe lacks is
// refused: tar.exe would otherwise run an external program of that name from its working folder
// or PATH (measured for zstd and bzip2 on Windows 10 and Server 2022).
public sealed class ArchiveFormatPolicyCreationTests
{
    // Windows 10 22H2 (19045.6456) and Server 2022 (20348.5622), measured.
    private static readonly TarCapabilities Windows10 = TarVersionParser.Parse("bsdtar 3.5.2 - libarchive 3.5.2 zlib/1.2.5.f-ipp ");
    private static readonly TarCapabilities Server2022 =
        TarVersionParser.Parse("bsdtar 3.8.4 - libarchive 3.8.4 zlib/1.2.5.f-ipp cng/2.0 libb2/bundled ");
    private static readonly TarCapabilities Windows11 =
        TarVersionParser.Parse("bsdtar 3.8.8 - libarchive 3.8.8 zlib/1.2.13.1-motley liblzma/5.8.1 bz2lib/1.0.8 libzstd/1.5.7");

    [Theory]
    [InlineData(ArchiveContainerFormat.Zip)]
    [InlineData(ArchiveContainerFormat.Tar)]
    [InlineData(ArchiveContainerFormat.TarGz)]
    public void ZlibOnly_TarAndGzipAndZip_AreCreatable(ArchiveContainerFormat format)
    {
        ArchiveFormatPolicy.GetTarCreationRefusalReason(format, SevenZipMethod.Lzma2, Windows10).Should().BeNull();
        ArchiveFormatPolicy.GetTarCreationRefusalReason(format, SevenZipMethod.Lzma2, Server2022).Should().BeNull();
    }

    [Theory]
    [InlineData(ArchiveContainerFormat.TarBz2, "bzip2")]
    [InlineData(ArchiveContainerFormat.TarXz, "XZ")]
    [InlineData(ArchiveContainerFormat.TarZst, "Zstandard")]
    [InlineData(ArchiveContainerFormat.TarLzma, "LZMA")]
    [InlineData(ArchiveContainerFormat.SevenZip, "7-Zip")]
    public void ZlibOnly_OtherFormats_AreRefusedNamingTheCodecAndVersion(ArchiveContainerFormat format, string name)
    {
        CoreText? reason = ArchiveFormatPolicy.GetTarCreationRefusalReason(format, SevenZipMethod.Lzma2, Server2022);

        reason.Should().NotBeNull();
        reason!.Code.Should().Be(MessageCode.FormatNotSupportedByTar);
        reason.English.Should().Be($"{name} is not supported by this system's tar.exe (version 3.8.4).");
    }

    [Fact]
    public void OlderVersion_ZstdAnd7z_SayANewerTarIsNeeded()
    {
        ArchiveFormatPolicy.GetTarCreationRefusalReason(ArchiveContainerFormat.TarZst, SevenZipMethod.Lzma2, Windows10)!
            .Code.Should().Be(MessageCode.FormatNeedsNewerTar);
        ArchiveFormatPolicy.GetTarCreationRefusalReason(ArchiveContainerFormat.SevenZip, SevenZipMethod.Lzma2, Windows10)!
            .Code.Should().Be(MessageCode.FormatNeedsNewerTar);
    }

    [Fact]
    public void AllCodecs_EveryFormatAndMethod_IsCreatable()
    {
        foreach (ArchiveContainerFormat format in Enum.GetValues<ArchiveContainerFormat>())
        {
            foreach (SevenZipMethod method in Enum.GetValues<SevenZipMethod>())
                ArchiveFormatPolicy.GetTarCreationRefusalReason(format, method, Windows11).Should().BeNull($"{format} {method}");
        }
    }

    [Fact]
    public void SevenZipWithoutBzip2_RefusesOnlyTheBZip2Method()
    {
        TarCapabilities noBzip2 = TarVersionParser.Parse("bsdtar 3.8.8 - libarchive 3.8.8 zlib/1.2.13 liblzma/5.8.1 libzstd/1.5.7");

        ArchiveFormatPolicy.CreatableSevenZipMethods(noBzip2).Should().Equal(
            SevenZipMethod.Lzma2, SevenZipMethod.Lzma, SevenZipMethod.Deflate, SevenZipMethod.Copy);
        CoreText? reason = ArchiveFormatPolicy.GetTarCreationRefusalReason(ArchiveContainerFormat.SevenZip, SevenZipMethod.BZip2, noBzip2);
        reason!.English.Should().Be("7-Zip BZip2 is not supported by this system's tar.exe (version 3.8.8).");
    }

    [Fact]
    public void ZipIgnoresTarCapabilities() =>
        ArchiveFormatPolicy.GetTarCreationRefusalReason(ArchiveContainerFormat.Zip, SevenZipMethod.Lzma2, new TarCapabilities())
            .Should().BeNull();

    [Theory]
    [InlineData(ArchiveFormat.Zstd, "Zstandard")]
    [InlineData(ArchiveFormat.Bz2, "bzip2")]
    [InlineData(ArchiveFormat.SevenZip, "7-Zip")]
    public void Reading_ModernVersionWithoutTheCodec_NamesTheCodec(ArchiveFormat format, string name)
    {
        CoreText? reason = ArchiveFormatPolicy.GetRefusalReason(format, Server2022, new GroupPolicyOptions());

        reason!.English.Should().Be($"{name} is not supported by this system's tar.exe (version 3.8.4).");
    }
}
