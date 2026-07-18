using System.Runtime.InteropServices;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// Entry-name and filesystem-path security checks shared by every archive extractor
/// (ZipArchiveService, TarProcessService). Kept in one place so ADS/reserved-name/reparse-point
/// validation cannot drift between extractors — see DECISIONS.md's T-F49 entry.
/// </summary>
internal static class ArchiveEntrySecurity
{
    // T-F94: single source of truth for both extractors' bomb-ratio threshold — previously
    // duplicated separately in ZipArchiveService and TarProcessService. See DECISIONS.md's
    // T-F94 entry.
    public const int MaxCompressionRatio = 1000;
    // T-F39: Reject reserved Windows device names (with or without extension, case-insensitive)
    private static readonly HashSet<string> _reservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    // T-F38: Reject entries with ':' in name (Alternate Data Streams)
    public static bool HasAlternateDataStreamMarker(string entryPath)
        => entryPath.Contains(':');

    public static bool HasReservedName(string entryPath)
    {
        // Use the last path segment from the raw archive entry name (before any GetFullPath call)
        string lastSegment = entryPath.Contains('/')
            ? entryPath[(entryPath.LastIndexOf('/') + 1)..]
            : entryPath;
        string nameWithoutExt = Path.GetFileNameWithoutExtension(lastSegment);
        return _reservedNames.Contains(nameWithoutExt);
    }

    // T-F39: Reject entries with control characters (0x00–0x1F) in name
    public static bool HasControlCharacters(string entryPath)
        => entryPath.Any(c => c < 0x20);

