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

    [Theory]
    [InlineData("clip.zip")]
    [InlineData("clip.RAR")]
    [InlineData("clip.7z")]
    [InlineData("clip.tar")]
    [InlineData("clip.gz")]
    [InlineData("clip.tgz")]
    [InlineData("clip.bz2")]
    [InlineData("clip.tbz2")]
    [InlineData("clip.xz")]
    [InlineData("clip.txz")]
    [InlineData("clip.zst")]
    [InlineData("clip.tzst")]
    [InlineData("clip.lzma")]
    [InlineData("nested/path/clip.zip")]
    public void IsRecognizedArchiveExtension_RecognizedExtension_ReturnsTrue(string fileName)
    {
        ArchiveFormatDetector.IsRecognizedArchiveExtension(fileName).Should().BeTrue();
    }

    [Theory]
    [InlineData("clip.mp4")]
    [InlineData("clip.txt")]
    [InlineData("clip.exe")]
    [InlineData("clip")]
    [InlineData("")]
    public void IsRecognizedArchiveExtension_UnrecognizedExtension_ReturnsFalse(string fileName)
    {
        ArchiveFormatDetector.IsRecognizedArchiveExtension(fileName).Should().BeFalse();
    }

    [Fact]
    public void IsRecognizedArchiveExtension_DoesNotTouchDisk()
    {
        // Unlike Detect(), this must work for a path that never existed on disk — it's used to
        // decide drill-down candidacy for an in-archive entry before anything is extracted.
        ArchiveFormatDetector.IsRecognizedArchiveExtension(Path.Combine(_temp.Path, "does_not_exist.zip")).Should().BeTrue();
    }

    // T-F113: synthetic, hand-crafted RAR5 block bytes — not full spec-valid archives, just
    // shaped exactly the way IsEncryptedRar reads them (verified field-by-field against real
    // WinRAR-encrypted fixtures via a throwaway Python probe; see DECISIONS.md's T-F113 entry).
    // Byte layout per block: CRC32(4, arbitrary) + HeaderSize(vint) + HeaderType(vint) + ...,
    // where HeaderSize counts bytes from HeaderType through the end of that block.
    private static readonly byte[] Rar5Sig = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00];

    [Fact]
    public void IsEncryptedRar_PlainFileEntry_ReturnsFalse()
    {
        byte[] bytes =
        [
            .. Rar5Sig,
            // Main Archive Header (type 1): HeaderSize=1 covers just the HeaderType byte —
            // IsEncryptedRar never reads anything else from this block.
            0x00, 0x00, 0x00, 0x00, 0x01, 0x01,
            // File Header (type 2): HeaderSize=6 (type+flags+extraAreaSize+3-byte extra area).
            // HeaderFlags=0x01 (extra area present). Extra area: one record, size=2 (type+data),
            // type=3 (arbitrary non-encryption type), data=0x00.
            0x00, 0x00, 0x00, 0x00, 0x06, 0x02, 0x01, 0x03, 0x02, 0x03, 0x00,
        ];
        var path = WriteBytes("plain.rar", bytes);
        ArchiveFormatDetector.IsEncryptedRar(path).Should().BeFalse();
    }

    [Fact]
    public void IsEncryptedRar_FirstEntryHasEncryptionRecord_ReturnsTrue()
    {
        byte[] bytes =
        [
            .. Rar5Sig,
            0x00, 0x00, 0x00, 0x00, 0x01, 0x01, // Main Archive Header (type 1)
            // File Header (type 2), extra area's one record has type=1 (Encryption).
            0x00, 0x00, 0x00, 0x00, 0x06, 0x02, 0x01, 0x03, 0x02, 0x01, 0x00,
        ];
        var path = WriteBytes("encrypted.rar", bytes);
        ArchiveFormatDetector.IsEncryptedRar(path).Should().BeTrue();
    }

    [Fact]
    public void IsEncryptedRar_ArchiveEncryptionHeaderBlock_ReturnsTrue()
    {
        byte[] bytes =
        [
            .. Rar5Sig,
            // First block is type 4 (Archive encryption header) — presence alone means every
            // further header, including filenames, is encrypted. IsEncryptedRar returns true
            // without reading anything past the HeaderType field.
            0x00, 0x00, 0x00, 0x00, 0x01, 0x04,
        ];
        var path = WriteBytes("encrypted_headers.rar", bytes);
        ArchiveFormatDetector.IsEncryptedRar(path).Should().BeTrue();
    }

    [Fact]
    public void IsRarHeaderEncrypted_DataOnlyEncryptionRecord_ReturnsFalse()
    {
        // Narrower than IsEncryptedRar — a data-only-encrypted first entry (Encryption record,
        // type 1) does not make headers/filenames unreadable, so this must return false even
        // though IsEncryptedRar(same bytes) returns true (see the test above).
        byte[] bytes =
        [
            .. Rar5Sig,
            0x00, 0x00, 0x00, 0x00, 0x01, 0x01, // Main Archive Header (type 1)
            0x00, 0x00, 0x00, 0x00, 0x06, 0x02, 0x01, 0x03, 0x02, 0x01, 0x00,
        ];
        var path = WriteBytes("data_only_encrypted.rar", bytes);
        ArchiveFormatDetector.IsRarHeaderEncrypted(path).Should().BeFalse();
    }

    [Fact]
    public void IsRarHeaderEncrypted_ArchiveEncryptionHeaderBlock_ReturnsTrue()
    {
        byte[] bytes =
        [
            .. Rar5Sig,
            0x00, 0x00, 0x00, 0x00, 0x01, 0x04, // First block is type 4
        ];
        var path = WriteBytes("encrypted_headers2.rar", bytes);
        ArchiveFormatDetector.IsRarHeaderEncrypted(path).Should().BeTrue();
    }

    [Fact]
    public void IsRarHeaderEncrypted_PlainFileEntry_ReturnsFalse()
    {
        byte[] bytes =
        [
            .. Rar5Sig,
            0x00, 0x00, 0x00, 0x00, 0x01, 0x01,
            0x00, 0x00, 0x00, 0x00, 0x06, 0x02, 0x01, 0x03, 0x02, 0x03, 0x00,
        ];
        var path = WriteBytes("plain2.rar", bytes);
        ArchiveFormatDetector.IsRarHeaderEncrypted(path).Should().BeFalse();
    }

    [Fact]
    public void IsRarHeaderEncrypted_MissingFile_ReturnsFalse()
    {
        ArchiveFormatDetector.IsRarHeaderEncrypted(Path.Combine(_temp.Path, "does_not_exist.rar")).Should().BeFalse();
    }

    [Fact]
    public void IsEncryptedRar_TruncatedAfterSignature_ReturnsFalse()
    {
        var path = WriteBytes("truncated.rar", Rar5Sig);
        ArchiveFormatDetector.IsEncryptedRar(path).Should().BeFalse();
    }

    [Fact]
    public void IsEncryptedRar_Rar4Signature_ReturnsFalse()
    {
        // Legacy RAR4 (7-byte signature, no version byte) is an accepted scope cut — not parsed.
        var path = WriteBytes("rar4.rar", [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00, 0x00]);
        ArchiveFormatDetector.IsEncryptedRar(path).Should().BeFalse();
    }

    [Fact]
    public void IsEncryptedRar_NotARarFile_ReturnsFalse()
    {
        var path = WriteBytes("a.zip", [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0]);
        ArchiveFormatDetector.IsEncryptedRar(path).Should().BeFalse();
    }

    [Fact]
    public void IsEncryptedRar_MissingFile_ReturnsFalse()
    {
        ArchiveFormatDetector.IsEncryptedRar(Path.Combine(_temp.Path, "does_not_exist.rar")).Should().BeFalse();
    }
}
