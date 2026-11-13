using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Archiver.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// T-F463: every disk call that reaches a name tar.exe wrote in the quarantine. tar.exe writes
/// through the long-path prefix, so "a.txt." and "d " are on disk as they are (3.8.9 and 3.5.2,
/// measured); asked for without it, or normalized by Path.GetFullPath/GetRelativePath, they are
/// "a.txt" and "d" (T-F419, T-F460). Paths in and out are plain full paths; only this class adds
/// the prefix. QuarantineFilesOwnerTests keeps every other such call out of the files that handle
/// the quarantine.
/// </summary>
internal static partial class QuarantineFiles
{
    /// <summary>A quarantined file's path below <paramref name="root"/> as tar.exe wrote it.</summary>
    /// <exception cref="IOException"><paramref name="file"/> is not below <paramref name="root"/>
    /// as spelled; Path.GetRelativePath would quietly turn "a.txt." into "a.txt".</exception>
    public static string Relative(string root, string file)
    {
        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        return file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? file[prefix.Length..]
            : throw new CoreTextIOException(CoreMessages.Text(MessageCode.CannotReadArchive, file));
    }

    /// <summary>The files below <paramref name="root"/>, never entering a reparse-point folder.</summary>
    public static IEnumerable<string> EnumerateFiles(string root, CancellationToken cancellationToken = default) =>
        Walk(root, cancellationToken).Where(item => !item.IsFolder).Select(item => item.Path);

    // Walks the quarantine without ever recursing into a reparse-point subdirectory: a plain
    // Directory.EnumerateFiles(..., AllDirectories) would follow such a directory and could walk
    // straight out of quarantine. The pre-scan already rejects any archive containing a symlink
    // entry, so this is defense-in-depth, not the primary safety mechanism (T-F146).
    // T-F443: folders included (a reparse-point folder is neither yielded nor entered).
    // T-F463: each item's attributes come with the listing itself, so none is judged by a second call
    // that could fail open; a file that is a link fails the walk: the pre-scan refuses link entries, so
    // the sandbox broke its rules, and a link's target is not the archive's.
    public static IEnumerable<(string Path, bool IsFolder)> Walk(string root, CancellationToken cancellationToken = default)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dir = pending.Pop();

            (List<string> files, List<string> subDirs) = ListFolder(dir);

            foreach (string file in files)
                yield return (file, false);

