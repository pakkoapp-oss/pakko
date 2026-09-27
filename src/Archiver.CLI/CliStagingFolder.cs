namespace Archiver.CLI;

/// <summary>
/// T-F244 item 4 / T-F263: one private %TEMP% folder owned by this pakko run for -si/-so staging.
/// Named <c>&lt;pid&gt;-&lt;guid&gt;</c> so a later run can tell a dead process's leftover from a
/// live run's folder (<see cref="CliStreamStaging.SweepAbandoned(string, Func{int, DateTime, bool})"/>);
/// <see cref="Dispose"/> removes it, best-effort.
/// </summary>
public sealed class CliStagingFolder : IDisposable
{
    private CliStagingFolder(string path) => Path = path;

    /// <summary>The folder.</summary>
    public string Path { get; }

    /// <summary>Creates a new, never-reused folder under <paramref name="root"/>.</summary>
    public static CliStagingFolder Create(string root)
    {
        string path = System.IO.Path.Combine(root, $"{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new CliStagingFolder(path);
    }

    /// <summary>Deletes the folder and everything in it; a failure is ignored.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: the next run's sweep removes what is left once this process has exited.
        }
    }
}
