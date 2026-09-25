using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Archiver.Core.IO;

namespace Archiver.Core.Services.Zip;

/// <summary>
/// T-F234: decodes a ZIP entry name by 7-Zip's rule (NanaZip <c>ZipItem.cpp</c>
/// <c>GetUnicodeString</c>, <c>ZipItem.h</c> <c>GetCodePage</c>):
/// UTF-8 flag (general-purpose bit 11) -> UTF-8; else a valid Info-ZIP Unicode Path extra (0x7075)
/// -> its UTF-8 name; else by the central record's host OS: Unix -> UTF-8, FAT/NTFS -> OEM code
/// page, anything else -> ANSI code page. .NET's own reader uses UTF-8 for all of these, which
/// garbles every legacy name and can merge two of them into one.
/// </summary>
internal static class ZipEntryNameDecoder
{
    private const ushort Utf8Flag = 0x0800;
    private const ushort UnicodePathExtraId = 0x7075;
    private const int UnicodePathHeaderLength = 5; // version(1) + CRC-32 of the raw name(4)
    private const byte HostFat = 0;
    private const byte HostUnix = 3;
    private const byte HostNtfs = 11;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Decode(
        ReadOnlySpan<byte> rawName, ushort flags, byte hostOs, ReadOnlySpan<byte> centralExtra, ZipNameCodePages codePages) =>
        DecodeRaw(rawName, flags, hostOs, centralExtra, codePages).Replace('\\', '/');

    // '\' is normalized on decoded characters, never raw bytes: in DBCS code pages (932/936/949/950)
    // 0x5C can be the second byte of a character. 7-Zip treats '\' as a separator for non-Unix
    // hosts; Pakko does for every host, so all security checks see one form of the path.
    private static string DecodeRaw(
        ReadOnlySpan<byte> rawName, ushort flags, byte hostOs, ReadOnlySpan<byte> centralExtra, ZipNameCodePages codePages)
    {
        if ((flags & Utf8Flag) != 0)
            return Encoding.UTF8.GetString(rawName);

        if (TryGetUnicodePath(rawName, centralExtra, out string? unicodeName))
            return unicodeName;

        return hostOs switch
        {
            HostUnix => Encoding.UTF8.GetString(rawName),
            HostFat or HostNtfs => codePages.Oem.GetString(rawName),
            _ => codePages.Ansi.GetString(rawName),
        };
    }

    // A malformed extra block is ignored rather than fatal (7-Zip does the same): the name then
    // falls through to the host-OS rule, so an archive with junk extras still opens.
    private static bool TryGetUnicodePath(ReadOnlySpan<byte> rawName, ReadOnlySpan<byte> extra, [NotNullWhen(true)] out string? name)
    {
        name = null;
        int position = 0;
        while (extra.Length - position >= 4)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra[position..]);
            int size = BinaryPrimitives.ReadUInt16LittleEndian(extra[(position + 2)..]);
            if (size > extra.Length - position - 4)
                return false;

            if (id == UnicodePathExtraId)
                return TryReadUnicodePathRecord(rawName, extra.Slice(position + 4, size), out name);
            position += 4 + size;
        }
        return false;
    }

    private static bool TryReadUnicodePathRecord(ReadOnlySpan<byte> rawName, ReadOnlySpan<byte> record, [NotNullWhen(true)] out string? name)
    {
        name = null;
        if (record.Length < UnicodePathHeaderLength || record[0] > 1)
            return false;
        var rawNameCrc = new Crc32.Accumulator();
        rawNameCrc.Update(rawName);
        if (BinaryPrimitives.ReadUInt32LittleEndian(record[1..]) != rawNameCrc.Finish())
            return false;

        ReadOnlySpan<byte> utf8Name = record[UnicodePathHeaderLength..];
        if (utf8Name.Contains((byte)0))
            return false;
        try
        {
            name = StrictUtf8.GetString(utf8Name);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
