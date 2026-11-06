using Archiver.Core.Models;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// T-F463: every disk call that reaches a name tar.exe wrote in the quarantine. tar.exe writes
/// through the long-path prefix, so "a.txt." and "d " are on disk as they are (3.8.9 and 3.5.2,
/// measured); asked for without it, or normalized by Path.GetFullPath/GetRelativePath, they are
/// "a.txt" and "d" (T-F419, T-F460). Paths in and out are plain full paths; only this class adds
/// the prefix. QuarantineFilesOwnerTests keeps every other such call out of the files that handle
/// the quarantine.
/// </summary>
internal static class QuarantineFiles
{
    /// <summary>A quarantined file's path below <paramref name="root"/> as tar.exe wrote it.</summary>
    /// <exception cref="IOException"><paramref name="file"/> is not below <paramref name="root"/>
    /// as spelled; Path.GetRelativePath would quietly turn "a.txt." into "a.txt".</exception>
    public static string Relative(string root, string file)
    {
        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        return file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? file[prefix.Length..]
            : throw new CoreTextIOException(CoreMessages.Text(MessageCode.CannotReadArchive, file));
    }

    /// <summary>The files below <paramref name="root"/>, never entering a reparse-point folder.</summary>
    public static IEnumerable<string> EnumerateFiles(string root) =>
        Walk(root).Where(item => !item.IsFolder).Select(item => item.Path);

    // Walks the quarantine without ever recursing into a reparse-point subdirectory: a plain
    // Directory.EnumerateFiles(..., AllDirectories) would follow such a directory and could walk
    // straight out of quarantine. The pre-scan already rejects any archive containing a symlink
    // entry, so this is defense-in-depth, not the primary safety mechanism (T-F146).
    // T-F443: folders included (a reparse-point folder is neither yielded nor entered).
    public static IEnumerable<(string Path, bool IsFolder)> Walk(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string dir = pending.Pop();

            // T-F460: a folder that cannot be read fails the walk: a silent skip left its files
            // unscanned (Clean) and not extracted.
            List<string> files;
            List<string> subDirs;
            try
            {
                files = [.. Directory.EnumerateFiles(Literal(dir)).Select(f => Path.Join(dir, Path.GetFileName(f)))];
                subDirs = [.. Directory.EnumerateDirectories(Literal(dir)).Select(d => Path.Join(dir, Path.GetFileName(d)))];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new CoreTextIOException(CoreMessages.Text(MessageCode.CannotReadArchive, CoreMessages.Detail(ex)), ex);
            }

            foreach (string file in files)
                yield return (file, false);

            // Pushed last to first, so they are walked in the order listed: "d" before "d " (a name
            // sorts before itself with a trailing space or dot), and the plain one takes the name at
            // the destination, as in ZIP (T-F460).
            foreach (string subDir in Enumerable.Reverse(subDirs).Where(d => !ArchiveEntrySecurity.IsReparsePoint(Literal(d))))
            {
                yield return (subDir, true);
                pending.Push(subDir);
            }
        }
    }

    public static FileInfo Info(string file) => new(Literal(file));

    public static FileStream OpenRead(string file) => new(Literal(file), FileMode.Open, FileAccess.Read, FileShare.Read);

    public static bool FolderExists(string folder) => Directory.Exists(Literal(folder));

    public static DateTime FolderTime(string folder) => Directory.GetLastWriteTimeUtc(Literal(folder));

    public static void CreateFolder(string folder) => Directory.CreateDirectory(Literal(folder));

    /// <summary>Moves a quarantined file out to <paramref name="destination"/>, a path Pakko chose.</summary>
    public static void MoveOut(string file, string destination) => File.Move(Literal(file), destination, overwrite: true);

    private static string Literal(string fullPath)
    {
        if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
            return fullPath;
        return fullPath.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + fullPath[2..] : @"\\?\" + fullPath;
    }
}
