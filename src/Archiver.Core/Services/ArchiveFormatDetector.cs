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
    // T-F131: .jar/.war/.ear (Java) and .apk (Android) are real ZIP-format containers (PK\x03\x04
    // signature) — Detect() already classified them as ArchiveFormat.Zip via magic bytes with no
    // change needed there. This list only gates the *extension-based, no-disk-I/O* fast paths
    // (Explorer context menu, FileTypeAssociation, this recognized-extension check) — kept
    // deliberately narrower than "every possible ZIP container" (no Office/OpenDocument/.epub) per
    // the user's explicit choice; see DECISIONS.md's T-F131 entry.
    // T-F133: .asice/.asics (ASiC-E/ASiC-S, ETSI TS 102 918 signed containers) and .bdoc (Estonia's
    // national ASiC-E profile) — also real ZIP-format containers, same reasoning as T-F131. See
    // DECISIONS.md's T-F133 entry.
    private static readonly HashSet<string> _recognizedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".jar", ".war", ".ear", ".apk", ".asice", ".asics", ".bdoc",
        ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz", ".txz", ".zst", ".tzst", ".lzma"
    };

    /// <summary>
    /// Fast, no-disk-I/O extension check — used only where <see cref="Detect"/>'s magic-byte
    /// sniff can't run yet (e.g. an in-archive entry not extracted). Not the security boundary.
    /// </summary>
    public static bool IsRecognizedArchiveExtension(string fileName) =>
        _recognizedExtensions.Contains(Path.GetExtension(fileName));

    /// <summary>Detects an archive's real format from its magic bytes. Returns Unknown on any read failure.</summary>
    public static ArchiveFormat Detect(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> header = stackalloc byte[262];
            int read = fs.Read(header);
            return DetectFormatFromHeader(header, read);
        }
        catch
        {
            return ArchiveFormat.Unknown;
        }
    }

    // NOTE: raw .lzma (LZMA_Alone) streams have no reliable magic number — cannot be
    // distinguished from arbitrary binary data by header bytes alone. Files in this format will
    // classify as Unknown until a future extension-assisted fallback is added; this is a known
    // gap, not an oversight.
    private static ArchiveFormat DetectFormatFromHeader(ReadOnlySpan<byte> header, int read)
    {
        if (IsZipSignature(header, read)) return ArchiveFormat.Zip;
        if (IsGZipSignature(header, read)) return ArchiveFormat.GZip;
        if (IsBz2Signature(header, read)) return ArchiveFormat.Bz2;
        if (IsRarSignature(header, read)) return ArchiveFormat.Rar;
        if (IsSevenZipSignature(header, read)) return ArchiveFormat.SevenZip;
        if (IsXzSignature(header, read)) return ArchiveFormat.Xz;
        if (IsZstdSignature(header, read)) return ArchiveFormat.Zstd;
        if (IsTarSignature(header, read)) return ArchiveFormat.Tar;
        return ArchiveFormat.Unknown;
    }

    private static bool IsZipSignature(ReadOnlySpan<byte> h, int read) =>
        read >= 4 && h[0] == 0x50 && h[1] == 0x4B && h[2] == 0x03 && h[3] == 0x04;

    private static bool IsGZipSignature(ReadOnlySpan<byte> h, int read) =>
        read >= 2 && h[0] == 0x1F && h[1] == 0x8B;

    private static bool IsBz2Signature(ReadOnlySpan<byte> h, int read) =>
        read >= 3 && h[0] == 0x42 && h[1] == 0x5A && h[2] == 0x68;

    // RAR: "Rar!" (52 61 72 21) — shared by both RAR4 and RAR5 signatures, same 4-byte check
    // ZipArchiveService.GetKnownArchiveReason already uses.
    private static bool IsRarSignature(ReadOnlySpan<byte> h, int read) =>
        read >= 4 && h[0] == 0x52 && h[1] == 0x61 && h[2] == 0x72 && h[3] == 0x21;

    private static bool IsSevenZipSignature(ReadOnlySpan<byte> h, int read) =>
        read >= 6 && h[0] == 0x37 && h[1] == 0x7A && h[2] == 0xBC && h[3] == 0xAF && h[4] == 0x27 && h[5] == 0x1C;

    private static bool IsXzSignature(ReadOnlySpan<byte> h, int read) =>
        read >= 6 && h[0] == 0xFD && h[1] == 0x37 && h[2] == 0x7A && h[3] == 0x58 && h[4] == 0x5A && h[5] == 0x00;

    private static bool IsZstdSignature(ReadOnlySpan<byte> h, int read) =>
        read >= 4 && h[0] == 0x28 && h[1] == 0xB5 && h[2] == 0x2F && h[3] == 0xFD;

    // Plain/uncompressed tar has no magic number at offset 0 — the real signature is the "ustar"
    // string at header offset 257 (POSIX ustar format).
    private static bool IsTarSignature(ReadOnlySpan<byte> h, int read) =>
        read >= 262
            && h[257] == (byte)'u' && h[258] == (byte)'s' && h[259] == (byte)'t'
            && h[260] == (byte)'a' && h[261] == (byte)'r';

    // T-F113: RAR5 signature (8 bytes, ends "01 00"). Legacy RAR4's 7-byte signature (ends
    // "00" with no version byte) is deliberately not handled below — an accepted scope cut,
    // not an oversight; RAR4 is increasingly rare and IsEncryptedRar returns false for it,
    // falling through to today's existing (unclean but correct) tar.exe stderr behavior.
    private static readonly byte[] Rar5Signature = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00];

    /// <summary>
    /// Detects RAR5 "archive encryption header" (block type 4) as the very first block — the
    /// whole archive, including every filename, is unreadable without a password (`tar -tf`
    /// itself fails). Narrower than <see cref="IsEncryptedRar"/>: does NOT flag the common
    /// data-only-encryption case, where filenames are still readable and listing should still
    /// succeed (matching ZipArchiveService.ListEntriesAsync's parity for an encrypted ZIP — only
    /// extraction refuses, not browsing). Byte offsets confirmed empirically against real
    /// WinRAR-created fixtures — see DECISIONS.md's T-F113 entry. Returns false for any parse
    /// failure or non-RAR5 input.
    /// </summary>
    public static bool IsRarHeaderEncrypted(string path)
    {
        try
        {
            byte[] data = File.ReadAllBytes(path);
            if (!TryReadFirstBlockType(data, out int headerType, out _, out _))
                return false;

            return headerType == 4;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Detects RAR5 encryption directly from the archive's own block structure — no tar.exe
    /// invocation needed. RAR headers are never compressed (only file data is), so walking the
    /// block/extra-area chain is real, bounded metadata parsing, not decryption. Two cases:
    /// (1) the very first block has type 4 ("Archive encryption header") — see
    /// <see cref="IsRarHeaderEncrypted"/>; (2) the first File Header block (following the normal
    /// Main Archive Header) carries an "Encryption" extra-area record (type 1) — only that
    /// entry's data is password-protected, same first-entry-only fidelity
    /// ZipArchiveService.IsEncryptedZip already accepts for ZIP. Use this (not the narrower
    /// <see cref="IsRarHeaderEncrypted"/>) wherever extraction is about to be attempted — both
    /// cases fail extraction, only the header-encrypted case fails listing. Byte offsets and the
    /// RAR5 vint/block layout were confirmed empirically against real WinRAR-created fixtures
    /// (encrypted.rar, encrypted_headers.rar) — see DECISIONS.md's T-F113 entry. Returns false
    /// (not an error) for any parse failure or non-RAR5 input — this is a fast-path UX
    /// improvement, not the actual security boundary (ScanForUnsafeEntriesAsync's whole-archive
    /// pre-scan still runs regardless).
    /// </summary>
    public static bool IsEncryptedRar(string path)
    {
        try
        {
            byte[] data = File.ReadAllBytes(path);
            if (!TryReadFirstBlockType(data, out int mainHeaderType, out int mainHeaderTypePos, out int mainHeaderSize))
                return false;

            // Type 4 = Archive encryption header: presence alone means every further header
            // (including filenames) is encrypted — no need to parse its own fields.
            if (mainHeaderType == 4)
                return true;

            if (mainHeaderType != 1)
                return false; // not the expected Main Archive Header shape — give up cleanly

            int mainHeaderEnd = mainHeaderTypePos + mainHeaderSize;

            // First File Header block (type 2), immediately following the Main Archive Header —
            // same first-entry-only fidelity ZipArchiveService.IsEncryptedZip already accepts.
            int pos = mainHeaderEnd + 4; // that block's CRC32
            int fileHeaderSize = ReadVInt(data, ref pos);
            int fileHeaderTypePos = pos;
            int fileHeaderType = ReadVInt(data, ref pos);
            if (fileHeaderType != 2)
                return false; // first block after Main Archive Header isn't a File Header

            int fileHeaderFlags = ReadVInt(data, ref pos);
            int fileExtraAreaSize = 0;
            if ((fileHeaderFlags & 0x0001) != 0)
                fileExtraAreaSize = ReadVInt(data, ref pos); // extra area size present
            if ((fileHeaderFlags & 0x0002) != 0)
                ReadVInt(data, ref pos); // data area size (unused, just skipped over)
            if (fileExtraAreaSize == 0)
                return false; // no extra area at all — cannot carry an Encryption record

            int fileHeaderEnd = fileHeaderTypePos + fileHeaderSize;
            int extraPos = fileHeaderEnd - fileExtraAreaSize;

            while (extraPos < fileHeaderEnd)
            {
                int recordSize = ReadVInt(data, ref extraPos);
                int afterSizePos = extraPos;
                int recordType = ReadVInt(data, ref extraPos);
                if (recordType == 1) // Encryption record
                    return true;
                extraPos = afterSizePos + recordSize;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    // Reads the very first RAR5 block's HeaderType, given the signature has already been
    // confirmed present. Returns false if the file is too short or not RAR5 at all.
    private static bool TryReadFirstBlockType(byte[] data, out int headerType, out int headerTypePos, out int headerSize)
    {
        headerType = 0;
        headerTypePos = 0;
        headerSize = 0;

        if (data.Length < Rar5Signature.Length || !data.AsSpan(0, Rar5Signature.Length).SequenceEqual(Rar5Signature))
            return false;

        // Each RAR5 block is: CRC32(4 bytes) + HeaderSize(vint) + HeaderType(vint) + ...,
        // where HeaderSize counts bytes from HeaderType through the end of the block (not the
        // CRC32 or HeaderSize field itself) — so a block's end is always (position of its
        // HeaderType field) + HeaderSize.
        int pos = Rar5Signature.Length + 4; // skip signature + this block's CRC32
        headerSize = ReadVInt(data, ref pos);
        headerTypePos = pos;
        headerType = ReadVInt(data, ref pos);
        return true;
    }

    private static int ReadVInt(byte[] data, ref int pos)
    {
        int result = 0;
        int shift = 0;
        while (true)
        {
            byte b = data[pos++];
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                break;
            shift += 7;
        }
        return result;
    }
}
