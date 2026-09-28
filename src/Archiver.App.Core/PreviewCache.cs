namespace Archiver.App.Core;

/// <summary>
/// T-F97: temp cache for Archive Browser file previews, mirroring TarSandboxScope's
/// "%TEMP%\Pakko&lt;Purpose&gt;" convention (Archiver.Core/Services/Sandbox) but kept in the
/// App.Core layer since preview staging is a pure App-layer concern. T-F252: one subfolder per
/// process (<see cref="ProcessTempRoot"/>).
/// </summary>
public static class PreviewCache
{
    private static readonly ProcessTempRoot _root = new(
        Path.Combine(Path.GetTempPath(), "PakkoPreview"), ProcessTempRoot.CurrentOwnerName, ProcessTempRoot.IsOwnerAlive);

    /// <summary>Root temp directory every process's preview scopes live under.</summary>
    public static string RootDirectory => _root.SharedRoot;

    /// <summary>This process's preview folder.</summary>
    public static string OwnDirectory => _root.OwnRoot;

    /// <summary>Creates a fresh scope directory for one previewed file and returns its path.</summary>
    public static string CreateScope() => _root.CreateScope();

    /// <summary>
    /// Deletes this process's preview scopes. Best-effort — a file still open in the OS handler
    /// that previewed it blocks deletion; <see cref="SweepStale"/> at the next start removes it.
    /// </summary>
    public static void DeleteOwn() => _root.DeleteOwn();

    /// <summary>Deletes preview folders left by Pakko processes that no longer run.</summary>
    public static void SweepStale() => _root.SweepStale();
}
