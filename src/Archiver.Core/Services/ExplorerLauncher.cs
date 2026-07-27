using System.Diagnostics;

namespace Archiver.Core.Services;

internal static class ExplorerLauncher
{
    // Resolved once via SpecialFolder.Windows (%SystemRoot%) rather than the bare "explorer.exe"
    // relative name every call site used before — matches this project's existing tar.exe
    // absolute-path convention and avoids resolving the executable via PATH.
    private static readonly string ExplorerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    public static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo(ExplorerPath, path) { UseShellExecute = true }); } catch { /* best-effort */ }
    }
}
