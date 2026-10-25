using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F413: 7-Zip's -tgzip, -tbzip2 and -txz - one file compressed on its own, no tar inside -
/// written by tar.exe's raw format. The parts that need no tar.exe run: names, policy, arguments
/// and every refusal that comes before anything is written. The writing itself is checked against
/// the real tar.exe in Archiver.Core.IntegrationTests.
/// </summary>
public sealed class SingleFileStreamCreationTests : IDisposable
{
    private static readonly TarCapabilities ZlibOnly =
        TarVersionParser.Parse("bsdtar 3.8.4 - libarchive 3.8.4 zlib/1.2.5.f-ipp cng/2.0 libb2/bundled ");
    private static readonly TarCapabilities AllCodecs =
        TarVersionParser.Parse("bsdtar 3.8.9 - libarchive 3.8.9 zlib/1.2.13.1-motley liblzma/5.8.3 bz2lib/1.0.8 libzstd/1.5.7");

    private readonly TempDirectory _temp = new();
    private int _probes;

    public void Dispose() => _temp.Dispose();

    private TarSandboxedService Service() =>
        new(new GroupPolicyOptions(), () => { _probes++; return Task.FromResult(AllCodecs); },
            beforeCreationLaunch: () => throw new InvalidOperationException("tar.exe must not run"));

    private ArchiveOptions Options(ArchiveContainerFormat format, params string[] sources) => new()
    {
        SourcePaths = sources,
        DestinationFolder = Path.Combine(_temp.Path, "out"),
        ArchiveName = "out",
        Format = format,
    };

    private string File(string name)
    {
        string path = Path.Combine(_temp.Path, name);
        System.IO.File.WriteAllText(path, name);
        return path;
    }

    private static List<string> Args(ArchiveContainerFormat format, CompressionLevel level)
    {
        var args = new List<string>();
        TarSandboxedService.AppendCompressionFilterArgs(args, format, level);
        return args;
    }

    // --- Happy path ---

    [Theory]
    [InlineData(ArchiveContainerFormat.GZip, ".gz", "gzip", ArchiveFormat.GZip)]
    [InlineData(ArchiveContainerFormat.BZip2, ".bz2", "bz2", ArchiveFormat.Bz2)]
    [InlineData(ArchiveContainerFormat.Xz, ".xz", "xz", ArchiveFormat.Xz)]
    public void NamingAndPolicyName_AreTheOnesReadingUses(ArchiveContainerFormat format, string extension, string policyName, ArchiveFormat read)
    {
        ArchiveNaming.GetExtension(format).Should().Be(extension);
        ArchiveFormatRegistryNames.ToRegistryName(format).Should().Be(policyName)
            .And.Be(ArchiveFormatRegistryNames.ToRegistryName(read));
        ArchiveFormatPolicy.IsSingleFileFormat(format).Should().BeTrue();
    }

    [Fact]
    public void ArchiveFormats_AreNotSingleFileFormats() =>
        Enum.GetValues<ArchiveContainerFormat>().Where(ArchiveFormatPolicy.IsSingleFileFormat)
            .Should().Equal(ArchiveContainerFormat.GZip, ArchiveContainerFormat.BZip2, ArchiveContainerFormat.Xz);

    [Theory]
    [InlineData(ArchiveContainerFormat.GZip, CompressionLevel.Optimal, "-z", "gzip:compression-level=4,gzip:!timestamp")]
    [InlineData(ArchiveContainerFormat.BZip2, CompressionLevel.Fastest, "-j", "bzip2:compression-level=1")]
    [InlineData(ArchiveContainerFormat.Xz, CompressionLevel.SmallestSize, "-J", "xz:compression-level=9")]
    public void AppendCompressionFilterArgs_SingleFileFormat_IsTheCodecInTheRawFormat(
        ArchiveContainerFormat format, CompressionLevel level, string filter, string options) =>
        Args(format, level).Should().Equal(filter, "--options", options, "--format", "raw");

    [Fact]
    public void AppendCompressionFilterArgs_TarGz_HasNoRawFormat() =>
        Args(ArchiveContainerFormat.TarGz, CompressionLevel.Optimal).Should().Equal("-z", "--options", "gzip:compression-level=4");

    // --- Security & Boundary: a codec tar.exe lacks is refused before tar.exe runs (T-F403) ---

