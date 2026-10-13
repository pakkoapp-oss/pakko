using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// T-F397: whether a test through tar.exe verifies what an archive's files hold, or only that
/// the archive can be read to its end. By the format, measured on bsdtar 3.8.8
/// (docs/DECISIONS.md): 7z, xz and bzip2 carry checksums tar.exe compares; a zstd frame carries
/// one only when its header says so; tar.exe does not compare gzip's CRC-32 or a RAR's data
/// CRC; a plain tar and an .lzma have none.
/// </summary>
internal static class TarTestCoverage
{
    private static ReadOnlySpan<byte> ZstdMagic => [0x28, 0xB5, 0x2F, 0xFD];

    // Frame_Header_Descriptor, the byte after the magic: bit 2 is Content_Checksum_flag (RFC 8878, 3.1.1.1.1).
    private const byte ZstdContentChecksumFlag = 0x04;

    internal static bool VerifiesContents(string archivePath) => ArchiveFormatDetector.Detect(archivePath) switch
    {
        ArchiveFormat.SevenZip or ArchiveFormat.Xz or ArchiveFormat.Bz2 => true,
        ArchiveFormat.Zstd => ZstdFrameHasContentChecksum(archivePath),
        _ => false,
    };

    // The first frame speaks for the file: tar.exe writes one frame, and a file whose first frame
    // has no checksum is not verified whatever follows.
    private static bool ZstdFrameHasContentChecksum(string archivePath)
    {
        Span<byte> head = stackalloc byte[5];
        try
        {
            using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == head.Length
                && head[..4].SequenceEqual(ZstdMagic)
                && (head[4] & ZstdContentChecksumFlag) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
