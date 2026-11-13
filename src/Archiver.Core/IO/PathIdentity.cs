using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.IO;

/// <summary>
/// T-F419: where a path really leads - its final path (every link, SUBST drive and \\?\ spelling
/// resolved) and its file identity (volume serial and file ID), which every spelling of one file
/// or folder shares, the admin share included. The one place -sdel's refusal (the CLI's
/// <c>CliSourceDeleter</c>) and "Delete after"'s containment rule (<c>SourceOutcomeRules</c>) ask (T-F420).
/// </summary>
public static partial class PathIdentity
{
    /// <summary>A volume serial number and a file ID (128-bit where the volume has one).</summary>
    public readonly record struct Identity(ulong Volume, ulong Low, ulong High);

    /// <summary>What <see cref="Resolve"/> found: the identity and the final path, without the
    /// long-path prefix.</summary>
    public sealed record Resolved(Identity Id, string FinalPath);

    /// <summary>The identity and final path (without the \\?\ prefix) of an existing file or
    /// folder; null when it does not exist or cannot be opened or read.</summary>
    public static Resolved? Resolve(string path)
    {
        using SafeFileHandle handle = CreateFile(path, 0, FileShareAll, 0, OpenExisting, BackupSemantics, 0);
        if (handle.IsInvalid)
            return null;
        if (IdentityOf(handle) is not { } identity)
            return null; // neither identity can be read: where it leads is unknown
        return ReadFinalPath(handle) is { } finalPath ? new Resolved(identity, WithoutLongPathPrefix(finalPath)) : null;
    }

    /// <summary>The identity of what <paramref name="handle"/> has open; null when neither identity can be read.</summary>
    internal static Identity? IdentityOf(SafeFileHandle handle)
    {
        if (GetFileInformationByHandleEx(handle, FileIdInfoClass, out FileIdInfo id, (uint)Marshal.SizeOf<FileIdInfo>()))
            return new Identity(id.VolumeSerialNumber, id.IdLow, id.IdHigh);
        if (GetFileInformationByHandle(handle, out ByHandleFileInformation info))
            return new Identity(info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow, 0);
        return null;
    }

    /// <summary>T-F474: whether <paramref name="path"/>, links followed, is the file <paramref name="identity"/> names.</summary>
    internal static bool LeadsTo(string path, Identity identity)
    {
        using SafeFileHandle handle = CreateFile(path, 0, FileShareAll, 0, OpenExisting, BackupSemantics, 0);
        return !handle.IsInvalid && IdentityOf(handle) == identity;
    }

    /// <summary>T-F511: the final path of the file or folder itself, a link opened as the link (its
    /// target is never what is meant: "Delete after" deletes the link, never what is behind it); the
    /// folders on the way, SUBST drives and \\?\ spellings resolved. Null when it cannot be opened or read.</summary>
    public static string? FinalPathOfItself(string path)
    {
        using SafeFileHandle handle = CreateFile(path, 0, FileShareAll, 0, OpenExisting, BackupSemantics | OpenReparsePoint, 0);
        if (handle.IsInvalid)
            return null;
        return ReadFinalPath(handle) is { } finalPath ? WithoutLongPathPrefix(finalPath) : null;
    }

