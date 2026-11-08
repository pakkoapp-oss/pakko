using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// Detects archive format from magic bytes — no extension reliance, matching this
/// codebase's existing ZIP signature-sniffing convention (see ZipArchiveService.IsZipFile),
/// except for .lzma, which has no magic number (T-F398).
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
    // T-F264: FormatListConsistencyTests compares these with ShellExtUtils.cpp and
    // Package.appxmanifest, so the three copies can no longer drift silently.
    private static readonly string[] _zipExtensions =
        [".zip", ".jar", ".war", ".ear", ".apk", ".asice", ".asics", ".bdoc"];

    // T-F212: each recognized extension with the format it names — the one list; the set below is its keys.
    private static readonly Dictionary<string, ArchiveFormat> _formatByExtension = new(
        [
            .. _zipExtensions.Select(e => KeyValuePair.Create(e, ArchiveFormat.Zip)),
            KeyValuePair.Create(".rar", ArchiveFormat.Rar),
            KeyValuePair.Create(".7z", ArchiveFormat.SevenZip),
            KeyValuePair.Create(".tar", ArchiveFormat.Tar),
            KeyValuePair.Create(".gz", ArchiveFormat.GZip),
            KeyValuePair.Create(".tgz", ArchiveFormat.GZip),
            KeyValuePair.Create(".bz2", ArchiveFormat.Bz2),
            KeyValuePair.Create(".tbz2", ArchiveFormat.Bz2),
            KeyValuePair.Create(".xz", ArchiveFormat.Xz),
            KeyValuePair.Create(".txz", ArchiveFormat.Xz),
            KeyValuePair.Create(".zst", ArchiveFormat.Zstd),
            KeyValuePair.Create(".tzst", ArchiveFormat.Zstd),
            KeyValuePair.Create(".lzma", ArchiveFormat.Lzma),
            // T-F442: read-only through tar.exe. Not ".a" (static libraries) or ".pkg" (several formats).
            KeyValuePair.Create(".iso", ArchiveFormat.Iso),
            KeyValuePair.Create(".cab", ArchiveFormat.Cab),
            KeyValuePair.Create(".cpio", ArchiveFormat.Cpio),
            KeyValuePair.Create(".deb", ArchiveFormat.Ar),
            KeyValuePair.Create(".ar", ArchiveFormat.Ar),
            KeyValuePair.Create(".z", ArchiveFormat.Compress),
            KeyValuePair.Create(".taz", ArchiveFormat.Compress),
            KeyValuePair.Create(".lz", ArchiveFormat.Lzip),
            KeyValuePair.Create(".tlz", ArchiveFormat.Lzip),
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> _recognizedExtensions = new(_formatByExtension.Keys, StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyCollection<string> RecognizedExtensions => _recognizedExtensions;

    internal static IReadOnlyCollection<string> RecognizedZipExtensions => _zipExtensions;

    /// <summary>
    /// Fast, no-disk-I/O extension check — used only where <see cref="Detect"/>'s magic-byte
    /// sniff can't run yet (e.g. an in-archive entry not extracted). Not the security boundary.
    /// </summary>
    public static bool IsRecognizedArchiveExtension(string fileName) =>
        _recognizedExtensions.Contains(Path.GetExtension(fileName));

    /// <summary>
    /// The format an archive's extension names, or <see cref="ArchiveFormat.Unknown"/> — no disk
    /// I/O (T-F212). Not the security boundary: <see cref="Detect"/> still decides at run time.
    /// </summary>
    public static ArchiveFormat FormatFromExtension(string fileName) =>
        _formatByExtension.GetValueOrDefault(Path.GetExtension(fileName), ArchiveFormat.Unknown);

    /// <summary>
    /// Detects an archive's real format from its magic bytes; an .lzma name decides only when the
    /// bytes name no format (T-F398). Returns Unknown on any read failure.
    /// </summary>
    public static ArchiveFormat Detect(string path)
    {
        try
        {
            using FileStream fs = File.OpenRead(path);
            Span<byte> header = stackalloc byte[262];
            int read = fs.Read(header);
            ArchiveFormat format = DetectFormatFromHeader(header, read);
            if (format == ArchiveFormat.Unknown)
                format = DetectDiscImage(fs);
            return format == ArchiveFormat.Unknown && NamesLzmaAlone(path) && IsLzmaAloneHeader(header, read)
                ? ArchiveFormat.Lzma
                : format;
        }
        catch
        {
            return ArchiveFormat.Unknown;
        }
    }

    // T-F398: an .lzma (LZMA_Alone) stream has no magic number, so the name decides, and only
    // when the bytes name no other format. A wrong guess reaches tar.exe in the sandbox, which
    // refuses it; before this, Pakko could not read the tar.lzma it writes.
    private const int LzmaAloneHeaderLength = 13;

    // The properties byte is (pb * 5 + lp) * 9 + lc with lc < 9, lp < 5, pb < 5.
    private const byte LzmaPropertiesLimit = 9 * 5 * 5;

    // T-F457: Slackware's .tlz is tar.lzma; an lzip .tlz has a magic number and is decided above.
    private static bool NamesLzmaAlone(string path) =>
        FormatFromExtension(path) == ArchiveFormat.Lzma
        || Path.GetExtension(path).Equals(".tlz", StringComparison.OrdinalIgnoreCase);

    private static bool IsLzmaAloneHeader(ReadOnlySpan<byte> h, int read) =>
        read >= LzmaAloneHeaderLength && h[0] < LzmaPropertiesLimit;

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
        if (IsCabSignature(header, read)) return ArchiveFormat.Cab;
        if (IsCpioSignature(header, read)) return ArchiveFormat.Cpio;
        if (IsArSignature(header, read)) return ArchiveFormat.Ar;
        if (IsCompressSignature(header, read)) return ArchiveFormat.Compress;
        if (IsLzipSignature(header, read)) return ArchiveFormat.Lzip;
        return ArchiveFormat.Unknown;
    }

    // T-F442: each signature is checked past its magic, so a text or data file that merely starts
    // with the same bytes is not handed to tar.exe.

    // "MSCF", a zero reserved field, and format version 1.3 at offset 24.
    private static bool IsCabSignature(ReadOnlySpan<byte> h, int read) =>
        read >= 26 && h[..4].SequenceEqual("MSCF"u8) && h[4..8].IndexOfAnyExcept((byte)0) < 0 && h[24] == 3 && h[25] == 1;

    // ASCII cpio (odc "070707", newc "070701", crc "070702") and its next field in digits; the
    // binary variant is not taken (a two-byte magic, and tar.exe writes odc/newc).
    private static bool IsCpioSignature(ReadOnlySpan<byte> h, int read) =>
        read >= 12
            && (h[..6].SequenceEqual("070707"u8) || h[..6].SequenceEqual("070701"u8) || h[..6].SequenceEqual("070702"u8))
            && h[6..12].IndexOfAnyExcept(HexDigits) < 0;

    private static readonly System.Buffers.SearchValues<byte> HexDigits = System.Buffers.SearchValues.Create("0123456789ABCDEFabcdef"u8);

    private static bool IsArSignature(ReadOnlySpan<byte> h, int read) =>
        read >= 8 && h[..8].SequenceEqual("!<arch>\n"u8);

    // 1F 9D, then a flags byte: 9 to 16 code bits, the two reserved bits clear.
    private static bool IsCompressSignature(ReadOnlySpan<byte> h, int read) =>
        read >= 3 && h[0] == 0x1F && h[1] == 0x9D && (h[2] & 0x60) == 0 && (h[2] & 0x1F) is >= 9 and <= 16;

    // "LZIP" and version 1, the only one.
    private static bool IsLzipSignature(ReadOnlySpan<byte> h, int read) =>
        read >= 5 && h[..4].SequenceEqual("LZIP"u8) && h[4] == 1;

    private const int DiscSectorSize = 2048;
    private const int FirstVolumeDescriptor = 16 * DiscSectorSize;

    // ECMA-167's volume recognition sequence lies in these sectors with the ISO 9660 descriptors.
    private const int VolumeDescriptorsRead = 16;

    // T-F442: an ISO 9660 or UDF image names itself in the descriptors from sector 16 on, each
    // identifier at byte 1 of its sector. An NSR descriptor among them makes the image UDF,
    // refused: libarchive reads only the ISO 9660 side, which on such an image is a readme or 8.3
    // names (docs/DECISIONS.md, "T-F442 - scope and the measurements before the code").
    private static ArchiveFormat DetectDiscImage(FileStream fs)
    {
        bool iso = false;
        Span<byte> identifier = stackalloc byte[6];
        for (int sector = 0; sector < VolumeDescriptorsRead; sector++)
        {
            long at = FirstVolumeDescriptor + (long)sector * DiscSectorSize;
            if (at + identifier.Length > fs.Length)
                break;
            fs.Position = at;
            fs.ReadExactly(identifier);
            ArchiveFormat? named = DescriptorFormat(identifier[1..]);
            if (named == ArchiveFormat.Udf)
                return ArchiveFormat.Udf;
            if (named == ArchiveFormat.Iso)
                iso = true;
            else if (named is null)
                break;
        }
        return iso ? ArchiveFormat.Iso : ArchiveFormat.Unknown;
    }

    /// <summary>The bytes a stream must start with to hold every descriptor <see cref="DetectDiscImage(ReadOnlySpan{byte})"/> reads.</summary>
    internal const int DiscImageStartLength = FirstVolumeDescriptor + VolumeDescriptorsRead * DiscSectorSize;

    /// <summary>
    /// T-F457: <see cref="ArchiveFormat.Iso"/>, <see cref="ArchiveFormat.Udf"/> or
    /// <see cref="ArchiveFormat.Unknown"/> for the first bytes of a stream that has no file to seek
    /// in (the start of a gzip's content, <see cref="BareGzip"/>), by the same reading as a file's.
    /// </summary>
    internal static ArchiveFormat DetectDiscImage(ReadOnlySpan<byte> start)
    {
        bool iso = false;
        for (int sector = 0; sector < VolumeDescriptorsRead; sector++)
        {
            int at = FirstVolumeDescriptor + sector * DiscSectorSize;
            if (start.Length - at < 6)
                break;
            ArchiveFormat? named = DescriptorFormat(start.Slice(at + 1, 5));
            if (named == ArchiveFormat.Udf)
                return ArchiveFormat.Udf;
            if (named == ArchiveFormat.Iso)
                iso = true;
            else if (named is null)
                break;
        }
        return iso ? ArchiveFormat.Iso : ArchiveFormat.Unknown;
    }

    // Udf for an NSR descriptor, Iso for CD001, Unknown for another descriptor of the sequence,
    // null for anything else: the descriptors have ended.
    private static ArchiveFormat? DescriptorFormat(ReadOnlySpan<byte> id)
    {
        if (id.SequenceEqual("NSR02"u8) || id.SequenceEqual("NSR03"u8))
            return ArchiveFormat.Udf;
        if (id.SequenceEqual("CD001"u8))
            return ArchiveFormat.Iso;
        if (id.SequenceEqual("BEA01"u8) || id.SequenceEqual("TEA01"u8) || id.SequenceEqual("BOOT2"u8) || id.SequenceEqual("CDW02"u8))
            return ArchiveFormat.Unknown;
        return null;
    }

    // ECMA-119 primary volume descriptor: the volume's size in logical blocks (both-endian, the
    // little-endian half first), the block size, and the root directory record's extent and length.
    private const int VolumeSpaceSizeOffset = 80;
    private const int LogicalBlockSizeOffset = 128;
    private const int RootExtentOffset = 156 + 2;
    private const int RootLengthOffset = 156 + 10;

    // libarchive's ISO 9660 reader bids only with RESERVED_AREA + 8 * LOGICAL_BLOCK_SIZE readable
    // (archive_read_support_format_iso9660.c, 3.5.2 and 3.8.1); a shorter file falls to the tar
    // reader, which takes the zero system area for an empty archive.
    private const int Iso9660BidBytes = 24 * DiscSectorSize;

    /// <summary>
    /// T-F442, T-F457: true when <paramref name="archive"/> starts with an ISO 9660 primary volume
    /// descriptor whose declared volume is longer than the file, and the file ends before tar.exe can
    /// read the directory tree: before libarchive's reader bids, or before the root directory ends.
    /// tar.exe reads such an image as an empty archive with exit code 0. A shorter image past that
    /// point is tar.exe's to judge: it reads it in full when only padding or a CD-R's run-out sectors
    /// are missing, and fails when data is (measured on bsdtar 3.8.9 and 3.5.2). False for anything
    /// else, including a file too short to hold a descriptor; a read error propagates as
    /// <see cref="IOException"/>.
    /// </summary>
    internal static bool IsDiscImageCut(FileStream archive)
    {
        Span<byte> descriptor = stackalloc byte[RootLengthOffset + 4];
        if (archive.Length < FirstVolumeDescriptor + descriptor.Length
            || RandomAccess.Read(archive.SafeFileHandle, descriptor, FirstVolumeDescriptor) != descriptor.Length
            || descriptor[0] != 1 || !descriptor[1..6].SequenceEqual("CD001"u8))
            return false;
        long blocks = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(descriptor[VolumeSpaceSizeOffset..]);
        long blockSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(descriptor[LogicalBlockSizeOffset..]);
        if (blocks * blockSize <= archive.Length)
            return false;
        // At most 2^32 * 2^16 + 2^32: no overflow in a long, whatever the record says.
        long rootEnd = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(descriptor[RootExtentOffset..]) * blockSize
            + System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(descriptor[RootLengthOffset..]);
        return archive.Length < Math.Max(Iso9660BidBytes, rootEnd);
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
            byte[] data = ReadHeaderWindow(path);
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
            byte[] data = ReadHeaderWindow(path);
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
                // T-F249: a size that is not positive would move the position backwards (a
                // hostile -5 re-read the same record forever).
                if (recordSize <= 0)
                    return false;
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

    // T-F249: RAR5 keeps the main archive header and the first file header in the first bytes
    // of the file — both checks used to read the whole file (1.5 GB of memory for a 1.5 GB RAR,
    // and a silent "false" above 2 GB, where ReadAllBytes throws). A header larger than this
    // window parses as "not detected", the same as any other malformed input.
    private const int HeaderWindowBytes = 64 * 1024;

    private static byte[] ReadHeaderWindow(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        byte[] buffer = new byte[(int)Math.Min(HeaderWindowBytes, stream.Length)];
        stream.ReadExactly(buffer);
        return buffer;
    }

    // Bounds checked on each line: the position stays inside the data, and at most five 7-bit
    // groups are read (35 bits covers every int); anything else throws and the caller's catch
    // answers "not detected".
    private static int ReadVInt(byte[] data, ref int pos)
    {
        int result = 0;
        for (int shift = 0; shift <= 28; shift += 7)
        {
            if (pos < 0 || pos >= data.Length)
                throw new InvalidDataException("Malformed RAR5 vint.");
            byte b = data[pos++];
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;
        }
        throw new InvalidDataException("Malformed RAR5 vint.");
    }
}
