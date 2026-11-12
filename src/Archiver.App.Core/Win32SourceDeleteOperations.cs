using System.Runtime.InteropServices;
using Archiver.Core.IO;

namespace Archiver.App.Core;

/// <summary>Real <see cref="ISourceDeleteOperations"/> over Win32 (T-F207).</summary>
public sealed partial class Win32SourceDeleteOperations(Func<IntPtr> ownerWindow) : ISourceDeleteOperations
{
    private const uint DriveFixed = 3;
    private const uint FoDelete = 3;
    private const ushort FofSilent = 0x0004, FofNoConfirmation = 0x0010, FofAllowUndo = 0x0040,
        FofNoErrorUi = 0x0400, FofWantNukeWarning = 0x4000;

    // shellapi.h packs this struct to 8 on 64-bit, 1 only on 32-bit; this app ships x64/ARM64 only.
    // String fields are raw PWSTRs so the struct stays blittable for [LibraryImport] (T-F287).
    [StructLayout(LayoutKind.Sequential)]
    private struct ShFileOpStruct
    {
        public IntPtr Hwnd;
        public uint Func;
        public IntPtr From;
        public IntPtr To;
        public ushort Flags;
        public int AnyOperationsAborted;
        public IntPtr NameMappings;
        public IntPtr ProgressTitle;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumePathNameW(string fileName, [Out] char[] volumePath, uint length);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveTypeW(string rootPath);

    [LibraryImport("shell32.dll")]
    private static partial int SHFileOperationW(ref ShFileOpStruct operation);

    /// <inheritdoc/>
    public string? ResolveFinalPath(string path) => PathIdentity.FinalPathOfItself(path);

    /// <inheritdoc/>
    public bool IsOnFixedLocalVolume(string finalPath)
    {
        if (finalPath.StartsWith(@"\\", StringComparison.Ordinal))
            return false;

        // The volume, not the drive letter: a removable volume mounted into a folder of C: is
        // still removable. Any failure answers "not fixed", which means asking before deleting.
        char[] volume = new char[32768];
        if (!GetVolumePathNameW(finalPath, volume, (uint)volume.Length))
            return false;
        int end = Array.IndexOf(volume, '\0');
        return end > 0 && GetDriveTypeW(new string(volume, 0, end)) == DriveFixed;
    }

    /// <inheritdoc/>
    public void MoveToRecycleBin(IReadOnlyList<string> finalPaths)
    {
        // Double-null-terminated list; StringToHGlobalUni copies embedded nulls and adds one more.
        IntPtr from = Marshal.StringToHGlobalUni(string.Join('\0', finalPaths) + "\0\0");
        try
        {
            var operation = new ShFileOpStruct
            {
                Hwnd = ownerWindow(),
                Func = FoDelete,
                From = from,
                // FofWantNukeWarning stays as a second line of defence for an item too large for the
                // bin; SourceRecycler checks the disk afterwards either way.
                Flags = (ushort)(FofAllowUndo | FofNoConfirmation | FofWantNukeWarning | FofNoErrorUi | FofSilent),
            };
            _ = SHFileOperationW(ref operation);
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
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
