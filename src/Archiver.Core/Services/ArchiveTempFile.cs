using Archiver.Core.IO;

namespace Archiver.Core.Services;

/// <summary>
/// T-F312: the temporary file an archive is written to before it is renamed into place — ZIP and
/// tar creation alike. Its name is unique to the run (a fixed "&lt;archive&gt;.tmp" left by a killed
/// run, or held by a sync client, failed every later run), carries no part of the archive's name (a
/// long archive name would hit the 255-character limit), and is not hidden (the rename would carry
/// the attribute to the archive, and tar.exe cannot overwrite a hidden file). T-F528: it lies in the
/// operation's <see cref="WorkRoot"/>, which is hidden, with whatever else the writer needs (the
/// parallel writer's chunk folder, tar.exe's exclusion list and 7z writer folder): one entry next to
/// the archive while it is written, none after.
/// </summary>
internal static class ArchiveTempFile
{
    /// <summary>The folder the parallel ZIP writer keeps its compressed chunks in, beside the temporary file.</summary>
    internal const string ChunkFolderPrefix = ".pakko-tmp-";
    // Each attempt takes the next free "name (N)"; another run would have to claim every one first.
    private const int MaxFreeNameAttempts = 10;

    // About 1.5 s in all: long enough for a scanner or sync client that opened the file for a moment.
    private static readonly int[] RetryDelaysMs = [100, 200, 400, 800];

    /// <summary>The work root of an archive written to <paramref name="destPath"/>: next to it. Every
    /// creation makes one (T-F379: a small one too), and its first role sweeps what killed runs left
    /// there; one archive per item makes one per destination folder for the whole run (T-F497: a
    /// sweep per archive listed a folder of thousands of entries thousands of times).</summary>
    public static WorkRoot WorkRootFor(string destPath) => WorkRoot.Near(FolderOf(destPath));

    /// <summary>A new temporary path in <paramref name="work"/>; the file itself is not created.</summary>
    public static string NewPath(WorkRoot work) => work.NewFile("a", ".tmp");

    internal static string FolderOf(string destPath) => Path.GetDirectoryName(Path.GetFullPath(destPath))!;

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
                return Landed(Move(tempPath, destPath, replaceExisting));
            }
            catch (Exception ex) when (IsHeld(ex, destPath))
            {
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }
        return Landed(Move(tempPath, destPath, replaceExisting));
    }

    // Written a level deeper, in the work root (T-F528).
    private static string Landed(string archive)
    {
        WorkRoot.TakeParentEntries(archive);
        return archive;
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
            catch (IOException ex) when (CoreMessages.IsAlreadyExists(ex) && File.Exists(target) && File.Exists(tempPath))
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
        IOException io => CoreMessages.IsSharingViolation(io),
        _ => false,
    };
}
