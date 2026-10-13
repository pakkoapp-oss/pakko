using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// T-F397: whether a test through tar.exe verifies what an archive's files hold, or only that
/// the archive can be read to its end. By the format, measured on bsdtar 3.8.8
/// (docs/DECISIONS.md): 7z and bzip2 carry checksums tar.exe compares; an xz stream and a zstd
/// frame carry one only when their header says so; tar.exe does not compare gzip's CRC-32 or a
/// RAR's data CRC; a plain tar and an .lzma have none.
/// </summary>
internal static class TarTestCoverage
{
    private const int HeadLength = 8;

    private static ReadOnlySpan<byte> XzMagic => [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00];

    // Stream Flags, the two bytes after the magic: the low four bits of the second name the
    // check, and 0 is None (xz file format 1.1.0, 2.1.1.2).
    private const int XzCheckOffset = 7;
    private const byte XzCheckMask = 0x0F;

    private static ReadOnlySpan<byte> ZstdMagic => [0x28, 0xB5, 0x2F, 0xFD];

    // Frame_Header_Descriptor, the byte after the magic: bit 2 is Content_Checksum_flag (RFC 8878, 3.1.1.1.1).
    private const int ZstdDescriptorOffset = 4;
    private const byte ZstdContentChecksumFlag = 0x04;

    internal static bool VerifiesContents(string archivePath) => ArchiveFormatDetector.Detect(archivePath) switch
    {
        ArchiveFormat.SevenZip or ArchiveFormat.Bz2 => true,
        ArchiveFormat.Xz => HeadHas(archivePath, XzMagic, XzCheckOffset, XzCheckMask),
        ArchiveFormat.Zstd => HeadHas(archivePath, ZstdMagic, ZstdDescriptorOffset, ZstdContentChecksumFlag),
        _ => false,
    };

    // The first stream or frame speaks for the file: tar.exe writes one, and a file whose first
    // has no check is not verified whatever follows.
    private static bool HeadHas(string archivePath, ReadOnlySpan<byte> magic, int offset, byte mask)
    {
        Span<byte> head = stackalloc byte[HeadLength];
        try
        {
            using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            return offset < read && head[..magic.Length].SequenceEqual(magic) && (head[offset] & mask) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
