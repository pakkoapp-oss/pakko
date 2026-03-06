using System.IO.Compression;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// ZIP archive service using System.IO.Compression.
/// Never throws to callers — all errors are captured in ArchiveResult.Errors.
/// </summary>
public sealed class ZipArchiveService : IArchiveService
{
    /// <inheritdoc/>
    public async Task<ArchiveResult> ArchiveAsync(
        ArchiveOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ArchiveError>();
        var createdFiles = new List<string>();

        if (options.Mode == ArchiveMode.SingleArchive)
        {
            string archiveName = options.ArchiveName
                ?? (options.SourcePaths.Count == 1
                    ? Path.GetFileNameWithoutExtension(options.SourcePaths[0])
                    : "archive");

            string destPath = Path.Combine(options.DestinationFolder, archiveName + ".zip");

            try
            {
                Directory.CreateDirectory(options.DestinationFolder);

                using var archive = ZipFile.Open(destPath, ZipArchiveMode.Create);

                int total = options.SourcePaths.Count;
                for (int i = 0; i < total; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    string sourcePath = options.SourcePaths[i];

                    try
                    {
                        if (Directory.Exists(sourcePath))
                        {
                            AddDirectoryToArchive(archive, sourcePath, Path.GetFileName(sourcePath));
                        }
                        else if (File.Exists(sourcePath))
                        {
                            archive.CreateEntryFromFile(sourcePath, Path.GetFileName(sourcePath), CompressionLevel.Optimal);
                        }
                        else
                        {
                            errors.Add(new ArchiveError
                            {
                                SourcePath = sourcePath,
                                Message = $"Source path does not exist: {sourcePath}"
                            });
                        }
                    }
                    catch (IOException ex)
                    {
                        errors.Add(new ArchiveError
                        {
                            SourcePath = sourcePath,
                            Message = $"Cannot access file: {ex.Message}",
                            Exception = ex
                        });
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        errors.Add(new ArchiveError
                        {
                            SourcePath = sourcePath,
                            Message = $"Access denied: {ex.Message}",
                            Exception = ex
                        });
                    }

                    progress?.Report((i + 1) * 100 / total);
                }
            }
            catch (IOException ex)
            {
                errors.Add(new ArchiveError
                {
                    SourcePath = destPath,
                    Message = $"Cannot create archive: {ex.Message}",
                    Exception = ex
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                errors.Add(new ArchiveError
                {
                    SourcePath = destPath,
                    Message = $"Access denied creating archive: {ex.Message}",
                    Exception = ex
                });
            }

            if (errors.Count == 0 && File.Exists(destPath))
                createdFiles.Add(destPath);
        }
        else // SeparateArchives
        {
            Directory.CreateDirectory(options.DestinationFolder);

            int total = options.SourcePaths.Count;
            for (int i = 0; i < total; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                string sourcePath = options.SourcePaths[i];
                string baseName = Path.GetFileNameWithoutExtension(sourcePath);
                string destPath = Path.Combine(options.DestinationFolder, baseName + ".zip");

                try
                {
                    if (Directory.Exists(sourcePath))
                    {
                        await Task.Run(() =>
                            ZipFile.CreateFromDirectory(sourcePath, destPath, CompressionLevel.Optimal, includeBaseDirectory: true),
                            cancellationToken).ConfigureAwait(false);
                    }
                    else if (File.Exists(sourcePath))
                    {
                        using var archive = ZipFile.Open(destPath, ZipArchiveMode.Create);
                        archive.CreateEntryFromFile(sourcePath, Path.GetFileName(sourcePath), CompressionLevel.Optimal);
                    }
                    else
                    {
                        errors.Add(new ArchiveError
                        {
                            SourcePath = sourcePath,
                            Message = $"Source path does not exist: {sourcePath}"
                        });
                        progress?.Report((i + 1) * 100 / total);
                        continue;
                    }

                    createdFiles.Add(destPath);
                }
                catch (IOException ex)
                {
                    errors.Add(new ArchiveError
                    {
                        SourcePath = sourcePath,
                        Message = $"Cannot access file: {ex.Message}",
                        Exception = ex
                    });
                }
                catch (UnauthorizedAccessException ex)
                {
                    errors.Add(new ArchiveError
                    {
                        SourcePath = sourcePath,
                        Message = $"Access denied: {ex.Message}",
                        Exception = ex
                    });
                }

                progress?.Report((i + 1) * 100 / total);
            }
        }

        return new ArchiveResult
        {
            Success = errors.Count == 0,
            CreatedFiles = createdFiles,
            Errors = errors
        };
    }

    /// <inheritdoc/>
    public async Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ArchiveError>();
        var createdFiles = new List<string>();

        Directory.CreateDirectory(options.DestinationFolder);

        int total = options.ArchivePaths.Count;
        for (int i = 0; i < total; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            string archivePath = options.ArchivePaths[i];

            if (!IsZipFile(archivePath))
            {
                progress?.Report((i + 1) * 100 / total);
                continue;
            }

            string destDir = options.Mode == ExtractMode.SeparateFolders
                ? Path.Combine(options.DestinationFolder, Path.GetFileNameWithoutExtension(archivePath))
                : options.DestinationFolder;

            try
            {
                bool alreadyIsolated = options.Mode == ExtractMode.SeparateFolders;
                string actualDest = await Task.Run(() =>
                    ExtractWithSmartFoldering(archivePath, destDir, alreadyIsolated),
                    cancellationToken).ConfigureAwait(false);

                createdFiles.Add(actualDest);
            }
            catch (IOException ex)
            {
                errors.Add(new ArchiveError
                {
                    SourcePath = archivePath,
                    Message = $"Cannot extract archive: {ex.Message}",
                    Exception = ex
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                errors.Add(new ArchiveError
                {
                    SourcePath = archivePath,
                    Message = $"Access denied extracting archive: {ex.Message}",
                    Exception = ex
                });
            }
            catch (InvalidDataException ex)
            {
                errors.Add(new ArchiveError
                {
                    SourcePath = archivePath,
                    Message = "File has ZIP signature but appears corrupted or incomplete.",
                    Exception = ex
                });
            }

            progress?.Report((i + 1) * 100 / total);
        }

        return new ArchiveResult
        {
            Success = errors.Count == 0,
            CreatedFiles = createdFiles,
            Errors = errors
        };
    }

    private static string ExtractWithSmartFoldering(string archivePath, string destDir, bool alreadyIsolated = false)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        var fileEntries = archive.Entries
            .Where(e => !e.FullName.EndsWith('/'))
            .ToList();

        if (fileEntries.Count == 0)
        {
            Directory.CreateDirectory(destDir);
            return destDir;
        }

        bool isSingleRootFolder = fileEntries.All(e => e.FullName.Contains('/'))
            && fileEntries
                .Select(e => e.FullName[..e.FullName.IndexOf('/')])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 1;

        bool isSingleRootFile = fileEntries.Count == 1 && !fileEntries[0].FullName.Contains('/');

        string actualDest = (isSingleRootFolder || isSingleRootFile || alreadyIsolated)
            ? destDir
            : Path.Combine(destDir, Path.GetFileNameWithoutExtension(archivePath));

        Directory.CreateDirectory(actualDest);
        string fullActualDest = Path.GetFullPath(actualDest).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        foreach (var entry in fileEntries)
        {
            string relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);

            if (isSingleRootFolder)
            {
                var sep = relativePath.IndexOf(Path.DirectorySeparatorChar);
                relativePath = relativePath[(sep + 1)..];
                if (string.IsNullOrEmpty(relativePath)) continue;
            }

            string destFilePath = Path.GetFullPath(Path.Combine(actualDest, relativePath));

            if (!destFilePath.StartsWith(fullActualDest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"ZIP entry '{entry.FullName}' would extract outside destination directory.");

            Directory.CreateDirectory(Path.GetDirectoryName(destFilePath)!);
            entry.ExtractToFile(destFilePath, overwrite: true);
        }

        return actualDest;
    }

    private static bool IsZipFile(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[4];
            using var fs = File.OpenRead(path);
            fs.ReadExactly(header);
            return header[0] == 0x50 && header[1] == 0x4B
                && header[2] == 0x03 && header[3] == 0x04;
        }
        catch
        {
            return false;
        }
    }

    private static void AddDirectoryToArchive(ZipArchive archive, string sourceDir, string entryPrefix)
    {
        foreach (string filePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(Path.GetDirectoryName(sourceDir)!, filePath);
            string entryName = relativePath.Replace('\\', '/');
            archive.CreateEntryFromFile(filePath, entryName, CompressionLevel.Optimal);
        }
    }
}
