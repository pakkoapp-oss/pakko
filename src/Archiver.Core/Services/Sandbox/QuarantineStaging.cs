using System.Runtime.InteropServices;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// Places the source archive into the quarantine "in\" folder before a sandboxed tar.exe touches
/// it — hardlinked when possible (instant, no I/O), copied when the archive and quarantine root
/// are on different volumes (CreateHardLinkW cannot span volumes). The AppContainer SID is never
/// granted an ACE on the archive's original, user-chosen path — only on this staged copy.
/// </summary>
internal static class QuarantineStaging
{
    // Same Path.GetPathRoot(Path.GetFullPath(...)) pattern as ArchiveEntrySecurity.
    // GetAvailableFreeSpace — comparing drive/share roots is volume-boundary-correct for both
    // local drive letters and UNC shares, and doesn't need an existing directory to work.
    public static bool IsSameVolume(string pathA, string pathB)
    {
        string? rootA = Path.GetPathRoot(Path.GetFullPath(pathA));
        string? rootB = Path.GetPathRoot(Path.GetFullPath(pathB));
        return !string.IsNullOrEmpty(rootA)
            && string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase);
    }

    // Stages archivePath into destPath (a path inside quarantine\in\). Tries a hardlink first —
    // a wrong same-volume guess just costs an unnecessary fallback copy, never a correctness
    // break, so this doesn't strictly need IsSameVolume called first; it's exposed separately
    // because callers also need it to decide whether an "out\" move at the end can be a rename
    // (same volume) or needs a copy+delete.
    public static void StageArchive(string archivePath, string destPath)
    {
        if (NativeMethods.CreateHardLinkW(destPath, archivePath, IntPtr.Zero))
            return;

        File.Copy(archivePath, destPath, overwrite: false);
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateHardLinkW(
            string lpFileName,
            string lpExistingFileName,
            IntPtr lpSecurityAttributes);
    }
}
