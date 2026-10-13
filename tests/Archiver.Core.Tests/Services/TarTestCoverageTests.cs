using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F397: which formats a test through tar.exe verifies the contents of. Measured on bsdtar 3.8.8
// (docs/DECISIONS.md); these pin the list, the integration tests pin what tar.exe really does.
public sealed class TarTestCoverageTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Write(string name, params byte[] head)
    {
        string path = Path.Combine(_temp.Path, name);
        byte[] content = new byte[1024];
        head.CopyTo(content, 0);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public void SevenZip_IsVerified() =>
        TarTestCoverage.VerifiesContents(Write("a.7z", 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C)).Should().BeTrue();

    [Fact]
    public void Xz_IsVerified() =>
        TarTestCoverage.VerifiesContents(Write("a.tar.xz", 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00)).Should().BeTrue();

    [Fact]
    public void Bzip2_IsVerified() =>
        TarTestCoverage.VerifiesContents(Write("a.tar.bz2", 0x42, 0x5A, 0x68, 0x39, 0x31, 0x41, 0x59, 0x26, 0x53, 0x59)).Should().BeTrue();

    // Bit 2 of the byte after the magic says whether the frame ends in a checksum of its content.
    [Theory]
    [InlineData(0x04, true)]
    [InlineData(0xE4, true)]
    [InlineData(0x00, false)]
    [InlineData(0xFB, false)]
    public void Zstd_IsVerifiedOnlyWithTheContentChecksumFlag(byte descriptor, bool expected) =>
        TarTestCoverage.VerifiesContents(Write("a.tar.zst", 0x28, 0xB5, 0x2F, 0xFD, descriptor)).Should().Be(expected);

    [Fact]
    public void Zstd_CutBeforeItsDescriptor_IsNotVerified()
    {
        string path = Path.Combine(_temp.Path, "short.zst");
        File.WriteAllBytes(path, [0x28, 0xB5, 0x2F, 0xFD]);

        TarTestCoverage.VerifiesContents(path).Should().BeFalse();
    }

    // tar.exe does not compare gzip's CRC-32 or a RAR's data CRC; a plain tar and an .lzma have none.
    [Fact]
    public void Gzip_IsNotVerified() =>
        TarTestCoverage.VerifiesContents(Write("a.tar.gz", 0x1F, 0x8B, 0x08)).Should().BeFalse();

    [Fact]
    public void Rar_IsNotVerified() =>
        TarTestCoverage.VerifiesContents(Write("a.rar", 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00)).Should().BeFalse();

    [Fact]
    public void PlainTar_IsNotVerified()
    {
        string path = Write("a.tar");
        using (FileStream stream = File.OpenWrite(path))
        {
            stream.Position = 257;
            stream.Write("ustar"u8);
        }

        TarTestCoverage.VerifiesContents(path).Should().BeFalse();
    }

    [Fact]
    public void Lzma_IsNotVerified() =>
        TarTestCoverage.VerifiesContents(Write("a.tar.lzma", 0x5D, 0x00, 0x00, 0x80, 0x00)).Should().BeFalse();

    [Fact]
    public void UnknownBytes_AreNotVerified() =>
        TarTestCoverage.VerifiesContents(Write("notes.bin", 0x01, 0x02, 0x03)).Should().BeFalse();

    [Fact]
    public void MissingFile_IsNotVerified_AndDoesNotThrow() =>
        TarTestCoverage.VerifiesContents(Path.Combine(_temp.Path, "absent.7z")).Should().BeFalse();

    [Fact]
    public void FileHeldOpenByAnotherProgram_IsNotVerified_AndDoesNotThrow()
    {
        string path = Write("locked.tar.zst", 0x28, 0xB5, 0x2F, 0xFD, 0x04);
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        TarTestCoverage.VerifiesContents(path).Should().BeFalse();
    }
}
