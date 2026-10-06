using System.Runtime.InteropServices;

namespace Archiver.Core.Services;

/// <summary>
/// T-F285: a drive made by <c>subst</c> is a DOS device name for a folder, not a volume. tar.exe
/// asks Windows for the volume of each path it archives and fails for a file directly in such a
/// drive's root ("GetVolumePathName failed: 123"), so tar.exe is given the folder itself.
/// </summary>
internal static partial class SubstDrive
{
    private const string NtPathPrefix = @"\??\";
    private const int DeviceNameCapacity = 1024;

    /// <summary>
    /// <paramref name="fullPath"/> with a <c>subst</c> drive replaced by the folder it stands for;
    /// <paramref name="fullPath"/> itself for any other path.
    /// </summary>
    public static string Resolve(string fullPath)
    {
        if (fullPath.Length < 3 || !char.IsAsciiLetter(fullPath[0]) || fullPath[1] != ':' || fullPath[2] != '\\')
            return fullPath;

        char[] device = new char[DeviceNameCapacity];
        if (QueryDosDeviceW(fullPath[..2], device, (uint)device.Length) == 0)
            return fullPath;
        int end = Array.IndexOf(device, '\0');
        if (end < 0)
            return fullPath;

        // A volume answers "\Device\HarddiskVolume3", a network drive "\Device\LanmanRedirector\...";
        // only subst answers with a DOS path.
        string target = new(device, 0, end);
        if (!target.StartsWith(NtPathPrefix, StringComparison.Ordinal))
            return fullPath;
        target = target[NtPathPrefix.Length..];
        if (target.Length < 3 || !char.IsAsciiLetter(target[0]) || target[1] != ':' || target[2] != '\\')
            return fullPath;

        string rest = fullPath[3..];
        return rest.Length == 0 ? Path.TrimEndingDirectorySeparator(target) : Path.Combine(target, rest);
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint QueryDosDeviceW(string deviceName, [Out] char[] targetPath, uint capacity);
}
