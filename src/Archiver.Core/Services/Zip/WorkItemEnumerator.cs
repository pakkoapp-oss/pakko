using Archiver.Core.Models;

namespace Archiver.Core.Services.Zip;

/// <summary>
/// Produces the exact same deterministic sequence of files/directory-placeholders that
/// <c>ZipArchiveService.AddDirectoryToArchiveAsync</c>/<c>AddEntryFromFileAsync</c> already walk
/// for <c>SingleArchive</c> mode (T-F31/T-F32 sorted order, T-F30 top-level basename collision
/// renaming, T-F23 reparse-point skip, T-F66 empty-directory placeholders, T-F75 fixed rootDir),
/// as a lazy sequence instead of an inline recursive write — used by
/// <see cref="ParallelSingleArchiveWriter"/> so entry ORDER is fixed by enumeration order alone,
/// independent of which worker finishes compressing which file first.
///
/// Recursive traversal deliberately enumerates via <see cref="DirectoryInfo.EnumerateFiles()"/>/
/// <see cref="DirectoryInfo.EnumerateDirectories()"/> (returning <see cref="FileSystemInfo"/>
/// objects) rather than the plain string-path <c>Directory.EnumerateFiles</c>/
/// <c>EnumerateDirectories</c> — on Windows both are backed by the same underlying
/// <c>FindNextFile</c> walk, which already returns a <c>WIN32_FIND_DATA</c> per entry containing
/// size, timestamps, and attributes; the <see cref="FileSystemInfo"/>-returning overloads cache
/// that data on the returned object, while the plain string overloads discard it. Reading
/// <c>Length</c>/<c>LastWriteTime</c>/<c>Attributes</c> off an already-enumerated
/// <see cref="FileInfo"/>/<see cref="DirectoryInfo"/> therefore costs zero extra stat calls,
/// versus three separate ones per file previously (<c>File.GetAttributes</c> for the reparse-point
/// check, <c>new FileInfo(path).Length</c>, <c>File.GetLastWriteTime(path)</c>) — real overhead at
/// thousands of files that a single large file never pays enough of to notice.
/// </summary>
internal static class WorkItemEnumerator
{
    public static IEnumerable<FileWorkItem> Enumerate(
        IReadOnlyList<string> sortedSourcePaths,
        Action<SkippedFile> reportSkipped,
        Action<ArchiveError> reportError)
    {
        var usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string sourcePath in sortedSourcePaths)
        {
            if (ArchiveEntrySecurity.IsReparsePoint(sourcePath))
            {
                reportSkipped(new SkippedFile
                {
                    Path = sourcePath,
                    Reason = "Symbolic links and NTFS junctions are not archived.",
                });
                continue;
            }

            if (Directory.Exists(sourcePath))
            {
                string entryName = ZipArchiveService.GetUniqueEntryName(usedEntryNames, Path.GetFileName(sourcePath));
                foreach (var item in EnumerateDirectory(sourcePath, sourcePath, entryName, reportSkipped))
                    yield return item;
            }
            else if (File.Exists(sourcePath))
            {
                string entryName = ZipArchiveService.GetUniqueEntryName(usedEntryNames, Path.GetFileName(sourcePath));
                yield return BuildFileItem(new FileInfo(sourcePath), entryName);
            }
            else
            {
                reportError(new ArchiveError
                {
                    SourcePath = sourcePath,
                    Message = $"Source path does not exist: {sourcePath}",
                });
            }
        }
    }

    private static IEnumerable<FileWorkItem> EnumerateDirectory(
        string sourceDir, string rootDir, string entryPrefix, Action<SkippedFile> reportSkipped)
    {
        if (!Directory.EnumerateFileSystemEntries(sourceDir).Any())
        {
            string relativeDir = Path.GetRelativePath(rootDir, sourceDir);
            string emptyEntryName = relativeDir == "."
                ? entryPrefix + "/"
                : entryPrefix + "/" + relativeDir.Replace('\\', '/') + "/";
            yield return new FileWorkItem(string.Empty, emptyEntryName, FileWorkKind.DirectoryPlaceholder, 0, DateTime.Now);
            yield break;
        }

        foreach (var fileInfo in new DirectoryInfo(sourceDir).EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase))
        {
            if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                reportSkipped(new SkippedFile
                {
                    Path = fileInfo.FullName,
                    Reason = "Symbolic links and reparse points are not archived.",
                });
                continue;
            }

            string relativePath = Path.GetRelativePath(rootDir, fileInfo.FullName).Replace('\\', '/');
            yield return BuildFileItem(fileInfo, entryPrefix + "/" + relativePath);
        }

        foreach (var dirInfo in new DirectoryInfo(sourceDir).EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
            .OrderBy(d => d.FullName, StringComparer.OrdinalIgnoreCase))
        {
            if (dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                reportSkipped(new SkippedFile
                {
                    Path = dirInfo.FullName,
                    Reason = "NTFS junctions and directory symbolic links are not followed during archiving.",
                });
                continue;
            }

            foreach (var item in EnumerateDirectory(dirInfo.FullName, rootDir, entryPrefix, reportSkipped))
                yield return item;
        }
    }

    private static FileWorkItem BuildFileItem(FileInfo fileInfo, string entryName)
    {
        long size = 0;
        try { size = fileInfo.Length; } catch { }

        // T-F31: pinned to the source file's real mtime, same as AddEntryFromFileAsync — kept
        // as local time (LastWriteTime, not LastWriteTimeUtc) to match the existing
        // ZipArchiveEntry.LastWriteTime convention exactly (DOS date/time has no timezone concept).
        DateTime lastWriteTime;
        try { lastWriteTime = fileInfo.LastWriteTime; } catch { lastWriteTime = DateTime.Now; }

        return new FileWorkItem(fileInfo.FullName, entryName, FileWorkKind.File, size, lastWriteTime);
    }
}