    // T-F23: Returns true when path itself carries the ReparsePoint attribute (symlink or junction).
    // Swallows all exceptions — returns false when attributes cannot be read.
    //
    // Filesystem compatibility:
    //   FAT32/exFAT : always false — these filesystems have no reparse points.
    //   ReFS        : correctly true for symlinks and junctions (same as NTFS).
    //   SMB/UNC     : true when the server propagates FILE_ATTRIBUTE_REPARSE_POINT;
    //                 DFS junctions are followed transparently by the SMB redirector
    //                 and appear as normal directories (false) — not detected here.
    //   Linux/Samba : Linux symlinks are NOT exposed as reparse points to Windows
    //                 clients; they resolve to targets and appear as normal files/dirs.
    //   ISO 9660    : always false — no reparse points on optical media.
    //
    // TODO: Cloud storage stubs (OneDrive cloud-only files) carry FILE_ATTRIBUTE_REPARSE_POINT
    //       and are therefore incorrectly added to SkippedFiles rather than being downloaded
    //       and archived. Fixing this requires reading the reparse tag to distinguish
    //       IO_REPARSE_TAG_CLOUD_* from IO_REPARSE_TAG_SYMLINK / IO_REPARSE_TAG_MOUNT_POINT.
    //       Implement when OneDrive compatibility becomes a requirement.
    public static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            // Cannot read attributes (unreachable network, permission denied, path vanished).
            // Return false — let the subsequent file-open produce an ArchiveError instead.
            return false;
        }
    }

    // T-F37: Check whether any directory component of destFilePath (within rootPath) is a reparse point
    // T-F37: No automated unit test — System.IO.Compression cannot create reparse points in test fixtures.
    public static bool PathContainsReparsePoint(string destFilePath, string rootPath)
    {
        string? current = Path.GetDirectoryName(destFilePath);
        while (current != null && current.Length >= rootPath.Length)
        {
            if (Directory.Exists(current))
            {
                var info = new DirectoryInfo(current);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    return true;
            }
            string? parent = Path.GetDirectoryName(current);
            if (parent == current) break;
            current = parent;
        }
        return false;
    }

    // T-F51: files matching this list drive GroupPolicyOptions.MotwMode.UnsafeExtensionsOnly —
    // modeled on Windows Attachment Manager/SmartScreen's own known-executable extension set.
    private static readonly HashSet<string> _unsafeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".com", ".cpl", ".msi", ".msp", ".scr", ".vbs", ".vbe", ".js",
        ".jse", ".ws", ".wsf", ".wsc", ".wsh", ".ps1", ".ps1xml", ".ps2", ".ps2xml", ".psc1",
        ".psc2", ".msh", ".mshxml", ".scf", ".lnk", ".inf", ".reg", ".hta"
    };

    // T-F45: Propagate Zone.Identifier ADS from archive to extracted file.
    // Best-effort — swallows all exceptions. Never fatal.
    // Silently no-ops if the archive has no Zone.Identifier ADS, if mode is Disabled, or (under
    // UnsafeExtensionsOnly, T-F51) if destFilePath's extension isn't in the unsafe-extension list.
    public static void TryPropagateMotw(string archivePath, string destFilePath, MotwMode mode = MotwMode.AllFiles)
    {
        if (mode == MotwMode.Disabled)
            return;

        if (mode == MotwMode.UnsafeExtensionsOnly && !_unsafeExtensions.Contains(Path.GetExtension(destFilePath)))
            return;

        try
        {
            using var source = new FileStream(
                archivePath + ":Zone.Identifier",
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using var dest = new FileStream(
                destFilePath + ":Zone.Identifier",
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            source.CopyTo(dest);
        }
        catch
        {
            // MOTW propagation is best-effort — never surfaces to caller
        }
    }

    // T-F94: replaces the old auto-reject-only model. An archive whose declared uncompressed
    // size exceeds MaxCompressionRatio against its compressed size is no longer always rejected —
    // if the destination has room for the declared size, the caller-supplied confirmCallback (if
    // any) decides; if there is not enough room, extraction is blocked regardless of the
    // callback. See DECISIONS.md's T-F94 entry for the full design trace.
    public static async Task<CompressionBombOutcome> EvaluateCompressionBombAsync(
        string archivePath,
        long declaredUncompressedSize,
        long compressedSize,
        long availableFreeSpaceBytes,
        Func<CompressionBombWarning, Task<bool>>? confirmCallback)
    {
        if (compressedSize <= 0 || declaredUncompressedSize <= 0)
            return CompressionBombOutcome.NotABomb;

        if (declaredUncompressedSize / compressedSize <= MaxCompressionRatio)
            return CompressionBombOutcome.NotABomb;

        if (availableFreeSpaceBytes < declaredUncompressedSize)
            return CompressionBombOutcome.InsufficientDiskSpace;

        if (confirmCallback is null)
            return CompressionBombOutcome.UserDeclined;

        bool proceed = await confirmCallback(new CompressionBombWarning
        {
            ArchivePath = archivePath,
            DeclaredUncompressedSize = declaredUncompressedSize,
            CompressedSize = compressedSize
        }).ConfigureAwait(false);

        return proceed ? CompressionBombOutcome.UserConfirmed : CompressionBombOutcome.UserDeclined;
    }

    // T-F94: GetDiskFreeSpaceExW instead of DriveInfo — DriveInfo's constructor throws on UNC
    // paths (\\server\share\...), which this app's destination folder picker can legitimately
    // point at. GetDiskFreeSpaceExW works uniformly for local drive letters and UNC shares.
    // Returns 0 (treated as "no room" by EvaluateCompressionBombAsync) if the call fails —
    // conservative default, consistent with this codebase's security-first posture on the
    // extraction path.
    public static long GetAvailableFreeSpace(string destinationPath)
    {
        try
        {
            // Use the volume/share root, not destinationPath itself — GetDiskFreeSpaceExW
            // requires an existing directory, and the exact destination subfolder (e.g. a
            // per-archive SeparateFolders subfolder) may not have been created yet at the point
            // this check runs.
            string? root = Path.GetPathRoot(Path.GetFullPath(destinationPath));
            if (string.IsNullOrEmpty(root))
                return 0;

            if (GetDiskFreeSpaceExW(root, out ulong freeBytesAvailable, out _, out _))
                return checked((long)freeBytesAvailable);
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailable,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);
}

internal enum CompressionBombOutcome
{
    NotABomb,
    InsufficientDiskSpace,
    UserDeclined,
    UserConfirmed
}
