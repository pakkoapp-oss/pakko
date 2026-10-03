using System.Globalization;
using System.Text;

namespace Archiver.App.Core;

/// <summary>
/// The size and date lines of the App's conflict dialog (T-F220 item 2), in the shape the Explorer
/// operation window uses. The App sets the "modified" word once at startup; English until then.
/// </summary>
public static class ConflictText
{
    private static CompositeFormat _modified = CompositeFormat.Parse("modified {0}");

    /// <summary>Called once by the App; the template takes the local date and time as {0}.</summary>
    public static void Configure(string modified) => _modified = CompositeFormat.Parse(modified);

    /// <summary>"2,0 KB · modified 03.10.2026 12:30", whichever part is known; null when neither is.</summary>
    public static string? Details(long? size, DateTimeOffset? modified)
    {
        string? date = modified is { } m
            ? string.Format(CultureInfo.CurrentCulture, _modified, m.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
            : null;
        return (size, date) switch
        {
            ({ } s, { } d) => DisplayText.FormatSize(s) + " · " + d,
            ({ } s, null) => DisplayText.FormatSize(s),
            (null, { } d) => d,
            _ => null,
        };
    }

    /// <summary>The existing file's size and time, best effort (unknown when it cannot be read).</summary>
    public static (long? Size, DateTimeOffset? Modified) Existing(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? (file.Length, new DateTimeOffset(file.LastWriteTimeUtc)) : (null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (null, null);
        }
    }
}
