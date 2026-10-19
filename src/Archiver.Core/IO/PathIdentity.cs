using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.IO;

/// <summary>
/// T-F419: where a path really leads - its final path (every link, SUBST drive and \\?\ spelling
/// resolved) and its file identity (volume serial and file ID), which every spelling of one file
/// or folder shares, the admin share included. The same idea as the CLI's <c>CliSourceDeleter</c>;
/// T-F420 is to make it one helper.
/// </summary>
internal static partial class PathIdentity
{
    internal readonly record struct Identity(ulong Volume, ulong Low, ulong High);

    internal sealed record Resolved(Identity Identity, string FinalPath);

    /// <summary>The identity and final path (without the \\?\ prefix) of an existing file or
    /// folder; null when it does not exist or cannot be opened or read.</summary>
    internal static Resolved? Resolve(string path)
    {
        using SafeFileHandle handle = CreateFile(path, 0, FileShareAll, 0, OpenExisting, BackupSemantics, 0);
        if (handle.IsInvalid)
            return null;
        Identity identity;
        if (GetFileInformationByHandleEx(handle, FileIdInfoClass, out FileIdInfo id, (uint)Marshal.SizeOf<FileIdInfo>()))
            identity = new Identity(id.VolumeSerialNumber, id.IdLow, id.IdHigh);
        else if (GetFileInformationByHandle(handle, out ByHandleFileInformation info))
            identity = new Identity(info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow, 0);
        else
            return null; // neither identity can be read: where it leads is unknown
        return FinalPath(handle) is { } finalPath ? new Resolved(identity, WithoutLongPathPrefix(finalPath)) : null;
    }

    // On the stack for an ordinary path (SmallFileAllocationTests); a longer one is asked for
    // again in a buffer of the size the first call reported.
    private static unsafe string? FinalPath(SafeFileHandle handle)
    {
        const int StackLength = 520;
        char* small = stackalloc char[StackLength];
        uint length = GetFinalPathNameByHandle(handle, small, StackLength, 0);
        if (length == 0)
            return null;
        if (length < StackLength)
            return new string(small, 0, (int)length);
        char[] large = new char[length];
        fixed (char* start = large)
        {
            uint written = GetFinalPathNameByHandle(handle, start, (uint)large.Length, 0);
            return written == 0 || written >= large.Length ? null : new string(start, 0, (int)written);
        }
    }

    /// <summary>The identities of <paramref name="path"/> and every folder holding it, by its final
    /// path; null when it cannot be resolved. Bounded by the path's depth.</summary>
    internal static HashSet<Identity>? SelfAndAncestors(string path)
    {
        if (Resolve(path) is not { } resolved)
            return null;
        var identities = new HashSet<Identity> { resolved.Identity };
        for (string? folder = Path.GetDirectoryName(resolved.FinalPath); folder is not null; folder = Path.GetDirectoryName(folder))
        {
            if (Resolve(folder) is not { } step)
                return null;
            identities.Add(step.Identity);
        }
        return identities;
    }

    private static string WithoutLongPathPrefix(string path) =>
        path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + path[8..]
        : path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..]
        : path;

    private const uint FileShareAll = 7;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const int FileIdInfoClass = 18;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        public ulong VolumeSerialNumber;
        public ulong IdLow;
        public ulong IdHigh;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public ulong CreationTime;
        public ulong LastAccessTime;
        public ulong LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string fileName, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out FileIdInfo info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation info);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static unsafe partial uint GetFinalPathNameByHandle(SafeFileHandle file, char* path, uint length, uint flags);
}
