using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// Detects archive format from magic bytes only — no extension reliance, matching this
/// codebase's existing ZIP signature-sniffing convention (see ZipArchiveService.IsZipFile).
/// Used by ExtractionRouter to decide which service (IArchiveService vs ITarService) should
/// handle a given archive path.
/// </summary>
public static class ArchiveFormatDetector
{
    // T-F98: extensions recognized as "probably an archive" without touching disk — used to
    // decide whether an in-archive entry (not yet extracted, so Detect's magic-byte sniff can't
    // run against it) is a drill-down candidate, and by MainViewModel.IsExtractOnlySelection for
    // the pending list. The real magic-byte Detect() call still runs after extraction to confirm.
    private static readonly HashSet<string> _recognizedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz", ".txz", ".zst", ".tzst", ".lzma"
    };

    public static bool IsRecognizedArchiveExtension(string fileName) =>
        _recognizedExtensions.Contains(Path.GetExtension(fileName));

    public static ArchiveFormat Detect(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> header = stackalloc byte[262];
            int read = fs.Read(header);

            if (read >= 4 && header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04)
                return ArchiveFormat.Zip;

            if (read >= 2 && header[0] == 0x1F && header[1] == 0x8B)
                return ArchiveFormat.GZip;

            if (read >= 3 && header[0] == 0x42 && header[1] == 0x5A && header[2] == 0x68)
                return ArchiveFormat.Bz2;

            // RAR: "Rar!" (52 61 72 21) — shared by both RAR4 and RAR5 signatures, same 4-byte
            // check ZipArchiveService.GetKnownArchiveReason already uses.
            if (read >= 4 && header[0] == 0x52 && header[1] == 0x61 && header[2] == 0x72 && header[3] == 0x21)
                return ArchiveFormat.Rar;

            if (read >= 6 && header[0] == 0x37 && header[1] == 0x7A && header[2] == 0xBC
                && header[3] == 0xAF && header[4] == 0x27 && header[5] == 0x1C)
                return ArchiveFormat.SevenZip;

            if (read >= 6 && header[0] == 0xFD && header[1] == 0x37 && header[2] == 0x7A
                && header[3] == 0x58 && header[4] == 0x5A && header[5] == 0x00)
                return ArchiveFormat.Xz;

            if (read >= 4 && header[0] == 0x28 && header[1] == 0xB5 && header[2] == 0x2F && header[3] == 0xFD)
                return ArchiveFormat.Zstd;

            // Plain/uncompressed tar has no magic number at offset 0 — the real signature is
            // the "ustar" string at header offset 257 (POSIX ustar format).
            if (read >= 262
                && header[257] == (byte)'u' && header[258] == (byte)'s' && header[259] == (byte)'t'
                && header[260] == (byte)'a' && header[261] == (byte)'r')
                return ArchiveFormat.Tar;

            // NOTE: raw .lzma (LZMA_Alone) streams have no reliable magic number — cannot be
            // distinguished from arbitrary binary data by header bytes alone. Files in this
            // format will classify as Unknown until a future extension-assisted fallback is
            // added; this is a known gap, not an oversight.

            return ArchiveFormat.Unknown;
        }
        catch
        {
            return ArchiveFormat.Unknown;
        }
    }
}
