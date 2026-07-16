namespace Archiver.App.Core;

// T-F97: one shared temp cache root for every Archive Browser file preview, mirroring
// TarSandboxScope's "%TEMP%\Pakko<Purpose>" convention (Archiver.Core/Services/Sandbox) but kept
// in the App.Core layer since preview staging is a pure App-layer concern.
public static class PreviewCache
{
    public static readonly string RootDirectory = Path.Combine(Path.GetTempPath(), "PakkoPreview");

    public static string CreateScope()
    {
        string dir = Path.Combine(RootDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // Best-effort — a file still open in the OS handler that previewed it blocks deletion; left
    // for the next app start or OS temp cleanup. Never surfaces to the caller.
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
