using System.Globalization;

namespace Archiver.App.Core;

/// <summary>
/// The words the App's lists show for items (T-F198): a folder's type, a file without an
/// extension, and size units. App.Core has no resource loader, so the App sets them once at
/// startup from its own resources; until then they are English, which is what the tests see.
/// </summary>
public static class DisplayText
{
    private static string _bytes = "{0} B";
    private static string _kilobytes = "{0} KB";
    private static string _megabytes = "{0} MB";
    private static string _gigabytes = "{0} GB";

    /// <summary>The Type column of a folder.</summary>
    public static string Folder { get; private set; } = "Folder";

    /// <summary>The Type column of a file without an extension.</summary>
    public static string File { get; private set; } = "File";

    /// <summary>Called once by the App before any list shows; every unit template takes the number as {0}.</summary>
    public static void Configure(string folder, string file, string bytes, string kilobytes, string megabytes, string gigabytes)
    {
        Folder = folder;
        File = file;
        _bytes = bytes;
        _kilobytes = kilobytes;
        _megabytes = megabytes;
        _gigabytes = gigabytes;
    }

    /// <summary>A byte count as B/KB/MB/GB in the user's number format.</summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => Format(_bytes, bytes.ToString(CultureInfo.CurrentCulture)),
        < 1024 * 1024 => Format(_kilobytes, (bytes / 1024.0).ToString("F1", CultureInfo.CurrentCulture)),
        < 1024L * 1024 * 1024 => Format(_megabytes, (bytes / (1024.0 * 1024)).ToString("F1", CultureInfo.CurrentCulture)),
        _ => Format(_gigabytes, (bytes / (1024.0 * 1024 * 1024)).ToString("F1", CultureInfo.CurrentCulture)),
    };

    private static string Format(string template, string number) =>
        string.Format(CultureInfo.CurrentCulture, template, number);
}
