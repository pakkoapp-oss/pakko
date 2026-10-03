using Archiver.Core.IO;

namespace Archiver.Core.Services;

/// <summary>
/// T-F171/T-F286: the folder in %TEMP% where tar creation stages a source whose name collides with
/// another one — a folder as a junction to the user's own folder. The creation removes it when it
/// ends; a killed process cannot, so each creation first sweeps the folders left by processes that
/// no longer run (<see cref="TempOwner"/>, which removes the links before the folder).
/// </summary>
internal static class TarCollisionStaging
{
    private const string Prefix = "PakkoTarStage_";
    private static readonly TimeSpan UnownedMaxAge = TimeSpan.FromDays(1);

    /// <summary>A new staging path for this process; the folder itself is not created.</summary>
    public static string NewDirectoryPath(string tempRoot) => Path.Combine(tempRoot, TempOwner.NewName(Prefix));

    /// <summary>Sweeps <see cref="Path.GetTempPath"/> for staging folders no running process owns.</summary>
    public static void SweepStale() =>
        TempOwner.SweepStale(Path.GetTempPath(), Prefix, "", TempScope.LocalTemp(UnownedMaxAge));

    /// <summary>Test seam: <paramref name="isProcessAlive"/> decides by process id alone.</summary>
    internal static void SweepStale(string tempRoot, Func<int, bool> isProcessAlive, DateTime utcNow) =>
        TempOwner.SweepStale(tempRoot, Prefix, "", TempScope.LocalTemp(UnownedMaxAge),
            new TempOwnerProbe("", (pid, _) => isProcessAlive(pid), utcNow));
}
