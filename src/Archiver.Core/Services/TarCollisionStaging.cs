using System.Diagnostics;
using System.Globalization;

namespace Archiver.Core.Services;

/// <summary>
/// T-F171/T-F286: the folder in %TEMP% where tar creation stages a source whose name collides with
/// another one — a folder as a junction to the user's own folder. The creation removes it when it
/// ends; a killed process cannot, so each creation first sweeps the folders left by processes that
/// no longer run. The folder name carries its owner's process id for that.
/// </summary>
internal static class TarCollisionStaging
{
    private const string Prefix = "PakkoTarStage_";
    private static readonly TimeSpan UnownedMaxAge = TimeSpan.FromDays(1);

    /// <summary>A new staging path for this process; the folder itself is not created.</summary>
    public static string NewDirectoryPath(string tempRoot) =>
        Path.Combine(tempRoot, string.Create(CultureInfo.InvariantCulture, $"{Prefix}{Environment.ProcessId}_{Guid.NewGuid():N}"));

    /// <summary>Sweeps <see cref="Path.GetTempPath"/> for staging folders no running process owns.</summary>
    public static void SweepStale() => SweepStale(Path.GetTempPath(), IsProcessAlive, DateTime.UtcNow);

    /// <summary>Best-effort: a folder that cannot be listed or removed stays for the next sweep.</summary>
    internal static void SweepStale(string tempRoot, Func<int, bool> isProcessAlive, DateTime utcNow)
    {
        string[] stagingDirs;
        try
        {
            stagingDirs = Directory.GetDirectories(tempRoot, Prefix + "*");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // best-effort: nothing to sweep in a folder that cannot be listed
        }

        foreach (string stagingDir in stagingDirs)
        {
            try
            {
                if (IsStale(stagingDir, isProcessAlive, utcNow))
                    RemoveLinksThenFolder(stagingDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // best-effort: this folder is swept by a later creation
            }
        }
    }

    private static bool IsStale(string stagingDir, Func<int, bool> isProcessAlive, DateTime utcNow)
    {
        ReadOnlySpan<char> owner = Path.GetFileName(stagingDir.AsSpan())[Prefix.Length..];
        int separator = owner.IndexOf('_');
        if (separator > 0 && int.TryParse(owner[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out int processId))
            return !isProcessAlive(processId);

        // Written before the name carried a process id: only its age can tell.
        return utcNow - Directory.GetCreationTimeUtc(stagingDir) > UnownedMaxAge;
    }

    // Every link is removed on its own first (a non-recursive delete removes only the link); the
    // recursive delete runs only once none is left, so it never reaches the user's own files.
    private static void RemoveLinksThenFolder(string stagingDir)
    {
        foreach (string entry in Directory.EnumerateDirectories(stagingDir))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                Directory.Delete(entry, recursive: false);
        }
        Directory.Delete(stagingDir, recursive: true);
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return true;
        }
        catch (ArgumentException)
        {
            return false; // no process has this id
        }
    }
}
