using System.Diagnostics;
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

            Directory.CreateDirectory(options.DestinationFolder);

            if (File.Exists(destPath))
            {
                switch (options.OnConflict)
                {
                    case ConflictBehavior.Skip:
                        return new ArchiveResult { Success = true, CreatedFiles = [], Errors = [] };
                    case ConflictBehavior.Overwrite:
                        File.Delete(destPath);
                        break;
                    case ConflictBehavior.Rename:
                        destPath = GetUniqueFilePath(destPath);
                        break;
                }
            }

            bool isSingleLargeFile = options.SourcePaths.Count == 1
                && File.Exists(options.SourcePaths[0])
                && new FileInfo(options.SourcePaths[0]).Length > 10 * 1024 * 1024;
            if (isSingleLargeFile)
                progress?.Report(-1);

            try
            {
                await Task.Run(async () =>
                {
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
                                await AddDirectoryToArchiveAsync(archive, sourcePath, Path.GetFileName(sourcePath), options.CompressionLevel, cancellationToken);
                            }
                            else if (File.Exists(sourcePath))
                            {
                                await AddEntryFromFileAsync(archive, sourcePath, Path.GetFileName(sourcePath), options.CompressionLevel, cancellationToken);
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

                        if (!isSingleLargeFile)
                            progress?.Report((i + 1) * 100 / total);
                    }
                }, cancellationToken).ConfigureAwait(false);
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(new ArchiveError
                {
                    SourcePath = destPath,
                    Message = $"Unexpected error: {ex.Message}",
                    Exception = ex
                });
            }

            if (errors.Count == 0 && File.Exists(destPath))
            {
                createdFiles.Add(destPath);
            }
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

                if (File.Exists(destPath))
                {
                    switch (options.OnConflict)
                    {
                        case ConflictBehavior.Skip:
                            progress?.Report((i + 1) * 100 / total);
                            continue;
                        case ConflictBehavior.Overwrite:
                            File.Delete(destPath);
                            break;
                        case ConflictBehavior.Rename:
                            destPath = GetUniqueFilePath(destPath);
                            break;
                    }
                }

                try
                {
                    if (Directory.Exists(sourcePath))
                    {
                        var level = options.CompressionLevel;
                        await Task.Run(async () =>
                        {
                            using var archive = ZipFile.Open(destPath, ZipArchiveMode.Create);
                            await AddDirectoryToArchiveAsync(archive, sourcePath, Path.GetFileName(sourcePath), level, cancellationToken);
                        }, cancellationToken).ConfigureAwait(false);
                    }
                    else if (File.Exists(sourcePath))
                    {
                        var level = options.CompressionLevel;
                        await Task.Run(async () =>
                        {
                            using var archive = ZipFile.Open(destPath, ZipArchiveMode.Create);
                            await AddEntryFromFileAsync(archive, sourcePath, Path.GetFileName(sourcePath), level, cancellationToken);
                        }, cancellationToken).ConfigureAwait(false);
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
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add(new ArchiveError
                    {
                        SourcePath = sourcePath,
                        Message = $"Unexpected error: {ex.Message}",
                        Exception = ex
                    });
                }

                progress?.Report((i + 1) * 100 / total);
            }
        }

        var result = new ArchiveResult
        {
            Success = errors.Count == 0,
            CreatedFiles = createdFiles,
            Errors = errors,
        };

        if (result.Success && options.DeleteSourceFiles)
        {
            foreach (var path in options.SourcePaths)
            {
                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                    else if (File.Exists(path)) File.Delete(path);
                }
                catch { }
            }
        }

        if (result.Success && options.OpenDestinationFolder)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", options.DestinationFolder) { UseShellExecute = true }); } catch { }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ArchiveError>();
        var createdFiles = new List<string>();
        var skippedFiles = new List<SkippedFile>();

        Directory.CreateDirectory(options.DestinationFolder);

        bool isSingleLargeArchive = options.ArchivePaths.Count == 1
            && File.Exists(options.ArchivePaths[0])
            && new FileInfo(options.ArchivePaths[0]).Length > 10 * 1024 * 1024;
        if (isSingleLargeArchive)
            progress?.Report(-1);

        int total = options.ArchivePaths.Count;
        for (int i = 0; i < total; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            string archivePath = options.ArchivePaths[i];

            if (!IsZipFile(archivePath))
            {
                string? reason = GetKnownArchiveReason(archivePath);
                if (reason is not null)
                    skippedFiles.Add(new SkippedFile { Path = archivePath, Reason = reason });
                if (!isSingleLargeArchive) progress?.Report((i + 1) * 100 / total);
                continue;
            }

            if (IsEncryptedZip(archivePath))
            {
                errors.Add(new ArchiveError
                {
                    SourcePath = archivePath,
                    Message = "This archive is password-protected and cannot be extracted."
                });
                if (!isSingleLargeArchive) progress?.Report((i + 1) * 100 / total);
                continue;
            }

            string destDir = options.Mode == ExtractMode.SeparateFolders
                ? Path.Combine(options.DestinationFolder, Path.GetFileNameWithoutExtension(archivePath))
                : options.DestinationFolder;

            try
            {
                bool alreadyIsolated = options.Mode == ExtractMode.SeparateFolders;
                string actualDest = await Task.Run(() =>
                    ExtractWithSmartFoldering(archivePath, destDir, alreadyIsolated, options.OnConflict),
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

            if (!isSingleLargeArchive) progress?.Report((i + 1) * 100 / total);
        }

        var result = new ArchiveResult
        {
            Success = errors.Count == 0,
            CreatedFiles = createdFiles,
            Errors = errors,
            SkippedFiles = skippedFiles,
        };

        if (result.Success && options.DeleteArchiveAfterExtraction)
        {
            foreach (var path in options.ArchivePaths)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        if (result.Success && options.OpenDestinationFolder)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", options.DestinationFolder) { UseShellExecute = true }); } catch { }
        }

        return result;
    }

    private static string ExtractWithSmartFoldering(string archivePath, string destDir, bool alreadyIsolated = false, ConflictBehavior onConflict = ConflictBehavior.Skip)
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

            if (File.Exists(destFilePath))
            {
                if (onConflict == ConflictBehavior.Skip) continue;
                if (onConflict == ConflictBehavior.Rename) destFilePath = GetUniqueFilePath(destFilePath);
            }

            entry.ExtractToFile(destFilePath, overwrite: true);
        }

        return actualDest;
    }

    private static async Task AddEntryFromFileAsync(
        ZipArchive archive,
        string sourcePath,
        string entryName,
        CompressionLevel compressionLevel,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, compressionLevel);
        using var entryStream = entry.Open();
        using var fileStream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        await fileStream.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task AddDirectoryToArchiveAsync(
        ZipArchive archive,
        string sourceDir,
        string entryPrefix,
        CompressionLevel compressionLevel,
        CancellationToken cancellationToken)
    {
        foreach (string filePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            string relativePath = Path.GetRelativePath(Path.GetDirectoryName(sourceDir)!, filePath);
            string entryName = relativePath.Replace('\\', '/');
            await AddEntryFromFileAsync(archive, filePath, entryName, compressionLevel, cancellationToken);
        }
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

    // ZIP local file header: [4 sig][2 version][2 general purpose bit flag]
    // Bit 0 of the general purpose bit flag indicates encryption.
    private static bool IsEncryptedZip(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[8];
            using var fs = File.OpenRead(path);
            int read = fs.Read(header);
            if (read < 8) return false;
            // flags are at offset 6 (little-endian); bit 0 = encryption flag
            return (header[6] & 0x01) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetKnownArchiveReason(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[6];
            using var fs = File.OpenRead(path);
            int read = fs.Read(header);

            // GZIP: 1F 8B
            if (read >= 2 && header[0] == 0x1F && header[1] == 0x8B)
                return "GZip format is not supported. Only ZIP-based formats are supported.";

            // BZip2: 42 5A 68
            if (read >= 3 && header[0] == 0x42 && header[1] == 0x5A && header[2] == 0x68)
                return "BZip2 format is not supported. Only ZIP-based formats are supported.";

            // RAR: 52 61 72 21
            if (read >= 4 && header[0] == 0x52 && header[1] == 0x61 && header[2] == 0x72 && header[3] == 0x21)
                return "RAR format is not supported. Only ZIP-based formats are supported.";

            // LZ4: 04 22 4D 18
            if (read >= 4 && header[0] == 0x04 && header[1] == 0x22 && header[2] == 0x4D && header[3] == 0x18)
                return "LZ4 format is not supported. Only ZIP-based formats are supported.";

            // 7-Zip: 37 7A BC AF 27 1C
            if (read >= 6 && header[0] == 0x37 && header[1] == 0x7A && header[2] == 0xBC
                && header[3] == 0xAF && header[4] == 0x27 && header[5] == 0x1C)
                return "7-Zip format is not supported. Only ZIP-based formats are supported.";

            // XZ: FD 37 7A 58 5A 00
            if (read >= 6 && header[0] == 0xFD && header[1] == 0x37 && header[2] == 0x7A
                && header[3] == 0x58 && header[4] == 0x5A && header[5] == 0x00)
                return "XZ format is not supported. Only ZIP-based formats are supported.";

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string GetUniqueFilePath(string path)
    {
        string dir = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        int i = 1;
        string candidate;
        do { candidate = Path.Combine(dir, $"{name} ({i++}){ext}"); }
        while (File.Exists(candidate));
        return candidate;
    }
}
