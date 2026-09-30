using System.Globalization;
using System.Runtime.InteropServices;

namespace Archiver.Core.Services;

/// <summary>
/// T-F214: reads the modified date from a "tar -tvf" line. tar.exe formats it with the C
/// runtime's strftime in the user's locale: "%b %d %H:%M" within half a year of now, else
/// "%b %d  %Y" (date only). The month abbreviations come from Windows' own locale data, the same
/// source the C runtime uses — .NET's CultureInfo is ICU-backed and differs (e.g. "вер" for
/// tar.exe's "Вер"). A date that cannot be read is null, never a guess.
/// </summary>
internal static partial class TarListingDate
{
    // Mode, link count, owner, group, size — then the date columns.
    private const int DateColumn = 5;
    private const uint LocaleSAbbrevMonthName1 = 0x00000044;
    private const int LocaleNameMaxLength = 85;
    private static readonly DateTime UnixEpochEast = new(1970, 1, 1);
    private static readonly DateTime UnixEpochWest = new(1969, 12, 31);

    /// <summary>The user locale's twelve month abbreviations, or none when Windows cannot say.</summary>
    public static IReadOnlyList<string> UserMonthNames { get; } = ReadUserMonthNames();

    public static DateTime? Parse(string line, IReadOnlyList<string> monthNames, DateTime now)
    {
        string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (int month = 1; month <= monthNames.Count; month++)
        {
            string[] monthFields = monthNames[month - 1].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            int dayColumn = DateColumn + monthFields.Length;
            if (monthFields.Length == 0 || fields.Length <= dayColumn + 1)
                continue;
            if (!fields.AsSpan(DateColumn, monthFields.Length).SequenceEqual(monthFields))
                continue;
            return ParseDayAndTime(month, fields[dayColumn], fields[dayColumn + 1], now);
        }
        return null;
    }

    private static DateTime? ParseDayAndTime(int month, string dayText, string timeOrYear, DateTime now)
    {
        if (!int.TryParse(dayText, NumberStyles.None, CultureInfo.InvariantCulture, out int day))
            return null;

        if (int.TryParse(timeOrYear, NumberStyles.None, CultureInfo.InvariantCulture, out int year))
        {
            // An entry with no stored time (7z -mtm=off) lists as the local Unix epoch.
            DateTime? date = TryCreate(year, month, day, 0, 0);
            return date == UnixEpochEast || date == UnixEpochWest ? null : date;
        }

        if (!TimeOnly.TryParseExact(timeOrYear, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time))
            return null;

        // The year is left out only within half a year of now, so the nearest candidate is it.
        DateTime? nearest = null;
        for (int candidateYear = now.Year - 1; candidateYear <= now.Year + 1; candidateYear++)
        {
            DateTime? candidate = TryCreate(candidateYear, month, day, time.Hour, time.Minute);
            if (candidate is { } value && (nearest is null || (value - now).Duration() < (nearest.Value - now).Duration()))
                nearest = value;
        }
        return nearest;
    }

    private static DateTime? TryCreate(int year, int month, int day, int hour, int minute) =>
        year is >= 1 and <= 9999 && day >= 1 && day <= DateTime.DaysInMonth(year, month)
            ? new DateTime(year, month, day, hour, minute, 0)
            : null;

    private static string[] ReadUserMonthNames()
    {
        char[] localeName = new char[LocaleNameMaxLength];
        if (GetUserDefaultLocaleName(localeName, localeName.Length) <= 0)
            return [];
        string locale = new string(localeName).TrimEnd('\0');

        var names = new string[12];
        char[] buffer = new char[80];
        for (int i = 0; i < names.Length; i++)
        {
            int written = GetLocaleInfoEx(locale, LocaleSAbbrevMonthName1 + (uint)i, buffer, buffer.Length);
            if (written <= 1)
                return [];
            names[i] = new string(buffer, 0, written - 1);
        }
        return names;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetUserDefaultLocaleName([Out] char[] localeName, int length);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetLocaleInfoEx(string localeName, uint type, [Out] char[] data, int length);
}
