namespace Archiver.App.Core;

/// <summary>
/// T-F97: one shared temp cache root for every Archive Browser file preview, mirroring
/// TarSandboxScope's "%TEMP%\Pakko&lt;Purpose&gt;" convention (Archiver.Core/Services/Sandbox) but
/// kept in the App.Core layer since preview staging is a pure App-layer concern.
/// </summary>
public static class PreviewCache
{
    /// <summary>Root temp directory every preview scope lives under.</summary>
    public static readonly string RootDirectory = Path.Combine(Path.GetTempPath(), "PakkoPreview");

    /// <summary>Creates a fresh scope directory for one previewed file and returns its path.</summary>
    public static string CreateScope()
    {
        string dir = Path.Combine(RootDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Deletes every preview scope. Best-effort — a file still open in the OS handler that
    /// previewed it blocks deletion; left for the next app start or OS temp cleanup. Never
    /// surfaces to the caller.
    /// </summary>
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
