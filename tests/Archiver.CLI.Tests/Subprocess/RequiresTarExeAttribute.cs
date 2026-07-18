namespace Archiver.CLI.Tests.Subprocess;

/// <summary>
/// Marks a test as requiring the real system tar.exe. Skipped automatically when
/// C:\Windows\System32\tar.exe is not present. Deliberately duplicated from
/// Archiver.Core.IntegrationTests' IntegrationAttribute rather than cross-referencing that test
/// project as a dependency (per TASKS.md's T-F09 note that this is a genuinely new, separate
/// test layer).
/// </summary>
public sealed class RequiresTarExeAttribute : FactAttribute
{
    public RequiresTarExeAttribute()
    {
        if (!File.Exists(@"C:\Windows\System32\tar.exe"))
            Skip = "tar.exe not present at C:\\Windows\\System32\\tar.exe";
    }
}
