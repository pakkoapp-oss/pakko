namespace Archiver.Core.Services;

/// <summary>
/// T-F298: sets an extracted file's or folder's modification time, best-effort. A time is metadata:
/// failing to set it (a locked file, a denied folder, a date Windows cannot store) never fails the
/// entry, so every exception the setters document is swallowed here, argument errors included.
/// </summary>
internal static class FileTimes
{
    public static void TrySetFile(string path, DateTime utc)
    {
        try { File.SetLastWriteTimeUtc(path, utc); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { /* best-effort: the content is written, only its time is not */ }
    }

    public static void TrySetDirectory(string path, DateTime utc)
    {
        try { Directory.SetLastWriteTimeUtc(path, utc); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { /* best-effort: the folder exists, only its time is not set */ }
    }
}
