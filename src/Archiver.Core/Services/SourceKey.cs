namespace Archiver.Core.Services;

/// <summary>
/// T-F511: the one key a source is known by when a frontend matches what it gave Core with what Core
/// reported (<c>SourceResult.Path</c>) and with what it listed before the operation: the full path
/// without trailing separators, case-insensitive by the callers' comparers. Core reports a source in
/// <see cref="SourcePathNormalizer"/>'s form, which this key maps onto the same string as the
/// spelling given. A name Windows trims ("a.txt.") has no key: by its full path it is its twin
/// "a.txt", and a source known by its twin's key could be listed or deleted as the twin (T-F509).
/// </summary>
public static class SourceKey
{
    /// <summary>The key of <paramref name="path"/>, or null when it has none: a name Windows trims,
    /// or a path Windows cannot parse. A source with no key is never listed, so never deleted.</summary>
    public static string? Of(string path)
    {
        if (InputPaths.NameTrimmed(path))
            return null;
        try
        {
            // Trimmed first, as Core trims a source: "NUL" + separator is a folder name, bare "NUL" the device.
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.TrimEndingDirectorySeparator(path)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
