using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

public sealed class ArchiveFormatDetectorTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string WriteBytes(string name, byte[] bytes)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Detect_ZipMagic_ReturnsZip()
    {
        var path = WriteBytes("a.zip", [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0]);
        ArchiveFormatDetector.Detect(path).Should().Be(ArchiveFormat.Zip);
    }

    [Fact]
    public void Detect_GZipMagic_ReturnsGZip()
    {
        var path = WriteBytes("a.gz", [0x1F, 0x8B, 0x08, 0]);
        ArchiveFormatDetector.Detect(path).Should().Be(ArchiveFormat.GZip);
    }

    [Fact]
    public void Detect_Bz2Magic_ReturnsBz2()
    {
        var path = WriteBytes("a.bz2", [0x42, 0x5A, 0x68, 0x39]);
        ArchiveFormatDetector.Detect(path).Should().Be(ArchiveFormat.Bz2);
    }

    [Fact]
    public void Detect_RarMagic_ReturnsRar()
    {
        var path = WriteBytes("a.rar", [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00]);
        ArchiveFormatDetector.Detect(path).Should().Be(ArchiveFormat.Rar);
    }

    [Fact]
    public void Detect_SevenZipMagic_ReturnsSevenZip()
    {
        var path = WriteBytes("a.7z", [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0, 0]);
        ArchiveFormatDetector.Detect(path).Should().Be(ArchiveFormat.SevenZip);
    }

    [Fact]
    public void Detect_XzMagic_ReturnsXz()
    {
        var path = WriteBytes("a.xz", [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00, 0, 0]);
        ArchiveFormatDetector.Detect(path).Should().Be(ArchiveFormat.Xz);
    }

    [Fact]
    public void Detect_ZstdMagic_ReturnsZstd()
    {
        var path = WriteBytes("a.zst", [0x28, 0xB5, 0x2F, 0xFD, 0, 0, 0, 0]);
        ArchiveFormatDetector.Detect(path).Should().Be(ArchiveFormat.Zstd);
    }

    [Fact]
    public void Detect_UstarMagicAtOffset257_ReturnsTar()
    {
        var header = new byte[512];
        var ustar = System.Text.Encoding.ASCII.GetBytes("ustar");
        Array.Copy(ustar, 0, header, 257, ustar.Length);
        var path = WriteBytes("a.tar", header);
        ArchiveFormatDetector.Detect(path).Should().Be(ArchiveFormat.Tar);
    }

    [Fact]
    public void Detect_UnrecognizedBytes_ReturnsUnknown()
    {
        var path = WriteBytes("a.bin", [1, 2, 3, 4, 5, 6, 7, 8]);
        ArchiveFormatDetector.Detect(path).Should().Be(ArchiveFormat.Unknown);
    }

    [Fact]
    public void Detect_MissingFile_ReturnsUnknown()
    {
        ArchiveFormatDetector.Detect(Path.Combine(_temp.Path, "does_not_exist.zip")).Should().Be(ArchiveFormat.Unknown);
    }
}