    /// <summary>T-F524: the volume <paramref name="folder"/> is on, asked of a handle to it (a junction or
    /// a mount point answers for where it leads): its file-system flags and its volume GUID root
    /// (<c>\\?\Volume{...}\</c>). Null when it cannot be opened or has no volume GUID path, as a
    /// network folder has none.</summary>
    internal static (uint FileSystemFlags, string VolumeRoot)? VolumeOf(string folder)
    {
        using SafeFileHandle handle = CreateFile(folder, 0, FileShareAll, 0, OpenExisting, BackupSemantics, 0);
        if (handle.IsInvalid || !GetVolumeInformationByHandle(handle, 0, 0, 0, 0, out uint flags, 0, 0))
            return null;
        const string Start = @"\\?\Volume{";
        if (ReadFinalPath(handle, VolumeNameGuid) is not { } guidPath || !guidPath.StartsWith(Start, StringComparison.OrdinalIgnoreCase))
            return null;
        int end = guidPath.IndexOf('\\', Start.Length);
        return (flags, end < 0 ? guidPath + @"\" : guidPath[..(end + 1)]);
    }

    private const uint VolumeNameGuid = 0x1;

    // On the stack for an ordinary path (SmallFileAllocationTests); a longer one is asked for
    // again in a buffer of the size the first call reported.
    private static string? ReadFinalPath(SafeFileHandle handle, uint flags = 0)
    {
        Span<char> small = stackalloc char[520];
        uint length = GetFinalPathNameByHandle(handle, ref MemoryMarshal.GetReference(MemoryMarshal.Cast<char, ushort>(small)), (uint)small.Length, flags);
        if (length == 0)
            return null;
        if (length < small.Length)
            return new string(small[..(int)length]);
        char[] large = new char[length];
        uint written = GetFinalPathNameByHandle(handle, ref MemoryMarshal.GetReference(MemoryMarshal.Cast<char, ushort>(large.AsSpan())), (uint)large.Length, flags);
        return written == 0 || written >= large.Length ? null : new string(large, 0, (int)written);
    }

    /// <summary>The identities of <paramref name="path"/> and every folder holding it, by its final
    /// path; null when it cannot be resolved. Bounded by the path's depth.</summary>
    public static HashSet<Identity>? SelfAndAncestors(string path) => SelfAndAncestors(path, folders: []);

    /// <summary>As <see cref="SelfAndAncestors(string)"/>, with the folders already resolved in this
    /// pass kept in <paramref name="folders"/> (T-F497: a thousand archives in one folder resolved
    /// that folder and each of its parents a thousand times).</summary>
    internal static HashSet<Identity>? SelfAndAncestors(string path, Dictionary<string, Identity?> folders)
    {
        if (Resolve(path) is not { } resolved)
            return null;
        var identities = new HashSet<Identity> { resolved.Id };
        for (string? folder = Path.GetDirectoryName(resolved.FinalPath); folder is not null; folder = Path.GetDirectoryName(folder))
        {
            if (!folders.TryGetValue(folder, out Identity? id))
            {
                id = Resolve(folder)?.Id;
                folders[folder] = id;
            }
            if (id is not { } step)
                return null;
            identities.Add(step);
        }
        return identities;
    }

    /// <summary>
    /// T-F392: the identity of every file directly in <paramref name="folder"/> (folders and links
    /// left out), by name, read in one listing of the folder rather than by opening each file. Null
    /// when the folder cannot be opened or its volume cannot list identities (FAT, some shares).
    /// </summary>
    internal static Dictionary<string, Identity>? FilesIn(string folder)
    {
        using SafeFileHandle handle = CreateFile(folder, ListDirectory, FileShareAll, 0, OpenExisting, BackupSemantics, 0);
        if (handle.IsInvalid
            || !GetFileInformationByHandleEx(handle, FileIdInfoClass, out FileIdInfo folderId, (uint)Marshal.SizeOf<FileIdInfo>()))
            return null;

        var files = new Dictionary<string, Identity>(StringComparer.OrdinalIgnoreCase);
        byte[] buffer = new byte[64 * 1024];
        int infoClass = FileIdExtdDirectoryRestartInfoClass;
        while (true)
        {
            if (!GetFileInformationByHandleEx(handle, infoClass, ref buffer[0], (uint)buffer.Length))
                return Marshal.GetLastPInvokeError() == ErrorNoMoreFiles ? files : null;
            if (!ReadDirectoryEntries(buffer, folderId.VolumeSerialNumber, files))
                return null;
            infoClass = FileIdExtdDirectoryInfoClass;
        }
    }

    // FILE_ID_EXTD_DIR_INFO (WinBase.h): NextEntryOffset at 0, FileAttributes at 56,
    // FileNameLength at 60, FileId (128 bits) at 72, FileName at 88. False on a malformed chain.
    private static bool ReadDirectoryEntries(byte[] buffer, ulong volume, Dictionary<string, Identity> files)
    {
        int offset = 0;
        while (true)
        {
            if (offset < 0 || offset > buffer.Length - DirInfoNameOffset)
                return false;
            uint next = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset));
            uint attributes = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset + 56));
            uint nameBytes = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset + 60));
            if (nameBytes > (uint)(buffer.Length - offset - DirInfoNameOffset) || nameBytes % 2 != 0)
                return false;
            if ((attributes & (FileAttributeDirectory | FileAttributeReparsePoint)) == 0)
            {
                string name = Encoding.Unicode.GetString(buffer, offset + DirInfoNameOffset, (int)nameBytes);
                files[name] = new Identity(volume,
                    BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset + 72)),
                    BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset + 80)));
            }
            if (next == 0)
                return true;
            if (next > (uint)(buffer.Length - offset))
                return false;
            offset += (int)next;
        }
    }

    /// <summary>
    /// T-F470, T-F474: whether the file open as <paramref name="file"/> (with FILE_READ_ATTRIBUTES,
    /// opened as itself, not through a link) is no link, has one name and only its unnamed data stream.
    /// No data is read. What the calls cannot show - a failed call, a second stream, a malformed
    /// record - is false.
    /// </summary>
    internal static bool IsPlainFile(SafeFileHandle file)
    {
        if (!GetFileInformationByHandle(file, out ByHandleFileInformation info) || info.NumberOfLinks != 1
            || (info.FileAttributes & (FileAttributeReparsePoint | FileAttributeDirectory)) != 0)
            return false;
        // FILE_STREAM_INFO (WinBase.h): NextEntryOffset at 0, StreamNameLength at 4, StreamName at 24;
        // its LARGE_INTEGERs want 8-byte alignment, which a long buffer gives.
        Span<byte> buffer = MemoryMarshal.AsBytes(stackalloc long[32]);
        if (!GetFileInformationByHandleEx(file, FileStreamInfoClass, ref MemoryMarshal.GetReference(buffer), (uint)buffer.Length))
            return false;
        uint next = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        uint nameBytes = BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..]);
        if (next != 0 || nameBytes != UnnamedDataStream.Length * 2)
            return false;
        return MemoryMarshal.Cast<byte, char>(buffer.Slice(StreamInfoNameOffset, (int)nameBytes)).SequenceEqual(UnnamedDataStream);
    }

    private static string WithoutLongPathPrefix(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + path[8..];
        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }

    private const uint FileShareAll = 7;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const int FileIdInfoClass = 18;
    private const int FileIdExtdDirectoryInfoClass = 19;
    private const int FileIdExtdDirectoryRestartInfoClass = 20;
    private const uint ListDirectory = 1;
    private const int ErrorNoMoreFiles = 18;
    private const int DirInfoNameOffset = 88;
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeReparsePoint = 0x400;
    private const int FileStreamInfoClass = 7;
    private const uint ReadAttributes = 0x80;
    private const uint OpenReparsePoint = 0x00200000;
    private const int StreamInfoNameOffset = 24;
    private const string UnnamedDataStream = "::$DATA";

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        public ulong VolumeSerialNumber;
        public ulong IdLow;
        public ulong IdHigh;
    }

    // BY_HANDLE_FILE_INFORMATION's FILETIMEs are two DWORDs, 4-byte aligned: without Pack = 4 each
    // ulong here would be padded to 8 and every field after FileAttributes read 4 bytes off.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
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
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, ref byte buffer, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation info);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformationByHandle(
        SafeFileHandle file, nint volumeName, uint volumeNameSize, nint serial, nint maxComponentLength,
        out uint fileSystemFlags, nint fileSystemName, uint fileSystemNameSize);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    // The buffer as UTF-16 code units: a char is not blittable to the source generator.
    private static partial uint GetFinalPathNameByHandle(SafeFileHandle file, ref ushort path, uint length, uint flags);
}
