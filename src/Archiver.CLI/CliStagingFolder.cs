using Archiver.Core.IO;

namespace Archiver.CLI;

/// <summary>
/// T-F244 item 4 / T-F263: one private %TEMP% folder owned by this pakko run for -si/-so staging.
/// Named by <see cref="TempOwner"/> (T-F511) so a later run can tell a dead process's leftover from a
/// live run's folder (<see cref="CliStreamStaging.SweepAbandoned"/>); <see cref="Dispose"/> removes
/// it, best-effort.
/// </summary>
public sealed class CliStagingFolder : IDisposable
{
    private CliStagingFolder(string path) => Path = path;

    /// <summary>The folder.</summary>
    public string Path { get; }

    /// <summary>Creates a new, never-reused folder under <paramref name="root"/>.</summary>
    public static CliStagingFolder Create(string root)
    {
        string path = System.IO.Path.Combine(root, TempOwner.NewName(""));
        Directory.CreateDirectory(path);
        return new CliStagingFolder(path);
    }

    /// <summary>Deletes the folder and everything in it; what is held stays for the next run's sweep.</summary>
    public void Dispose() => _ = TempOwner.TryRemove(Path);
}
