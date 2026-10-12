namespace Archiver.Core.IntegrationTests;

/// <summary>
/// A test that needs the real tar.exe and the right to create symbolic links (an elevated
/// process, or Developer Mode). Skipped when either is missing.
/// </summary>
public sealed class SymbolicLinkFactAttribute : FactAttribute
{
    public SymbolicLinkFactAttribute()
    {
        if (!File.Exists(@"C:\Windows\System32\tar.exe"))
        {
            Skip = "tar.exe not present at C:\\Windows\\System32\\tar.exe";
            return;
        }

        string link = Path.Combine(Path.GetTempPath(), "pakko-symlink-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.CreateSymbolicLink(link, Environment.ProcessPath!);
            File.Delete(link);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Skip = "this process may not create symbolic links: " + ex.Message;
        }
    }
}
