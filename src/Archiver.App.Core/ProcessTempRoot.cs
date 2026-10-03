using Archiver.Core.IO;

namespace Archiver.App.Core;

/// <summary>
/// T-F252: one shared %TEMP% root with a subfolder per Pakko process. Pakko is multi-process by
/// design (T-F88), so a window may delete only its own subfolder; <see cref="SweepStale"/> removes
/// the subfolders of processes that are gone (a crash or a kill leaves previewed plaintext behind).
/// </summary>
public sealed class ProcessTempRoot
{
    private readonly Func<string, bool> _isOwnerAlive;

    /// <summary>Creates a root; <paramref name="isOwnerAlive"/> decides whether a subfolder's owner still runs.</summary>
    public ProcessTempRoot(string sharedRoot, string ownerName, Func<string, bool> isOwnerAlive)
    {
        SharedRoot = sharedRoot;
        OwnRoot = Path.Combine(sharedRoot, ownerName);
        _isOwnerAlive = isOwnerAlive;
    }

    /// <summary>The folder every process's subfolder lives under.</summary>
    public string SharedRoot { get; }

    /// <summary>This process's subfolder.</summary>
    public string OwnRoot { get; }

    /// <summary>This process's subfolder name (<see cref="TempOwner.ProcessTag"/>, T-F263): the
    /// v1.6.0 shape, so a v1.6.0 window still open next to this one sees a live owner.</summary>
    public static string CurrentOwnerName => TempOwner.ProcessTag;

    /// <summary>True when the named process still runs here (<see cref="TempOwner.IsRunningHere"/>).</summary>
    public static bool IsOwnerAlive(string ownerName) => TempOwner.IsRunningHere(ownerName);

    /// <summary>Creates a fresh scope directory under <see cref="OwnRoot"/> and returns its path.</summary>
    public string CreateScope()
    {
        string dir = Path.Combine(OwnRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Deletes this process's subfolder. Best-effort, never throws.</summary>
    public void DeleteOwn() => TryDelete(OwnRoot);

    /// <summary>Deletes the subfolders of processes that no longer run, and stray files. Best-effort.</summary>
    public void SweepStale()
    {
        string[] entries;
        try
        {
            if (!Directory.Exists(SharedRoot))
                return;
            entries = Directory.GetFileSystemEntries(SharedRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // best-effort — the next start tries again
        }

        foreach (string entry in entries)
        {
            if (string.Equals(entry, OwnRoot, StringComparison.OrdinalIgnoreCase))
                continue;
            if (File.Exists(entry))
            {
                try { File.Delete(entry); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best-effort */ }
                continue;
            }
            if (!_isOwnerAlive(Path.GetFileName(entry)))
                TryDelete(entry);
        }
    }

    internal static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort — a file still open in the previewing app blocks it; SweepStale retries
        }
    }
}
