namespace Archiver.App.Core;

/// <summary>
/// T-F98: temp cache for the Archive Browser's nested-archive drill-down, one Guid subfolder per
/// nesting level extracted so far — mirrors PreviewCache's shape, but adds DeleteScope for
/// immediate per-level cleanup on navigating back out. That's safe here (unlike PreviewCache,
/// which must wait for window close since an external OS handler may still have the previewed
/// file open): nothing outside Pakko ever holds a handle into a nested-archive scope once the user
/// leaves that level. T-F252: one subfolder per process (<see cref="ProcessTempRoot"/>).
/// </summary>
public static class NestedArchiveCache
{
    private static readonly ProcessTempRoot _root = new(
        Path.Combine(Path.GetTempPath(), "PakkoNestedArchive"), ProcessTempRoot.CurrentOwnerName, ProcessTempRoot.IsOwnerAlive);

    /// <summary>Root temp directory every process's nested-archive scopes live under.</summary>
    public static string RootDirectory => _root.SharedRoot;

    /// <summary>This process's nested-archive folder.</summary>
    public static string OwnDirectory => _root.OwnRoot;

    /// <summary>Creates a fresh scope directory for one nesting level and returns its path.</summary>
    public static string CreateScope() => _root.CreateScope();

    /// <summary>
    /// Deletes one nesting level's scope directory when the user navigates back out of it.
    /// Best-effort — never surfaces to the caller; <see cref="DeleteOwn"/> is the safety net for
    /// anything left behind by the window closing mid-drill-down.
    /// </summary>
    public static void DeleteScope(string scopeDir) => ProcessTempRoot.TryDelete(scopeDir);

    /// <summary>Deletes this process's nested-archive scopes.</summary>
    public static void DeleteOwn() => _root.DeleteOwn();

    /// <summary>Deletes nested-archive folders left by Pakko processes that no longer run.</summary>
    public static void SweepStale() => _root.SweepStale();
}