    [Fact]
    public void ZlibOnly_GzipIsCreatable_Bzip2AndXzAreRefusedByName()
    {
        ArchiveFormatPolicy.GetTarCreationRefusalReason(ArchiveContainerFormat.GZip, SevenZipMethod.Lzma2, ZlibOnly).Should().BeNull();
        ArchiveFormatPolicy.GetTarCreationRefusalReason(ArchiveContainerFormat.BZip2, SevenZipMethod.Lzma2, ZlibOnly)!
            .English.Should().Be("bzip2 is not supported by this system's tar.exe (version 3.8.4).");
        ArchiveFormatPolicy.GetTarCreationRefusalReason(ArchiveContainerFormat.Xz, SevenZipMethod.Lzma2, ZlibOnly)!
            .English.Should().Be("XZ is not supported by this system's tar.exe (version 3.8.4).");
    }

    [Theory]
    [InlineData("blocked")]
    [InlineData("tar disabled")]
    public async Task Router_PolicyRefusesGzip_LikeTarGz(string policyName)
    {
        GroupPolicyOptions policy = policyName == "blocked"
            ? new GroupPolicyOptions { BlockedFormats = ["gzip"] }
            : new GroupPolicyOptions { DisableTarExtraction = true };
        var services = PakkoServices.Create(policy);

        ArchiveResult result = await services.CreationRouter.ArchiveAsync(Options(ArchiveContainerFormat.GZip, File("a.txt")));

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(
            policyName == "blocked" ? MessageCode.CreationFormatBlocked : MessageCode.TarCreationDisabled);
        Directory.Exists(Path.Combine(_temp.Path, "out")).Should().BeFalse();
    }

    // --- Misuse: a stream holds one file. tar.exe's raw writer refuses a second entry or a folder
    // only after it has created the output (measured: a 0-byte file and exit 1); Pakko refuses
    // first, as 7-Zip does (exit 2, "The parameter is incorrect"). ---

    [Theory]
    [InlineData(ArchiveContainerFormat.GZip, "gzip")]
    [InlineData(ArchiveContainerFormat.BZip2, "bzip2")]
    [InlineData(ArchiveContainerFormat.Xz, "xz")]
    public async Task CompressAsync_TwoFiles_IsRefusedBeforeTarExeAndWritesNothing(ArchiveContainerFormat format, string name)
    {
        TarSandboxedService sut = Service();

        ArchiveResult result = await sut.CompressAsync(Options(format, File("a.txt"), File("b.txt")));

        CoreText text = result.Errors.Should().ContainSingle().Which.Text!;
        text.Code.Should().Be(MessageCode.SingleFileFormatNeedsOneFile);
        text.English.Should().Be($"{name} compresses a single file: give exactly one file, not a folder or several files.");
        result.CreatedFiles.Should().BeEmpty();
        Directory.Exists(Path.Combine(_temp.Path, "out")).Should().BeFalse();
        _probes.Should().Be(0);
    }

    [Fact]
    public async Task CompressAsync_AFolder_IsRefusedBeforeTarExe()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "dir")).FullName;
        System.IO.File.WriteAllText(Path.Combine(folder, "x.txt"), "x");
        TarSandboxedService sut = Service();

        ArchiveResult result = await sut.CompressAsync(Options(ArchiveContainerFormat.Xz, folder));

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.SingleFileFormatNeedsOneFile);
        Directory.Exists(Path.Combine(_temp.Path, "out")).Should().BeFalse();
        _probes.Should().Be(0);
    }

    // One archive per source: several files are several streams, a folder still is not one.
    [Fact]
    public async Task CompressAsync_SeparateArchives_AFolderAmongFiles_IsRefused()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "dir")).FullName;
        TarSandboxedService sut = Service();

        ArchiveResult result = await sut.CompressAsync(
            Options(ArchiveContainerFormat.GZip, File("a.txt"), folder) with { Mode = ArchiveMode.SeparateArchives });

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.SingleFileFormatNeedsOneFile);
        _probes.Should().Be(0);
    }

    [Fact]
    public async Task CompressAsync_TarGzOfTwoFiles_IsNotRefusedAsAStream()
    {
        // The refusal is the single-file formats' only; a tar.gz takes any number of sources.
        TarSandboxedService sut = Service();

        ArchiveResult result = await sut.CompressAsync(
            Options(ArchiveContainerFormat.TarGz, File("a.txt"), File("b.txt")) with { ResolvePasswordAsync = _ => Task.FromResult(new PasswordDecision()) });

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.PasswordOnlyForZip);
    }
}
