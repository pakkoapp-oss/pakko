using System.Runtime.InteropServices;
using Archiver.Core.Interfaces;
using Archiver.Core.IO;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// T-F524: where an extraction's quarantine goes. Next to its destination, in the nearest folder of
/// it that already exists, when that folder is on another fixed local volume than <c>%TEMP%</c> and
/// that volume keeps permissions: the files then leave the quarantine by a rename on one volume, and
/// nothing of the archive is written to the system drive first. Otherwise (the volume of
/// <c>%TEMP%</c>, where a rename out of it works already; a network folder, FAT or exFAT, a removable
/// drive, a name Windows would trim, a folder too deep) under <c>%TEMP%</c>, as before. The folder is taken by
/// its final path (<see cref="PathIdentity.Resolve"/>), and its volume asked of a handle to it, so a
/// junction or a mount point is judged by where it leads, never by the drive letter it starts from.
/// </summary>
internal static partial class QuarantineLocation
{
    /// <summary>The prefix of a quarantine root next to a destination.</summary>
    internal const string RootPrefix = ".pakko-sandbox-";

    /// <summary>The folder, by its final plain path, to hold the quarantine of an extraction into
    /// <paramref name="destination"/>; null for <c>%TEMP%</c>. A folder on one of <paramref name="keptInTemp"/>
    /// (<see cref="VolumesKeptInTemp"/>) never qualifies. The archive helper writes it only where
    /// Controlled Folder Access does not block it (<see cref="ControlledFolderAccessBlocks"/>).</summary>
    public static string? NearDestination(string destination, bool byHelper, IRegistryReader registry, IReadOnlyList<string> keptInTemp)
    {
        if ((byHelper && ControlledFolderAccessBlocks(registry)) || NearestExisting(destination) is not { } existing || PathIdentity.Resolve(existing) is not { } resolved)
            return null;
        // The plain path is what every sandboxed run gets as its working folder: a \\?\ or \\.\
        // spelling reaches tar.exe mangled, and a name Windows trims would lead elsewhere (T-F509).
        string folder = resolved.FinalPath;
        if (folder.StartsWith(@"\\", StringComparison.Ordinal) || InputPaths.NameTrimmed(folder) || !RootFits(folder))
            return null;
        return PathIdentity.VolumeOf(folder) is { } volume
            && !keptInTemp.Contains(volume.VolumeRoot, StringComparer.OrdinalIgnoreCase)
            && Accepts(volume.VolumeRoot, volume.FileSystemFlags, GetDriveType)
            ? folder
            : null;
    }

    /// <summary>
    /// The volume roots (<c>\\?\Volume{...}\</c>) where an extraction quarantines in <c>%TEMP%</c>,
    /// asked once per process: <c>%TEMP%</c>'s, where a rename out of it works already, and the user
    /// profile's, where the folders Controlled Folder Access protects by default and OneDrive syncs are
    /// (a hidden quarantine never goes into them, %TEMP% moved to another drive or not). Null when either
    /// cannot be asked: then every extraction quarantines in %TEMP% (batch 5a's review).
    /// </summary>
    internal static IReadOnlyList<string>? VolumesKeptInTemp => KeptInTemp.Value;

    private static readonly Lazy<IReadOnlyList<string>?> KeptInTemp = new(() =>
        PathIdentity.VolumeOf(Path.GetTempPath())?.VolumeRoot is { } temp
        && PathIdentity.VolumeOf(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))?.VolumeRoot is { } profile
            ? [temp, profile]
            : null);

    // The destination is created only after the archive passed its checks: a folder that is not
    // there yet must not appear for an archive that is refused.
    internal static string? NearestExisting(string destination)
    {
        string? folder = Path.GetFullPath(destination);
        while (folder is not null && !Directory.Exists(folder))
            folder = Path.GetDirectoryName(folder);
        return folder;
    }

    // The root is the working folder of every sandboxed run, and CreateProcess takes no working
    // folder of MAX_PATH or longer (error 267, measured with a 200-character destination). The
    // margin keeps a folder name of the longest owner tag inside it.
    internal const int MaxRootLength = 240;

    // A pid and start ticks longer than this process's in another one.
    private const int OwnerTagMargin = 16;

    internal static bool RootFits(string folder) =>
        Path.Combine(folder, TempOwner.NewName(RootPrefix)).Length + OwnerTagMargin <= MaxRootLength;

    internal static bool Accepts(string? volumeRoot, uint volumeFlags, Func<string, uint> driveType) =>
        volumeRoot is not null && (volumeFlags & FilePersistentAcls) != 0 && driveType(volumeRoot) == DriveFixed;

    // Controlled Folder Access blocks an unsigned program writing into a protected folder: the
    // helper's copy, writing its out\ into Documents, was refused (event 1123, measured on PakkoW10,
    // 2026-11-13); tar.exe, signed by Microsoft, was let through. Which folders are protected is
    // Defender's list, so in block mode the helper keeps its quarantine in %TEMP%, as before T-F524.
    // Block mode (1) set by any channel a standard user can read is enough: the local setting, Group
    // Policy's key, or MDM's (the winning provider's value under PolicyManager; Defender's own copy
    // of it, Policies\...\Policy Manager, is readable by administrators only - measured on PakkoW10).
    // Audit (2) and the disk-sector modes (3, 4) write nothing off.
    internal static bool ControlledFolderAccessBlocks(IRegistryReader registry) =>
        registry.GetDword(DefenderKey, CfaValue) == CfaBlock
        || registry.GetDword(PolicyKey, CfaValue) == CfaBlock
        || (Guid.TryParse(registry.GetString(MdmCurrentKey, CfaValue + "_WinningProvider"), out Guid provider)
            && registry.GetDword($@"SOFTWARE\Microsoft\PolicyManager\Providers\{provider:D}\default\Device\Defender", CfaValue) == CfaBlock);

    private const string DefenderKey = @"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access";
    private const string PolicyKey = @"SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access";
    private const string MdmCurrentKey = @"SOFTWARE\Microsoft\PolicyManager\current\device\Defender";
    private const string CfaValue = "EnableControlledFolderAccess";
    private const int CfaBlock = 1;

    private const uint FilePersistentAcls = 0x00000008;
    private const uint DriveFixed = 3;

    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveType(string rootPathName);
}
