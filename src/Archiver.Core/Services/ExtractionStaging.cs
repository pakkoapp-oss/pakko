namespace Archiver.Core.Services;

/// <summary>
/// A staging folder owned by one extraction run (T-F263, T-F227): created fresh under a unique
/// name, never reused, and on <see cref="Dispose"/> removes only itself. The old fixed
/// "&lt;dest&gt;_tmp" name reused — and then deleted — a user's own folder of that name.
/// </summary>
internal sealed class ExtractionStaging : IDisposable
{
    // The PID lets a later startup sweep (rest of T-F263) tell a dead process's leftover from a
    // live run's folder.
    private const string NamePrefix = ".pakko-x-";

    private ExtractionStaging(string path)
    {
        Path = path;
        FullPathWithSeparator = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path))
            + System.IO.Path.DirectorySeparatorChar;
    }

    /// <summary>The staging folder.</summary>
    public string Path { get; }

    /// <summary>The full staging path with a trailing separator, for prefix checks.</summary>
    public string FullPathWithSeparator { get; }

    /// <summary>Creates a new staging folder inside <paramref name="stagingRoot"/>, which must be
    /// on the same volume as the destination so the commit can rename instead of copy.</summary>
    public static ExtractionStaging Create(string stagingRoot)
    {
        string path;
        do
        {
            path = System.IO.Path.Combine(stagingRoot,
                $"{NamePrefix}{Environment.ProcessId}-{Guid.NewGuid():N}");
        } while (Directory.Exists(path) || File.Exists(path));

        var info = Directory.CreateDirectory(path);
        info.Attributes |= FileAttributes.Hidden;
        return new ExtractionStaging(path);
    }

    // T-F161: fast-path atomic rename if actualDest doesn't exist yet, otherwise a per-file merge
    // so pre-existing files (e.g. skipped/renamed) are preserved. moveOverride exists purely for
    // test injection (defaults to the real Directory.Move).
    //
    // The fast path's Directory.Move fails the WHOLE source tree with IOException — naming only
    // the staging folder, never the specific offending file — whenever ANY file anywhere inside
    // it is transiently held open by another process (real-time antivirus scan, cloud-sync
    // filter, Search Indexer). Confirmed empirically — see DECISIONS.md's T-F161 entry. Falling
    // back to the per-file merge tolerates one still-locked file instead of losing the whole
    // extraction to it.
    //
    // T-F170: a single locked destination file in the merge loop is a per-item failure — returns
    // the relative paths that could not be moved so the caller can record an ArchiveError for
    // each. Catches both IOException AND UnauthorizedAccessException: a real locked-file
    // File.Move(overwrite: true) was confirmed to throw the latter.
    /// <summary>Moves everything staged into <paramref name="actualDest"/>; returns the relative
    /// paths of files that could not be moved.</summary>
    public IReadOnlyList<string> CommitInto(string actualDest, Action<string, string>? moveOverride = null)
    {
        if (!Directory.Exists(actualDest))
        {
            // The rename carries the folder's attributes with it.
            var info = new DirectoryInfo(Path);
            info.Attributes &= ~FileAttributes.Hidden;
            try
            {
                (moveOverride ?? Directory.Move)(Path, actualDest);
                return [];
            }
            catch (IOException)
            {
                Directory.CreateDirectory(actualDest);
            }
        }

        // T-F197: folders first, so an empty one arrives too.
        foreach (string folder in Directory.EnumerateDirectories(Path, "*", SearchOption.AllDirectories).ToList())
            Directory.CreateDirectory(System.IO.Path.Combine(actualDest, System.IO.Path.GetRelativePath(Path, folder)));

        var lockedRelativePaths = new List<string>();
        foreach (string file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories).ToList())
        {
            string relative = System.IO.Path.GetRelativePath(Path, file);
            string finalFile = System.IO.Path.Combine(actualDest, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(finalFile)!);
            try
            {
                File.Move(file, finalFile, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lockedRelativePaths.Add(relative);
            }
        }

        // A file left behind above failed to move (locked); Dispose removes it with the rest of
        // the staging folder — it cannot reach its destination, and must not be left behind.
        return lockedRelativePaths;
    }

    /// <summary>Removes the staging folder (best-effort).</summary>
    public void Dispose()
    {
        try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); } catch { /* best-effort */ }
    }
}
