using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Services;

/// <summary>
/// T-F171: a directory junction, used to give tar.exe a folder source under another name. tar.exe
/// has no rename option (no "-s", no "--transform"), and a junction in its "-T" list is archived as
/// the folder it points to. .NET can only create symbolic links, which need Developer Mode.
/// </summary>
internal static partial class DirectoryJunction
{
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FsctlSetReparsePoint = 0x000900A4;
    private const uint IoReparseTagMountPoint = 0xA0000003;
    private const string NtPathPrefix = @"\??\";

    /// <summary>
    /// True when <paramref name="fullPath"/> is on a network share or a mapped network drive — a
    /// junction can only point to a local volume.
    /// </summary>
    public static bool IsNetworkPath(string fullPath)
    {
        if (fullPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal) || fullPath.StartsWith(@"\\.\", StringComparison.Ordinal))
            fullPath = fullPath[4..];
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
            return true;
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
            return false;
        try
        {
            return new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Creates <paramref name="junctionPath"/> (must not exist) pointing to the local folder <paramref name="targetFullPath"/>.</summary>
    /// <exception cref="IOException">The junction could not be created; nothing is left behind.</exception>
    public static void Create(string junctionPath, string targetFullPath)
    {
        byte[] reparseData = BuildMountPointData(targetFullPath);
        Directory.CreateDirectory(junctionPath);
        try
        {
            using SafeFileHandle handle = CreateFileW(junctionPath, GenericWrite, 0, IntPtr.Zero, OpenExisting,
                FileFlagBackupSemantics | FileFlagOpenReparsePoint, IntPtr.Zero);
            if (handle.IsInvalid)
                throw LastWin32Error();
            if (!DeviceIoControl(handle, FsctlSetReparsePoint, reparseData, reparseData.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
                throw LastWin32Error();
        }
        catch
        {
            try { Directory.Delete(junctionPath, recursive: false); } catch { /* best-effort: the folder is still empty */ }
            throw;
        }
    }

    // T-F297: keeps the Windows code (as an HRESULT) so the error detail can show it.
    private static IOException LastWin32Error()
    {
        int error = Marshal.GetLastWin32Error();
        return new IOException(new Win32Exception(error).Message, unchecked((int)(0x80070000u | ((uint)error & 0xFFFFu))));
    }

    // REPARSE_DATA_BUFFER for IO_REPARSE_TAG_MOUNT_POINT: tag, data length, reserved, then the
    // substitute name ("\??\C:\...") and print name offsets and lengths in bytes, then both names,
    // each with a terminating NUL.
    internal static byte[] BuildMountPointData(string targetFullPath)
    {
        string target = Path.TrimEndingDirectorySeparator(targetFullPath);
        byte[] substitute = Encoding.Unicode.GetBytes(NtPathPrefix + target);
        byte[] print = Encoding.Unicode.GetBytes(target);
        const int HeaderBytes = 8;
        const int NamesHeaderBytes = 8;
        int dataLength = NamesHeaderBytes + substitute.Length + 2 + print.Length + 2;
        if (dataLength > ushort.MaxValue - HeaderBytes)
            throw new IOException("The folder path is too long for a junction.");

        byte[] buffer = new byte[HeaderBytes + dataLength];
        Span<byte> span = buffer;
        BinaryPrimitives.WriteUInt32LittleEndian(span, IoReparseTagMountPoint);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)dataLength);
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], (ushort)substitute.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], (ushort)(substitute.Length + 2));
        BinaryPrimitives.WriteUInt16LittleEndian(span[14..], (ushort)print.Length);
        substitute.CopyTo(span[16..]);
        print.CopyTo(span[(16 + substitute.Length + 2)..]);
        return buffer;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, [In] byte[] inBuffer, int inBufferSize,
        IntPtr outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);
}
