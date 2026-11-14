using Archiver.Core.IO;

namespace Archiver.CLI;

/// <summary>
/// T-F244 item 4 / T-F263: one private %TEMP% folder owned by this pakko run for -si/-so staging.
/// Named by <see cref="TempOwner"/> (T-F511) so a later run can tell a dead process's leftover from a
/// live run's folder (<see cref="CliStreamStaging.SweepAbandoned"/>); <see cref="Dispose"/> removes
/// it, best-effort. T-F525: or a folder in the <see cref="WorkRoot"/> next to an extraction's
/// destination (<see cref="Near"/>), which goes with it.
/// </summary>
public sealed class CliStagingFolder : IDisposable
{
    private readonly WorkRoot? _work;

    private CliStagingFolder(string path, WorkRoot? work = null)
    {
        Path = path;
        _work = work;
    }

    /// <summary>The folder.</summary>
    public string Path { get; }

    /// <summary>Creates a new, never-reused folder under <paramref name="root"/>.</summary>
    public static CliStagingFolder Create(string root)
    {
        string path = System.IO.Path.Combine(root, TempOwner.NewName(""));
        Directory.CreateDirectory(path);
        return new CliStagingFolder(path);
    }

    /// <summary>T-F525: a new folder in the work root next to <paramref name="destination"/>. Throws
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when none can be made there.</summary>
    public static CliStagingFolder Near(string destination)
    {
        var work = WorkRoot.Near(destination);
        try
        {
            return new CliStagingFolder(work.Folder("si"), work);
        }
        catch
        {
            work.Dispose();
            throw;
        }
    }

    /// <summary>Deletes the folder and everything in it; what is held stays for the next run's sweep.</summary>
    public void Dispose()
    {
        if (_work is not null)
            _work.Dispose();
        else
            _ = TempOwner.TryRemove(Path);
    }
}
