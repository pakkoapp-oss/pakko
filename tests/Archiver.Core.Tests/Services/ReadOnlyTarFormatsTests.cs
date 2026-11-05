using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F442: the read-only formats tar.exe opens beyond the tar family - detection by signature, the
/// capabilities each needs from the installed tar.exe, and the policy names. Each format has a
/// success row and failure rows (a near-miss signature, a short file, a missing codec).
/// </summary>
public sealed class ReadOnlyTarFormatsTests : IDisposable
{
    private const string Win11Tar = "bsdtar 3.8.9 - libarchive 3.8.9 zlib/1.2.13.1-motley liblzma/5.8.3 bz2lib/1.0.8 libzstd/1.5.7 cng/2.0 libb2/bundled";
    private const string Win10Tar = "bsdtar 3.5.2 - libarchive 3.5.2 zlib/1.2.5.f-ipp ";
    private const string ZliblessTar = "bsdtar 3.8.4 - libarchive 3.8.4 cng/2.0 libb2/bundled ";

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string WriteBytes(string name, byte[] bytes)
    {
        string path = Path.Combine(_temp.Path, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] Padded(byte[] head, int length = 512)
    {
        byte[] bytes = new byte[Math.Max(length, head.Length)];
        head.CopyTo(bytes, 0);
        return bytes;
    }

    private const int Sector = 2048;
    private const int FirstDescriptor = 16 * Sector;

    // An image with a volume descriptor per identifier, from sector 16 on.
    private static byte[] DiscImage(params string[] identifiers)
    {
        byte[] bytes = new byte[FirstDescriptor + (identifiers.Length + 1) * Sector];
        for (int i = 0; i < identifiers.Length; i++)
        {
            int at = FirstDescriptor + i * Sector;
            bytes[at] = identifiers[i] == "CD001" ? (byte)1 : (byte)0;
            System.Text.Encoding.ASCII.GetBytes(identifiers[i]).CopyTo(bytes, at + 1);
            bytes[at + 6] = 1;
        }
        return bytes;
    }

    private static byte[] CabHead() =>
        Padded([0x4D, 0x53, 0x43, 0x46, 0, 0, 0, 0, 0x8B, 0, 0, 0, 0, 0, 0, 0, 0x2C, 0, 0, 0, 0, 0, 0, 0, 0x03, 0x01]);

    // --- Detection: success ---

    [Fact]
    public void Detect_Iso9660_ReturnsIso() =>
        ArchiveFormatDetector.Detect(WriteBytes("a.iso", DiscImage("CD001"))).Should().Be(ArchiveFormat.Iso);

    [Theory]
    [InlineData("NSR02")]
    [InlineData("NSR03")]
    public void Detect_IsoBesideUdf_ReturnsUdf(string nsr) =>
        ArchiveFormatDetector.Detect(WriteBytes("win.iso", DiscImage("CD001", "BEA01", nsr, "TEA01"))).Should().Be(ArchiveFormat.Udf);

    [Fact]
    public void Detect_UdfWithoutIso9660_ReturnsUdf() =>
        ArchiveFormatDetector.Detect(WriteBytes("u.iso", DiscImage("BEA01", "NSR03", "TEA01"))).Should().Be(ArchiveFormat.Udf);

    [Fact]
    public void Detect_Cab_ReturnsCab() =>
        ArchiveFormatDetector.Detect(WriteBytes("a.cab", CabHead())).Should().Be(ArchiveFormat.Cab);

    [Theory]
    [InlineData("0707010000002A000081B6")]
    [InlineData("0707020000002A000081B6")]
    [InlineData("070707000000000000100644")]
    public void Detect_CpioAscii_ReturnsCpio(string head) =>
        ArchiveFormatDetector.Detect(WriteBytes("a.cpio", Padded(System.Text.Encoding.ASCII.GetBytes(head)))).Should().Be(ArchiveFormat.Cpio);

    [Fact]
    public void Detect_Ar_ReturnsAr() =>
        ArchiveFormatDetector.Detect(WriteBytes("a.deb", Padded("!<arch>\ndebian-binary   "u8.ToArray()))).Should().Be(ArchiveFormat.Ar);

    [Theory]
    [InlineData(0x90)]
    [InlineData(0x8C)]
    [InlineData(0x10)]
    public void Detect_Compress_ReturnsCompress(int flags) =>
        ArchiveFormatDetector.Detect(WriteBytes("a.tar.Z", Padded([0x1F, 0x9D, (byte)flags, 0x61]))).Should().Be(ArchiveFormat.Compress);

    [Fact]
    public void Detect_Lzip_ReturnsLzip() =>
        ArchiveFormatDetector.Detect(WriteBytes("a.tar.lz", Padded([.. "LZIP"u8, 1, 0x14]))).Should().Be(ArchiveFormat.Lzip);

    // --- Detection: near misses stay Unknown, so tar.exe never gets a file of no known format ---

    [Fact]
    public void Detect_IsoNamedFileWithoutDescriptor_ReturnsUnknown() =>
        ArchiveFormatDetector.Detect(WriteBytes("a.iso", new byte[FirstDescriptor + Sector])).Should().Be(ArchiveFormat.Unknown);

    [Fact]
    public void Detect_FileEndingBeforeTheFirstDescriptor_ReturnsUnknown() =>
        ArchiveFormatDetector.Detect(WriteBytes("a.iso", new byte[FirstDescriptor + 3])).Should().Be(ArchiveFormat.Unknown);

    [Fact]
    public void Detect_EmptyFile_ReturnsUnknown() =>
        ArchiveFormatDetector.Detect(WriteBytes("a.iso", [])).Should().Be(ArchiveFormat.Unknown);

    [Fact]
    public void Detect_MscfWithoutCabVersion_ReturnsUnknown()
    {
        byte[] head = CabHead();
        head[24] = 0x07;
        ArchiveFormatDetector.Detect(WriteBytes("a.cab", head)).Should().Be(ArchiveFormat.Unknown);
    }

    [Fact]
    public void Detect_MscfWithNonZeroReserved_ReturnsUnknown()
    {
        byte[] head = CabHead();
        head[5] = 1;
        ArchiveFormatDetector.Detect(WriteBytes("a.cab", head)).Should().Be(ArchiveFormat.Unknown);
    }

    [Theory]
    [InlineData("070701 hello text")]
    [InlineData("070707")]
    public void Detect_CpioMagicFollowedByNoDigits_ReturnsUnknown(string head) =>
        ArchiveFormatDetector.Detect(WriteBytes("a.txt", Padded(System.Text.Encoding.ASCII.GetBytes(head)))).Should().Be(ArchiveFormat.Unknown);

    [Fact]
    public void Detect_BinaryCpio_IsNotTaken() =>
        ArchiveFormatDetector.Detect(WriteBytes("a.cpio", Padded([0xC7, 0x71, 0, 0]))).Should().Be(ArchiveFormat.Unknown);

    [Theory]
    [InlineData(0x08)] // fewer than 9 bits
    [InlineData(0x11)] // more than 16 bits
    [InlineData(0x30)] // a reserved bit
    public void Detect_CompressMagicWithImpossibleFlags_ReturnsUnknown(int flags) =>
        ArchiveFormatDetector.Detect(WriteBytes("a.Z", Padded([0x1F, 0x9D, (byte)flags, 0x61]))).Should().Be(ArchiveFormat.Unknown);

    [Fact]
    public void Detect_LzipMagicWithUnknownVersion_ReturnsUnknown() =>
        ArchiveFormatDetector.Detect(WriteBytes("a.lz", Padded([.. "LZIP"u8, 2, 0x14]))).Should().Be(ArchiveFormat.Unknown);

    [Fact]
    public void Detect_ArMagicWithoutItsLineEnd_ReturnsUnknown() =>
        ArchiveFormatDetector.Detect(WriteBytes("a.ar", Padded("!<arch> "u8.ToArray()))).Should().Be(ArchiveFormat.Unknown);

    // --- Availability follows the installed tar.exe ---

    [Theory]
    [InlineData(ArchiveFormat.Iso)]
    [InlineData(ArchiveFormat.Cab)]
    [InlineData(ArchiveFormat.Cpio)]
    [InlineData(ArchiveFormat.Ar)]
    [InlineData(ArchiveFormat.Compress)]
    [InlineData(ArchiveFormat.Lzip)]
    public void Windows11Tar_ReadsEveryNewFormat(ArchiveFormat format) =>
        ArchiveFormatPolicy.GetRefusalReason(format, TarVersionParser.Parse(Win11Tar), new GroupPolicyOptions()).Should().BeNull();

    [Theory]
    [InlineData(ArchiveFormat.Iso)]
    [InlineData(ArchiveFormat.Cab)]
    [InlineData(ArchiveFormat.Cpio)]
    [InlineData(ArchiveFormat.Ar)]
    [InlineData(ArchiveFormat.Compress)]
    public void Windows10Tar_ReadsTheBuiltInFormats(ArchiveFormat format) =>
        ArchiveFormatPolicy.GetRefusalReason(format, TarVersionParser.Parse(Win10Tar), new GroupPolicyOptions()).Should().BeNull();

    [Fact]
    public void Windows10Tar_RefusesLzipNamingIt()
    {
        CoreText? reason = ArchiveFormatPolicy.GetRefusalReason(ArchiveFormat.Lzip, TarVersionParser.Parse(Win10Tar), new GroupPolicyOptions());

        reason.Should().NotBeNull();
        reason!.ToString().Should().Contain("lzip").And.Contain("3.5.2");
    }

    [Fact]
    public void TarWithoutZlib_RefusesCab()
    {
        CoreText? reason = ArchiveFormatPolicy.GetRefusalReason(ArchiveFormat.Cab, TarVersionParser.Parse(ZliblessTar), new GroupPolicyOptions());

        reason.Should().NotBeNull();
        reason!.ToString().Should().Contain("CAB");
    }

    [Theory]
    [InlineData(Win11Tar)]
    [InlineData(Win10Tar)]
    public void Udf_IsRefusedOnEveryTar_NamingUdf(string versionOutput)
    {
        CoreText? reason = ArchiveFormatPolicy.GetRefusalReason(ArchiveFormat.Udf, TarVersionParser.Parse(versionOutput), new GroupPolicyOptions());

        reason.Should().NotBeNull();
        reason!.ToString().Should().Contain("UDF");
    }

    [Theory]
    [InlineData(ArchiveFormat.Iso)]
    [InlineData(ArchiveFormat.Cpio)]
    [InlineData(ArchiveFormat.Lzip)]
    public void TarThatGaveNoVersion_RefusesEveryNewFormat(ArchiveFormat format) =>
        ArchiveFormatPolicy.GetRefusalReason(format, TarVersionParser.Parse(""), new GroupPolicyOptions()).Should().NotBeNull();

    [Fact]
    public void ReadableNames_FollowTheInstalledTar()
    {
        ArchiveFormatPolicy.TarReadableRegistryNames(TarVersionParser.Parse(Win11Tar))
            .Should().Contain(["iso", "cab", "cpio", "ar", "compress", "lzip"]);
        ArchiveFormatPolicy.TarReadableRegistryNames(TarVersionParser.Parse(Win10Tar))
            .Should().Contain(["iso", "cab", "cpio", "ar", "compress"]).And.NotContain("lzip");
        ArchiveFormatPolicy.TarReadableRegistryNames(TarVersionParser.Parse(""))
            .Should().NotContain(["iso", "cab", "cpio", "ar", "compress", "lzip"]);
    }

    [Fact]
    public void Snapshot_WithEveryName_StillFitsItsLine()
    {
        string line = TarCapabilitySnapshot.Format(long.MaxValue, long.MaxValue, true,
            ArchiveFormatPolicy.TarReadableRegistryNames(TarVersionParser.Parse(Win11Tar)));

        line.Length.Should().BeLessThan(200);
    }

    // --- Policy ---

    [Theory]
    [InlineData(ArchiveFormat.Iso, "iso")]
    [InlineData(ArchiveFormat.Udf, "iso")]
    [InlineData(ArchiveFormat.Cab, "cab")]
    [InlineData(ArchiveFormat.Cpio, "cpio")]
    [InlineData(ArchiveFormat.Ar, "ar")]
    [InlineData(ArchiveFormat.Compress, "compress")]
    [InlineData(ArchiveFormat.Lzip, "lzip")]
    public void RegistryName(ArchiveFormat format, string name) =>
        ArchiveFormatRegistryNames.ToRegistryName(format).Should().Be(name);

    [Fact]
    public void BlockedFormats_RefusesANewFormat()
    {
        var policy = new GroupPolicyOptions { BlockedFormats = ["iso"] };

        ArchiveFormatPolicy.GetRefusalReason(ArchiveFormat.Iso, TarVersionParser.Parse(Win11Tar), policy).Should().NotBeNull();
        ArchiveFormatPolicy.GetRefusalReason(ArchiveFormat.Cab, TarVersionParser.Parse(Win11Tar), policy).Should().BeNull();
    }

    [Fact]
    public void AllowedFormats_WithoutTheNewNames_RefusesThem()
    {
        var policy = new GroupPolicyOptions { AllowedFormats = ["zip", "tar"] };

        ArchiveFormatPolicy.GetRefusalReason(ArchiveFormat.Cpio, TarVersionParser.Parse(Win11Tar), policy).Should().NotBeNull();
    }

    [Fact]
    public void DisableTarExtraction_RefusesTheNewFormats() =>
        ArchiveFormatPolicy.GetRefusalReason(ArchiveFormat.Ar, TarVersionParser.Parse(Win11Tar), new GroupPolicyOptions { DisableTarExtraction = true })
            .Should().NotBeNull();
}
