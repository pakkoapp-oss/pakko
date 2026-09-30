using System.Buffers.Binary;

namespace Archiver.Core.Services.Zip;

/// <summary>
/// T-F298: an entry's modification time from its central-directory extra block, by 7-Zip's rule —
/// the NTFS record (0x000A) first, then the Info-ZIP extended timestamp (0x5455). Never throws: the
/// block is attacker-controlled, and a bad time field must fall back to the DOS time, not fail the
/// extraction.
/// </summary>
internal static class ZipExtendedTime
{
    private const ushort NtfsExtraId = 0x000A;
    private const ushort ExtendedTimestampExtraId = 0x5455;
    private const ushort NtfsTimesTag = 0x0001;
    private const int NtfsTimesTagSize = 24; // mtime, atime, ctime — three FILETIMEs
    private const int NtfsReservedSize = 4;
    private static readonly long MaxFileTime = DateTime.MaxValue.ToFileTimeUtc();

    /// <summary>The UTC modification time, or null when neither record holds a usable one.</summary>
    public static DateTime? TryReadModifiedUtc(ReadOnlySpan<byte> extra)
    {
        DateTime? ntfs = null, unix = null;
        int position = 0;
        while (position <= extra.Length - 4)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra[position..]);
            int size = BinaryPrimitives.ReadUInt16LittleEndian(extra[(position + 2)..]);
            if (size > extra.Length - position - 4)
                break; // a record running past the block ends the walk, as in 7-Zip
            ReadOnlySpan<byte> data = extra.Slice(position + 4, size);
            if (id == NtfsExtraId)
                ntfs ??= ReadNtfs(data);
            else if (id == ExtendedTimestampExtraId)
                unix ??= ReadUnix(data);
            position += 4 + size;
        }
        return ntfs ?? unix;
    }

    private static DateTime? ReadNtfs(ReadOnlySpan<byte> data)
    {
        int position = NtfsReservedSize;
        while (position <= data.Length - 4)
        {
            ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
            int size = BinaryPrimitives.ReadUInt16LittleEndian(data[(position + 2)..]);
            if (size > data.Length - position - 4)
                return null;
            if (tag == NtfsTimesTag && size >= NtfsTimesTagSize)
            {
                long fileTime = BinaryPrimitives.ReadInt64LittleEndian(data[(position + 4)..]);
                return fileTime > 0 && fileTime <= MaxFileTime ? DateTime.FromFileTimeUtc(fileTime) : null;
            }
            position += 4 + size;
        }
        return null;
    }

    // Flags bit 0 = mtime present; in the central directory only the mtime follows the flags.
    private static DateTime? ReadUnix(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5 || (data[0] & 1) == 0)
            return null;
        int seconds = BinaryPrimitives.ReadInt32LittleEndian(data[1..]);
        return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
    }
}