            // Pushed last to first, so they are walked in the order listed: "d" before "d " (a name
            // sorts before itself with a trailing space or dot), and the plain one takes the name at
            // the destination, as in ZIP (T-F460).
            foreach (string subDir in Enumerable.Reverse(subDirs))
            {
                yield return (subDir, true);
                pending.Push(subDir);
            }
        }
    }

    // T-F460: a folder that cannot be read fails the walk: a silent skip left its files unscanned
    // (Clean) and not extracted.
    private static (List<string> Files, List<string> Folders) ListFolder(string dir)
    {
        List<string> files = [];
        List<string> folders = [];
        try
        {
            foreach (FileSystemInfo item in new DirectoryInfo(Literal(dir)).EnumerateFileSystemInfos())
            {
                bool isLink = item.Attributes.HasFlag(FileAttributes.ReparsePoint);
                bool isFolder = item is DirectoryInfo;
                if (isLink && !isFolder)
                    throw new CoreTextIOException(CoreMessages.Text(MessageCode.CannotReadArchive, "the sandbox left a link"));
                if (!isFolder)
                    files.Add(Path.Join(dir, item.Name));
                else if (!isLink)
                    folders.Add(Path.Join(dir, item.Name));
            }
        }
        catch (Exception ex) when (ex is (IOException or UnauthorizedAccessException) and not CoreTextIOException)
        {
            throw new CoreTextIOException(CoreMessages.Text(MessageCode.CannotReadArchive, CoreMessages.Detail(ex)), ex);
        }
        return (files, folders);
    }

    public static FileInfo Info(string file) => new(Literal(file));

    /// <summary>T-F474: opened as itself, never through a link, and shared for reading only: no other
    /// handle can be writing it. Throws <see cref="IOException"/> when it cannot be opened so, or is a link.</summary>
    public static FileStream OpenRead(string file)
    {
        SafeFileHandle handle = Open(file, GenericRead);
        if (File.GetAttributes(handle).HasFlag(FileAttributes.ReparsePoint))
        {
            handle.Dispose();
            throw new IOException("The file is a link.");
        }
        return new FileStream(handle, FileAccess.Read);
    }

    public static bool FolderExists(string folder) => Directory.Exists(Literal(folder));

    public static DateTime FolderTime(string folder) => Directory.GetLastWriteTimeUtc(Literal(folder));

    public static void CreateFolder(string folder) => Directory.CreateDirectory(Literal(folder));

    /// <summary>
    /// T-F474: moves a quarantined file out to <paramref name="destination"/>, a path Pakko chose,
    /// replacing what is there, through one handle that also asks whether it may: no link, one name,
    /// only its unnamed data stream (T-F470), and no AppContainer in its DACL. The handle shares
    /// reading only, so it cannot be opened while anything else may write the file, and nothing can
    /// open it for writing until it is moved; the sandbox's entries came off with
    /// <see cref="QuarantineAcl.Revoke"/>, so no new name or stream can be added. Asked again after the
    /// move. False, and nothing delivered, when the sandbox broke these rules; throws
    /// <see cref="IOException"/> when the file cannot be opened or moved.
    /// </summary>
    public static bool TakeOut(string file, string destination)
    {
        using SafeFileHandle handle = Open(file, Delete | ReadControl | ReadAttributes | Synchronize);
        if (!MayLeave(handle))
            return false;
        if (!TryRenameByHandle(handle, destination, out int error))
        {
            if (error != ErrorNotSameDevice)
                throw ErrorFor(error);
            // The commit takes every file in staging: a copy cut short, or one of a file that broke
            // the rules, must not stay there.
            bool copied;
            try
            {
                copied = CopyOut(handle, file, destination);
            }
            catch
            {
                TryDeleteBestEffort(destination);
                throw;
            }
            if (!copied || !MayLeave(handle))
            {
                TryDeleteBestEffort(destination); // the archive fails whole, and its staging with it
                return false;
            }
            // The quarantine is deleted whole with its scope; this only frees its space sooner.
            _ = TryDeleteByHandle(handle);
            return true;
        }
        if (MayLeave(handle))
            return true;
        _ = TryDeleteByHandle(handle); // the archive fails whole, and its staging with it
        return false;
    }

    /// <summary>
    /// After <see cref="QuarantineAcl.Revoke"/> on <paramref name="root"/>: nothing in it may still let
    /// an AppContainer in. Revoke takes the sandbox's entry off the root and off what inherits it, but a
    /// folder or file a run created with a protected DACL of its own (no WRITE_DAC is needed for that)
    /// keeps its entry, and a later run of the scope (the first-copies run) could then add files to it,
    /// or rewrite one after the checks and the scan (batch 5a's review). An honest reader sets no DACL,
    /// so such an entry fails the archive. No run of the scope is alive here: each run is waited for.
    /// </summary>
    public static void EnsureSealed(string root, CancellationToken cancellationToken = default)
    {
        foreach ((string path, bool isFolder) in Walk(root, cancellationToken))
        {
            using SafeFileHandle handle = OpenForSecurity(path, isFolder);
            if (QuarantineAcl.HasAppContainerEntry(handle))
                throw new CoreTextIOException(CoreMessages.Text(MessageCode.CannotReadArchive, "the sandbox gave an entry a DACL of its own"));
        }
    }

    private static SafeFileHandle OpenForSecurity(string path, bool isFolder)
    {
        SafeFileHandle handle = CreateFile(Literal(path), ReadControl, FileShareAll, 0, OpenExisting, OpenReparsePoint | (isFolder ? BackupSemantics : 0), 0);
        if (!handle.IsInvalid)
            return handle;
        int error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        throw ErrorFor(error);
    }

    private static void TryDeleteBestEffort(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best-effort: staging is not committed for a failed archive */ }
    }

    private static bool MayLeave(SafeFileHandle handle) =>
        Archiver.Core.IO.PathIdentity.IsPlainFile(handle) && !QuarantineAcl.HasAppContainerEntry(handle);

    // FILE_RENAME_INFO: ReplaceIfExists (a BOOLEAN in a DWORD-sized union), RootDirectory (a handle,
    // pointer-aligned), FileNameLength, then the NT path of the new name.
    // The destination is normalized as File.Move and the copy's FileStream normalize it (a
    // \\?\ spelling is taken as it is), so both branches land on the same name.
    private static bool TryRenameByHandle(SafeFileHandle handle, string destination, out int error)
    {
        string full = destination.StartsWith(@"\\?\", StringComparison.Ordinal) ? destination : Path.GetFullPath(destination);
        string ntPath = full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal)
            ? @"\??\" + full[4..]
            : full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\??\UNC\" + full[2..]
            : @"\??\" + full;
        int rootOffset = IntPtr.Size;
        int lengthOffset = rootOffset + IntPtr.Size;
        int nameOffset = lengthOffset + sizeof(uint);
        byte[] info = new byte[nameOffset + (ntPath.Length + 1) * sizeof(char)];
        info[0] = 1;
        BitConverter.TryWriteBytes(info.AsSpan(lengthOffset), (uint)(ntPath.Length * sizeof(char)));
        MemoryMarshal.AsBytes(ntPath.AsSpan()).CopyTo(info.AsSpan(nameOffset));
        if (SetFileInformationByHandle(handle, FileRenameInfoClass, ref info[0], (uint)info.Length))
        {
            error = 0;
            return true;
        }
        error = Marshal.GetLastPInvokeError();
        return false;
    }

    // Another volume: a copy from the same handle's file, reopened for reading only and sharing as
    // the first handle does, with its times and attributes, as File.Move's copy keeps them.
    // The one step not through the handle (T-F474): CopyFile by path, while the handle, sharing read
    // only, keeps the file from being written, renamed or deleted, and the path is asked before and
    // after to be that same file. A stream copy from the handle cost about 5 ms a file against
    // CopyFile's 1 ms (measured on another drive, distinct contents). False when the path is
    // another file: nothing of it may be delivered.
    internal static bool CopyOut(SafeFileHandle handle, string file, string destination)
    {
        if (Archiver.Core.IO.PathIdentity.IdentityOf(handle) is not { } identity)
            throw ErrorFor(Marshal.GetLastPInvokeError());
        string source = Literal(file);
        if (!Archiver.Core.IO.PathIdentity.LeadsTo(source, identity))
            return false;
        File.Copy(source, destination, overwrite: true);
        if (!Archiver.Core.IO.PathIdentity.LeadsTo(source, identity))
            return false;
        File.SetCreationTimeUtc(destination, File.GetCreationTimeUtc(handle));
        return true;
    }

    // FILE_DISPOSITION_INFO_EX: delete even a read-only file, which a plain delete leaves behind.
    // Neither reader makes a 0444 entry read-only (measured), but a copied-from file must not stay.
    private static bool TryDeleteByHandle(SafeFileHandle handle)
    {
        uint flags = DispositionDelete | DispositionIgnoreReadOnly;
        return SetFileInformationByHandle(handle, FileDispositionInfoExClass, ref Unsafe.As<uint, byte>(ref flags), sizeof(uint));
    }

    private static SafeFileHandle Open(string file, uint access)
    {
        SafeFileHandle handle = CreateFile(Literal(file), access, FileShareRead, 0, OpenExisting, OpenReparsePoint, 0);
        if (!handle.IsInvalid)
            return handle;
        int error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        throw ErrorFor(error);
    }

    private static IOException ErrorFor(int error) =>
        new(Marshal.GetPInvokeErrorMessage(error), unchecked((int)0x80070000) | error);

    private const uint Delete = 0x00010000;
    private const uint ReadControl = 0x00020000;
    private const uint Synchronize = 0x00100000;
    private const uint ReadAttributes = 0x0080;
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 1;
    private const uint FileShareAll = 7;
    private const uint BackupSemantics = 0x02000000;
    private const uint OpenExisting = 3;
    private const uint OpenReparsePoint = 0x00200000;
    private const int FileRenameInfoClass = 3;
    private const int FileDispositionInfoExClass = 21;
    private const uint DispositionDelete = 0x1;
    private const uint DispositionIgnoreReadOnly = 0x10;
    private const int ErrorNotSameDevice = 17;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string fileName, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(SafeFileHandle file, int infoClass, ref byte info, uint size);

    private static string Literal(string fullPath)
    {
        if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
            return fullPath;
        return fullPath.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + fullPath[2..] : @"\\?\" + fullPath;
    }
}
