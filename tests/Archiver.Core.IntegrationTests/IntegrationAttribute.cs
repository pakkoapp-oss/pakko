namespace Archiver.Core.IntegrationTests;

/// <summary>
/// Marks a test as requiring the real system tar.exe. Skipped automatically when
/// C:\Windows\System32\tar.exe is not present (e.g. non-Windows CI, or a stripped-down image).
/// </summary>
public sealed class IntegrationAttribute : FactAttribute
{
    public IntegrationAttribute()
    {
        if (!File.Exists(@"C:\Windows\System32\tar.exe"))
            Skip = "tar.exe not present at C:\\Windows\\System32\\tar.exe";
    }
}
