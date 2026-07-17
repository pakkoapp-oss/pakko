namespace Archiver.App.Core;

// T-F98: one shared temp cache root for the Archive Browser's nested-archive drill-down, one
// Guid subfolder per nesting level extracted so far — mirrors PreviewCache's shape, but adds
// DeleteScope for immediate per-level cleanup on navigating back out. That's safe here (unlike
// PreviewCache, which must wait for window close since an external OS handler may still have the
// previewed file open): nothing outside Pakko ever holds a handle into a nested-archive scope
// once the user leaves that level.
public static class NestedArchiveCache
{
    public static readonly string RootDirectory = Path.Combine(Path.GetTempPath(), "PakkoNestedArchive");

    public static string CreateScope()
    {
        string dir = Path.Combine(RootDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // Best-effort — never surfaces to the caller. Called when popping a level off the browse
    // stack; DeleteAll() remains the safety net for anything left behind by a crash or the
    // window closing mid-drill-down.
    public static void DeleteScope(string scopeDir)
    {
        try
        {
            if (Directory.Exists(scopeDir))
                Directory.Delete(scopeDir, recursive: true);
        }
        catch
        {
        }
    }

    public static void DeleteAll()
    {
        try
        {
            if (Directory.Exists(RootDirectory))
                Directory.Delete(RootDirectory, recursive: true);
        }
        catch
        {
        }
    }
}
