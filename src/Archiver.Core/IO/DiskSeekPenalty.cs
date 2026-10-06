using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.IO;

/// <summary>
/// T-F352: asks Windows whether the disk under a path has a seek penalty (a spinning disk has, an
/// SSD has not). Several files read and written side by side cost a spinning disk head movement,
/// so the parallel ZIP writer is chosen for a few large files only where this answers "none".
/// </summary>
internal static partial class DiskSeekPenalty
{
    private const uint FileShareReadWrite = 0x1 | 0x2;
    private const uint OpenExisting = 3;
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const int StorageDeviceSeekPenaltyProperty = 7;
    private const int VolumeNameCapacity = 64;

    /// <summary>
    /// True only when the volume holding <paramref name="path"/> reports no seek penalty. A disk
    /// that does not answer (many USB enclosures, RAID controllers, virtual disks), a network
    /// share and any failure give false - the caller then keeps its sequential path. Never throws.
    /// </summary>
    public static bool IsKnownAbsent(string path) => Query(path) == false;

    /// <summary>
    /// T-F359: true only when the volume holding <paramref name="path"/> reports a seek penalty.
    /// No answer is false here too - a caller that slows down for a spinning disk must not slow
    /// down for a share or a disk that says nothing. Never throws.
    /// </summary>
    public static bool IsKnownPresent(string path) => Query(path) == true;

    private static bool? Query(string path)
    {
        try
        {
            string? volume = VolumeDevicePath(path);
            if (volume is null)
                return null;

            // No access rights: reading a storage property needs none, and so no elevation.
            using SafeFileHandle handle = CreateFileW(volume, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid)
                return null;

            // STORAGE_PROPERTY_QUERY { PropertyId, QueryType = PropertyStandardQuery, 1 byte + padding }
            byte[] query = new byte[12];
            BinaryPrimitives.WriteInt32LittleEndian(query, StorageDeviceSeekPenaltyProperty);

            // DEVICE_SEEK_PENALTY_DESCRIPTOR { Version, Size, BOOLEAN IncursSeekPenalty + padding }
            byte[] descriptor = new byte[12];
            if (!DeviceIoControl(handle, IoctlStorageQueryProperty, query, query.Length, descriptor, descriptor.Length, out int returned, IntPtr.Zero)
                || returned < 9)
            {
                return null;
            }
            return descriptor[8] != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    // "\\?\Volume{guid}" of the volume that really holds the path - a folder can be the mount
    // point of another disk, so the drive letter alone is not enough. Null for a network path.
    private static string? VolumeDevicePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        char[] mountPoint = new char[fullPath.Length + 2];
        if (!GetVolumePathNameW(fullPath, mountPoint, mountPoint.Length))
            return null;

        char[] volumeName = new char[VolumeNameCapacity];
        string mount = new(mountPoint, 0, Math.Max(0, Array.IndexOf(mountPoint, '\0')));
        if (!GetVolumeNameForVolumeMountPointW(mount, volumeName, volumeName.Length))
            return null;

        // The device is the name without its trailing backslash; with it, the root folder opens.
        return new string(volumeName, 0, Math.Max(0, Array.IndexOf(volumeName, '\0'))).TrimEnd('\\');
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumePathNameW(string fileName, [Out] char[] volumePathName, int bufferLength);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeNameForVolumeMountPointW(string volumeMountPoint, [Out] char[] volumeName, int bufferLength);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, [In] byte[] inBuffer, int inBufferSize,
        [Out] byte[] outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);
}
