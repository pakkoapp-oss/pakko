using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F403: CompressAsync refuses what this tar.exe cannot make before tar.exe starts. On a zlib-only
// tar.exe, --zstd and -j made tar.exe run zstd.exe and bzip2.exe from its working folder or PATH
// (measured on Windows 10 22H2 and Server 2022), outside any sandbox.
public sealed class TarCreationCapabilityGateTests : IDisposable
{
    private static readonly TarCapabilities ZlibOnly =
        TarVersionParser.Parse("bsdtar 3.8.4 - libarchive 3.8.4 zlib/1.2.5.f-ipp cng/2.0 libb2/bundled ");

    private readonly TempDirectory _temp = new();
    private int _launches;

    public void Dispose() => _temp.Dispose();

    private TarSandboxedService Service(TarCapabilities capabilities) => new(
        new GroupPolicyOptions(), () => Task.FromResult(capabilities), beforeCreationLaunch: () => _launches++,
        sevenZipWriterProbe: () => Task.FromResult<bool?>(true));

    private ArchiveOptions Options(ArchiveContainerFormat format, SevenZipMethod method = SevenZipMethod.Lzma2)
    {
        string source = Path.Combine(_temp.Path, "a.txt");
        File.WriteAllText(source, "a");
        return new ArchiveOptions
        {
            SourcePaths = [source],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
            ArchiveName = "out",
            Format = format,
            SevenZipMethod = method,
        };
    }

    [Theory]
    [InlineData(ArchiveContainerFormat.TarZst)]
    [InlineData(ArchiveContainerFormat.TarBz2)]
    [InlineData(ArchiveContainerFormat.TarXz)]
    [InlineData(ArchiveContainerFormat.TarLzma)]
    [InlineData(ArchiveContainerFormat.SevenZip)]
    public async Task CompressAsync_FormatThisTarCannotMake_IsRefusedBeforeTarStarts(ArchiveContainerFormat format)
    {
        ArchiveResult result = await Service(ZlibOnly).CompressAsync(Options(format));

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.FormatNotSupportedByTar);
        result.CreatedFiles.Should().BeEmpty();
        _launches.Should().Be(0);
        Directory.Exists(Path.Combine(_temp.Path, "out")).Should().BeFalse();
    }

    // A --version run that gave no answer (a timeout on a loaded machine: empty capabilities) is
    // asked again, and an answer is kept: a slow tar.exe must not read as one without codecs.
    [Fact]
    public async Task CompressAsync_FirstProbeGaveNoAnswer_AsksAgainAndKeepsTheAnswer()
    {
        TarCapabilities full = TarVersionParser.Parse(
            "bsdtar 3.8.8 - libarchive 3.8.8 zlib/1.2.13 liblzma/5.8.1 bz2lib/1.0.8 libzstd/1.5.7");
        int probes = 0;
        var sut = new TarSandboxedService(
            new GroupPolicyOptions(), () => Task.FromResult(probes++ == 0 ? new TarCapabilities() : full),
            beforeCreationLaunch: () => _launches++, sevenZipWriterProbe: () => Task.FromResult<bool?>(true));

        ArchiveResult first = await sut.CompressAsync(Options(ArchiveContainerFormat.TarXz));
        ArchiveResult second = await sut.CompressAsync(Options(ArchiveContainerFormat.TarXz) with { ArchiveName = "out2" });

        first.Errors.Should().NotContain(e => e.Text != null && (e.Text.Code == MessageCode.FormatNeedsNewerTar || e.Text.Code == MessageCode.FormatNotSupportedByTar));
        second.Errors.Should().NotContain(e => e.Text != null && (e.Text.Code == MessageCode.FormatNeedsNewerTar || e.Text.Code == MessageCode.FormatNotSupportedByTar));
        _launches.Should().Be(2);
        probes.Should().Be(2);
    }

    [Fact]
    public async Task CompressAsync_SevenZipMethodThisTarCannotUse_IsRefusedBeforeTarStarts()
    {
        TarCapabilities noBzip2 = TarVersionParser.Parse("bsdtar 3.8.8 - libarchive 3.8.8 zlib/1.2.13 liblzma/5.8.1");

        ArchiveResult result = await Service(noBzip2).CompressAsync(Options(ArchiveContainerFormat.SevenZip, SevenZipMethod.BZip2));

        result.Errors.Should().ContainSingle().Which.Message.Should().Be(
            "7-Zip BZip2 is not supported by this system's tar.exe (version 3.8.8).");
        _launches.Should().Be(0);
    }
}
