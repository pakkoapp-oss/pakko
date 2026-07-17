namespace Archiver.Core.Services.Zip;

/// <summary>
/// Packs/unpacks the MS-DOS date+time fields used in a ZIP local file header's
/// mod-time/mod-date fields. Mirrors the exact bit layout .NET's own
/// <see cref="System.IO.Compression.ZipArchiveEntry.LastWriteTime"/> uses internally
/// (System.IO.Compression.ZipHelper.DateTimeToDosTime), so a hand-rolled entry written by
/// <see cref="ZipEntryWriter"/> is byte-identical to one written via <c>ZipArchiveEntry</c> for
/// the same input <see cref="DateTime"/>. Out-of-range dates clamp to the DOS-representable
/// bounds (1980-01-01 .. 2107-12-31 23:59:58), same as .NET does.
/// </summary>
internal static class DosDateTime
{
    private static readonly DateTime MinDosDateTime = new(1980, 1, 1, 0, 0, 0);
    private static readonly DateTime MaxDosDateTime = new(2107, 12, 31, 23, 59, 58);

    /// <summary>
    /// Encodes <paramref name="dateTime"/> into a packed 32-bit value: high 16 bits are the
    /// DOS date (year-since-1980 in bits 15-9, month in bits 8-5, day in bits 4-0), low 16 bits
    /// are the DOS time (hour in bits 15-11, minute in bits 10-5, seconds/2 in bits 4-0).
    /// </summary>
    public static uint Encode(DateTime dateTime)
    {
        DateTime clamped = dateTime < MinDosDateTime
            ? MinDosDateTime
            : dateTime > MaxDosDateTime
                ? MaxDosDateTime
                : dateTime;

        return (uint)(
            ((clamped.Year - 1980) & 0x7F) << 25 |
            clamped.Month << 21 |
            clamped.Day << 16 |
            clamped.Hour << 11 |
            clamped.Minute << 5 |
            clamped.Second / 2);
    }

    /// <summary>Unpacks a value produced by <see cref="Encode"/> back into a <see cref="DateTime"/>.</summary>
    public static DateTime Decode(uint packed)
    {
        int year = (int)((packed >> 25) & 0x7F) + 1980;
        int month = (int)(packed >> 21) & 0x0F;
        int day = (int)(packed >> 16) & 0x1F;
        int hour = (int)(packed >> 11) & 0x1F;
        int minute = (int)(packed >> 5) & 0x3F;
        int second = (int)(packed & 0x1F) * 2;

        return new DateTime(year, month, day, hour, minute, second);
    }
}
