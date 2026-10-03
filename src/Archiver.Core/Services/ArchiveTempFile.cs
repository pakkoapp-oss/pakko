using Archiver.Core.IO;

namespace Archiver.Core.Services;

/// <summary>
/// T-F312: the temporary file an archive is written to before it is renamed into place — ZIP and
/// tar creation alike. Its name is unique to the run (a fixed "&lt;archive&gt;.tmp" left by a killed
/// run, or held by a sync client, failed every later run), carries no part of the archive's name (a
/// long archive name would hit the 255-character limit), and is not hidden (the rename would carry
/// the attribute to the archive, and tar.exe cannot overwrite a hidden file).
/// </summary>
internal static class ArchiveTempFile
{
    private const string Prefix = ".pakko-a-";
    private const string Suffix = ".tmp";
    private const int SharingViolation = 0x20;
    private const int LockViolation = 0x21;
    private const int FileExists = 0x50;
    private const int AlreadyExists = 0xB7;
    // Each attempt takes the next free "name (N)"; another run would have to claim every one first.
    private const int MaxFreeNameAttempts = 10;

    // About 1.5 s in all: long enough for a scanner or sync client that opened the file for a moment.
    private static readonly int[] RetryDelaysMs = [100, 200, 400, 800];

    /// <summary>Sweeps the archive's folder for temporary files of runs that no longer run, and
    /// returns a new temporary path there. The file itself is not created.</summary>
    public static string Create(string destPath)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(destPath))!;
        TempOwner.SweepStale(folder, Prefix, Suffix, TempScope.Destination);
        return Path.Combine(folder, TempOwner.NewName(Prefix, Suffix));
    }

    /// <summary>
    /// Overwrite keeps the old archive until the commit replaces it — except when the archive lies
    /// inside a folder being archived: the walk would pack the old archive into the new one, so it is
    /// deleted first, as before T-F312. Throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> when it cannot be deleted; the callers' catches report it.
    /// </summary>
    public static void RemoveOldArchiveInsideSources(string destPath, IEnumerable<string> sourcePaths)
    {
        if (!File.Exists(destPath))
            return;
        string dest = Path.GetFullPath(destPath);
        foreach (string source in sourcePaths)
        {
            if (!Directory.Exists(source))
                continue;
            string folder = Path.GetFullPath(source);
            if (!Path.EndsInDirectorySeparator(folder))
                folder += Path.DirectorySeparatorChar; // a drive root already ends in one
            if (dest.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(destPath);
                return;
            }
        }
    }

    /// <summary>Renames <paramref name="tempPath"/> onto <paramref name="destPath"/>, retrying
    /// briefly while either file is held by another process. Returns the path the archive landed at.
    /// <paramref name="replaceExisting"/> is true only when the archive existed when the run started
    /// and the caller chose to overwrite it. Otherwise an archive that appeared during the run (T-F321:
    /// a second run creating the same name at once) is kept, and this one takes the next free name.</summary>
    public static async Task<string> CommitAsync(string tempPath, string destPath, bool replaceExisting, CancellationToken cancellationToken)
    {
        foreach (int delayMs in RetryDelaysMs)
        {
            try
            {
                return Move(tempPath, destPath, replaceExisting);
            }
            catch (Exception ex) when (IsHeld(ex, destPath))
            {
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }
        return Move(tempPath, destPath, replaceExisting);
    }

    private static string Move(string tempPath, string destPath, bool replaceExisting)
    {
        if (replaceExisting)
        {
            File.Move(tempPath, destPath, overwrite: true);
            return destPath;
        }

        string target = destPath;
        for (int attempt = 1; attempt < MaxFreeNameAttempts; attempt++)
        {
            try
            {
                File.Move(tempPath, target, overwrite: false);
                return target;
            }
            catch (IOException ex) when (IsAlreadyExists(ex) && File.Exists(target) && File.Exists(tempPath))
            {
                target = ArchiveNaming.GetUniqueFilePath(destPath);
            }
        }
        File.Move(tempPath, target, overwrite: false);
        return target;
    }

    // File.Move(overwrite: true) onto a file another process holds throws UnauthorizedAccessException,
    // not a sharing violation (T-F170).
    private static bool IsHeld(Exception ex, string destPath) => ex switch
    {
        UnauthorizedAccessException => File.Exists(destPath),
        IOException io => (io.HResult & 0xFFFF) is SharingViolation or LockViolation,
        _ => false,
    };

    private static bool IsAlreadyExists(IOException ex) => (ex.HResult & 0xFFFF) is FileExists or AlreadyExists;
}
