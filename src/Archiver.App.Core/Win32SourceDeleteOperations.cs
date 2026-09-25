using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Archiver.App.Core;

/// <summary>Real <see cref="ISourceDeleteOperations"/> over Win32 (T-F207).</summary>
public sealed class Win32SourceDeleteOperations(Func<IntPtr> ownerWindow) : ISourceDeleteOperations
{
    private const uint FileShareAll = 0x7;              // READ | WRITE | DELETE
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;  // lets CreateFileW open a folder
    private const uint FileFlagOpenReparsePoint = 0x00200000; // a link source resolves to the link, not its target
    private const uint DriveFixed = 3;
    private const uint FoDelete = 3;
    private const ushort FofSilent = 0x0004, FofNoConfirmation = 0x0010, FofAllowUndo = 0x0040,
        FofNoErrorUi = 0x0400, FofWantNukeWarning = 0x4000;

    // shellapi.h packs this struct to 8 on 64-bit, 1 only on 32-bit; this app ships x64/ARM64 only.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr Hwnd;
        public uint Func;
        public string From;
        public string? To;
        public ushort Flags;
        public int AnyOperationsAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] path, uint length, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(string fileName, [Out] char[] volumePath, uint length);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDriveTypeW(string rootPath);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref ShFileOpStruct operation);

    /// <inheritdoc/>
    public string? ResolveFinalPath(string path)
    {
        using SafeFileHandle handle = CreateFileW(
            path, 0, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics | FileFlagOpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
            return null;

        var buffer = new char[32768];
        uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        if (length == 0 || length >= buffer.Length)
            return null;

        string final = new(buffer, 0, (int)length);
        if (final.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + final[8..];
        return final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..] : final;
    }

    /// <inheritdoc/>
    public bool IsOnFixedLocalVolume(string finalPath)
    {
        if (finalPath.StartsWith(@"\\", StringComparison.Ordinal))
            return false;

        // The volume, not the drive letter: a removable volume mounted into a folder of C: is
        // still removable. Any failure answers "not fixed", which means asking before deleting.
        var volume = new char[32768];
        if (!GetVolumePathNameW(finalPath, volume, (uint)volume.Length))
            return false;
        int end = Array.IndexOf(volume, '\0');
        return end > 0 && GetDriveTypeW(new string(volume, 0, end)) == DriveFixed;
    }

    /// <inheritdoc/>
    public void MoveToRecycleBin(IReadOnlyList<string> finalPaths)
    {
        var operation = new ShFileOpStruct
        {
            Hwnd = ownerWindow(),
            Func = FoDelete,
            From = string.Join('\0', finalPaths) + "\0\0",
            // FofWantNukeWarning stays as a second line of defence for an item too large for the
            // bin; SourceRecycler checks the disk afterwards either way.
            Flags = (ushort)(FofAllowUndo | FofNoConfirmation | FofWantNukeWarning | FofNoErrorUi | FofSilent),
        };
        _ = SHFileOperationW(ref operation);
    }

    /// <inheritdoc/>
    public void DeletePermanently(string finalPath)
    {
        if (Directory.Exists(finalPath))
            Directory.Delete(finalPath, recursive: true);
        else
            File.Delete(finalPath);
    }

    /// <inheritdoc/>
    public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
