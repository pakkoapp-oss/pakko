namespace Archiver.App.Core;

/// <summary>
/// T-F98: one shared temp cache root for the Archive Browser's nested-archive drill-down, one
/// Guid subfolder per nesting level extracted so far — mirrors PreviewCache's shape, but adds
/// DeleteScope for immediate per-level cleanup on navigating back out. That's safe here (unlike
/// PreviewCache, which must wait for window close since an external OS handler may still have the
/// previewed file open): nothing outside Pakko ever holds a handle into a nested-archive scope
/// once the user leaves that level.
/// </summary>
public static class NestedArchiveCache
{
    /// <summary>Root temp directory all nested-archive scopes live under.</summary>
    public static readonly string RootDirectory = Path.Combine(Path.GetTempPath(), "PakkoNestedArchive");

    /// <summary>Creates a fresh scope directory for one nesting level and returns its path.</summary>
    public static string CreateScope()
    {
        string dir = Path.Combine(RootDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Deletes one nesting level's scope directory when the user navigates back out of it.
    /// Best-effort — never surfaces to the caller; <see cref="DeleteAll"/> is the safety net for
    /// anything left behind by a crash or the window closing mid-drill-down.
    /// </summary>
    public static void DeleteScope(string scopeDir)
    {
        try
        {
            if (Directory.Exists(scopeDir))
                Directory.Delete(scopeDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup — never surfaces to the caller
        }
    }

    /// <summary>Deletes every nested-archive scope — the safety net for anything a scoped delete missed.</summary>
    public static void DeleteAll()
    {
        try
        {
            if (Directory.Exists(RootDirectory))
                Directory.Delete(RootDirectory, recursive: true);
        }
        catch
        {
            // best-effort cleanup — never surfaces to the caller
        }
    }
}
