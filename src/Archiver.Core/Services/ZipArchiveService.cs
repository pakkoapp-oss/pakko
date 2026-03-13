using System.Diagnostics;
using System.IO.Compression;
using Archiver.Core.IO;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// ZIP archive service using System.IO.Compression.
/// Never throws to callers — all errors are captured in ArchiveResult.Errors.
/// </summary>
public sealed class ZipArchiveService : IArchiveService
{
    private const int MaxCompressionRatio = 1000;
    private const int CopyBufferSize = 81920;        // 80 KB — CopyToAsync transfer buffer
    private const int FileStreamBufferSize = 262144; // 256 KB — FileStream read buffer (archiving)

    /// <inheritdoc/>
    public async Task<ArchiveResult> ArchiveAsync(
        ArchiveOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ArchiveError>();
        var createdFiles = new List<string>();
        var skippedFiles = new List<SkippedFile>();

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

            string tempPath = destPath + ".tmp";

            long totalSourceBytes = ComputeTotalBytes(options.SourcePaths);
            progress?.Report(new ProgressReport { Percent = 0, BytesTransferred = 0, TotalBytes = totalSourceBytes });

            // T-F31/T-F32: Sort source paths for deterministic archive entry order (ordinal, case-insensitive).
            // This ensures identical inputs always produce identical archives regardless of the order
            // in which the caller supplies paths or the OS enumerates them.
            var sortedSourcePaths = options.SourcePaths
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            try
            {
                await Task.Run(async () =>
                {
                    using var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create);
                    int total = sortedSourcePaths.Count;
                    long byteOffset = 0;
                    for (int i = 0; i < total; i++)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        string sourcePath = sortedSourcePaths[i];

                        // Compute source size for offset tracking (best-effort)
                        long pathSize = 0;
                        if (File.Exists(sourcePath))
                            try { pathSize = new FileInfo(sourcePath).Length; } catch { }
                        else if (Directory.Exists(sourcePath))
                            pathSize = ComputeDirectoryBytes(sourcePath);

                        // T-F23: Skip top-level symlinks and NTFS junctions
                        if (IsReparsePoint(sourcePath))
                        {
                            skippedFiles.Add(new SkippedFile
                            {
                                Path = sourcePath,
                                Reason = "Symbolic links and NTFS junctions are not archived."
                            });
                            byteOffset += pathSize;
                            continue;
                        }

                        try
                        {
                            if (Directory.Exists(sourcePath))
                            {
                                await AddDirectoryToArchiveAsync(archive, sourcePath, Path.GetFileName(sourcePath),
                                    options.CompressionLevel, cancellationToken, skippedFiles, errors, totalSourceBytes, byteOffset, progress);
                            }
                            else if (File.Exists(sourcePath))
                            {
                                await AddEntryFromFileAsync(archive, sourcePath, Path.GetFileName(sourcePath),
                                    options.CompressionLevel, cancellationToken, totalSourceBytes, byteOffset, progress);
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

                        byteOffset += pathSize;
                    }
                }, cancellationToken).ConfigureAwait(false);

                // T-F21: Commit the archive even when per-item errors occurred so that
                // successfully archived files are preserved. Fatal errors (IOException
                // creating the archive itself) are still caught below and delete the temp.
                // T-F60: Only commit if at least one entry was written. When every source
                // path failed (missing, locked, etc.) the temp is an empty ZIP — discard it
                // so no zero-entry archive and no leftover .tmp lands on disk.
                if (HasTempEntries(tempPath))
                {
                    File.Move(tempPath, destPath, overwrite: true);
                    createdFiles.Add(destPath);
                }
                else
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                }
            }
            catch (OperationCanceledException)
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }
            catch (IOException ex)
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                errors.Add(new ArchiveError
                {
                    SourcePath = destPath,
                    Message = $"Cannot create archive: {ex.Message}",
                    Exception = ex
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                errors.Add(new ArchiveError
                {
                    SourcePath = destPath,
                    Message = $"Access denied creating archive: {ex.Message}",
                    Exception = ex
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                errors.Add(new ArchiveError
                {
                    SourcePath = destPath,
                    Message = $"Unexpected error: {ex.Message}",
                    Exception = ex
                });
            }
        }
        else // SeparateArchives
        {
            Directory.CreateDirectory(options.DestinationFolder);

            long totalSourceBytes = ComputeTotalBytes(options.SourcePaths);
            progress?.Report(new ProgressReport { Percent = 0, BytesTransferred = 0, TotalBytes = totalSourceBytes });
            long byteOffset = 0;

            // T-F31/T-F32: Sort source paths for deterministic archive entry order (ordinal, case-insensitive).
            var sortedSourcePaths = options.SourcePaths
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int total = sortedSourcePaths.Count;
            for (int i = 0; i < total; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                string sourcePath = sortedSourcePaths[i];
                string baseName = Path.GetFileNameWithoutExtension(sourcePath);
                string destPath = Path.Combine(options.DestinationFolder, baseName + ".zip");

                // Compute source size for offset tracking (best-effort)
                long pathSize = 0;
                if (File.Exists(sourcePath))
                    try { pathSize = new FileInfo(sourcePath).Length; } catch { }
                else if (Directory.Exists(sourcePath))
                    pathSize = ComputeDirectoryBytes(sourcePath);

                // T-F23: Skip top-level symlinks and NTFS junctions
                if (IsReparsePoint(sourcePath))
                {
                    skippedFiles.Add(new SkippedFile
                    {
                        Path = sourcePath,
                        Reason = "Symbolic links and NTFS junctions are not archived."
                    });
                    byteOffset += pathSize;
                    continue;
                }

                if (File.Exists(destPath))
                {
                    switch (options.OnConflict)
                    {
                        case ConflictBehavior.Skip:
                            byteOffset += pathSize;
                            continue;
                        case ConflictBehavior.Overwrite:
                            File.Delete(destPath);
                            break;
                        case ConflictBehavior.Rename:
                            destPath = GetUniqueFilePath(destPath);
                            break;
                    }
                }

                string separateTempPath = destPath + ".tmp";
                try
                {
                    if (Directory.Exists(sourcePath))
                    {
                        var level = options.CompressionLevel;
                        long capturedOffset = byteOffset;
                        await Task.Run(async () =>
                        {
                            using var archive = ZipFile.Open(separateTempPath, ZipArchiveMode.Create);
                            await AddDirectoryToArchiveAsync(archive, sourcePath, Path.GetFileName(sourcePath),
                                level, cancellationToken, skippedFiles, errors, totalSourceBytes, capturedOffset, progress);
                        }, cancellationToken).ConfigureAwait(false);
                    }
                    else if (File.Exists(sourcePath))
                    {
                        var level = options.CompressionLevel;
                        long capturedOffset = byteOffset;
                        await Task.Run(async () =>
                        {
                            using var archive = ZipFile.Open(separateTempPath, ZipArchiveMode.Create);
                            await AddEntryFromFileAsync(archive, sourcePath, Path.GetFileName(sourcePath),
                                level, cancellationToken, totalSourceBytes, capturedOffset, progress);
                        }, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        errors.Add(new ArchiveError
                        {
                            SourcePath = sourcePath,
                            Message = $"Source path does not exist: {sourcePath}"
                        });
                        byteOffset += pathSize;
                        continue;
                    }

                    // T-F60: Only commit if at least one entry was written (e.g. a directory
                    // where all contained files failed would otherwise leave an empty archive).
                    if (HasTempEntries(separateTempPath))
                    {
                        File.Move(separateTempPath, destPath, overwrite: true);
                        createdFiles.Add(destPath);
                    }
                    else
                    {
                        try { if (File.Exists(separateTempPath)) File.Delete(separateTempPath); } catch { }
                    }
                }
                catch (OperationCanceledException)
                {
                    try { if (File.Exists(separateTempPath)) File.Delete(separateTempPath); } catch { }
                    throw;
                }
                catch (IOException ex)
                {
                    try { if (File.Exists(separateTempPath)) File.Delete(separateTempPath); } catch { }
                    errors.Add(new ArchiveError
                    {
                        SourcePath = sourcePath,
                        Message = $"Cannot access file: {ex.Message}",
                        Exception = ex
                    });
                }
                catch (UnauthorizedAccessException ex)
                {
                    try { if (File.Exists(separateTempPath)) File.Delete(separateTempPath); } catch { }
                    errors.Add(new ArchiveError
                    {
                        SourcePath = sourcePath,
                        Message = $"Access denied: {ex.Message}",
                        Exception = ex
                    });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    try { if (File.Exists(separateTempPath)) File.Delete(separateTempPath); } catch { }
                    errors.Add(new ArchiveError
                    {
                        SourcePath = sourcePath,
                        Message = $"Unexpected error: {ex.Message}",
                        Exception = ex
                    });
                }

                byteOffset += pathSize;
            }
        }

        var result = new ArchiveResult
        {
            Success = errors.Count == 0,
            CreatedFiles = createdFiles,
            Errors = errors,
            SkippedFiles = skippedFiles,
        };

        if (result.Success && options.OpenDestinationFolder)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", options.DestinationFolder) { UseShellExecute = true }); } catch { }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ArchiveError>();
        var createdFiles = new List<string>();
        var skippedFiles = new List<SkippedFile>();

        Directory.CreateDirectory(options.DestinationFolder);

        int total = options.ArchivePaths.Count;
        bool singleArchive = total == 1;

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
                if (!singleArchive) progress?.Report(new ProgressReport { Percent = (i + 1) * 100 / total, BytesTransferred = 0, TotalBytes = 0 });
                continue;
            }

            if (IsEncryptedZip(archivePath))
            {
                errors.Add(new ArchiveError
                {
                    SourcePath = archivePath,
                    Message = "This archive is password-protected and cannot be extracted."
                });
                if (!singleArchive) progress?.Report(new ProgressReport { Percent = (i + 1) * 100 / total, BytesTransferred = 0, TotalBytes = 0 });
                continue;
            }

            string destDir = options.Mode == ExtractMode.SeparateFolders
                ? Path.Combine(options.DestinationFolder, Path.GetFileNameWithoutExtension(archivePath))
                : options.DestinationFolder;

            try
            {
                IProgress<ProgressReport>? archiveProgress = singleArchive ? progress : null;
                bool alreadyIsolated = options.Mode == ExtractMode.SeparateFolders;
                string actualDest = await Task.Run(async () =>
                    await ExtractWithSmartFolderingAsync(archivePath, destDir, alreadyIsolated,
                        options.OnConflict, skippedFiles, archiveProgress, cancellationToken),
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

            if (!singleArchive) progress?.Report(new ProgressReport { Percent = (i + 1) * 100 / total, BytesTransferred = 0, TotalBytes = 0 });
        }

        var result = new ArchiveResult
        {
            Success = errors.Count == 0,
            CreatedFiles = createdFiles,
            Errors = errors,
            SkippedFiles = skippedFiles,
        };

        if (result.Success && options.OpenDestinationFolder)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", options.DestinationFolder) { UseShellExecute = true }); } catch { }
        }

        return result;
    }

    private static async Task<string> ExtractWithSmartFolderingAsync(
        string archivePath,
        string destDir,
        bool alreadyIsolated,
        ConflictBehavior onConflict,
        List<SkippedFile> skippedFiles,
        IProgress<ProgressReport>? progress,
        CancellationToken cancellationToken)
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

        string tempDest = actualDest + "_tmp";

        Directory.CreateDirectory(tempDest);
        string fullTempDest = Path.GetFullPath(tempDest).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        long totalUncompressedBytes = fileEntries.Where(e => e.Length > 0).Sum(e => e.Length);
        long bytesRead = 0;

        try
        {
            foreach (var entry in fileEntries)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                string relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);

                if (isSingleRootFolder)
                {
                    var sep = relativePath.IndexOf(Path.DirectorySeparatorChar);
                    relativePath = relativePath[(sep + 1)..];
                    if (string.IsNullOrEmpty(relativePath))
                    {
                        bytesRead += entry.Length;
                        continue;
                    }
                }

                // T-F38: Reject entries with Alternate Data Stream marker
                if (EntryHasAlternateDataStream(entry.FullName))
                {
                    skippedFiles.Add(new SkippedFile
                    {
                        Path = entry.FullName,
                        Reason = "Alternate Data Stream entry rejected for security."
                    });
                    bytesRead += entry.Length;
                    continue;
                }

                // T-F39: Reject reserved Windows device names
                if (EntryHasReservedName(entry.FullName))
                {
                    skippedFiles.Add(new SkippedFile
                    {
                        Path = entry.FullName,
                        Reason = $"Entry name matches a reserved Windows device name and was skipped."
                    });
                    bytesRead += entry.Length;
                    continue;
                }

                // T-F39: Reject entries with control characters in name
                if (EntryHasControlCharacters(entry.FullName))
                {
                    skippedFiles.Add(new SkippedFile
                    {
                        Path = entry.FullName,
                        Reason = "Entry name contains control characters and was skipped."
                    });
                    bytesRead += entry.Length;
                    continue;
                }

                string destFilePath = Path.GetFullPath(Path.Combine(tempDest, relativePath));

                if (!destFilePath.StartsWith(fullTempDest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"ZIP entry '{entry.FullName}' would extract outside destination directory.");

                Directory.CreateDirectory(Path.GetDirectoryName(destFilePath)!);

                // T-F37: Reject entries whose path traverses a reparse point (symlink/junction)
                if (PathContainsReparsePoint(destFilePath, fullTempDest))
                {
                    skippedFiles.Add(new SkippedFile
                    {
                        Path = entry.FullName,
                        Reason = "Entry path traverses a reparse point (symlink or junction) and was skipped."
                    });
                    bytesRead += entry.Length;
                    continue;
                }

                // ZIP bomb check — skip entries with suspicious compression ratio
                if (entry.CompressedLength > 0
                    && entry.Length > 0
                    && entry.Length / entry.CompressedLength > MaxCompressionRatio)
                {
                    skippedFiles.Add(new SkippedFile
                    {
                        Path = entry.FullName,
                        Reason = $"Suspicious compression ratio ({entry.Length / entry.CompressedLength}:1). " +
                                 "Entry was skipped as a precaution against ZIP bombs."
                    });
                    bytesRead += entry.Length;
                    continue;
                }

                // Conflict check against the final destination, not the temp dir
                string finalFilePath = Path.GetFullPath(Path.Combine(actualDest, relativePath));
                if (File.Exists(finalFilePath))
                {
                    if (onConflict == ConflictBehavior.Skip)
                    {
                        bytesRead += entry.Length;
                        continue;
                    }
                    if (onConflict == ConflictBehavior.Rename)
                    {
                        string uniqueFinal = GetUniqueFilePath(finalFilePath);
                        destFilePath = Path.Combine(Path.GetDirectoryName(destFilePath)!, Path.GetFileName(uniqueFinal));
                    }
                }

                if (progress != null && totalUncompressedBytes > 0)
                {
                    var entryStream = entry.Open();
                    await using var ps = new ProgressStream(entryStream, totalUncompressedBytes, bytesRead, progress);
                    using var fileStream = new FileStream(
                        destFilePath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: CopyBufferSize,
                        useAsync: true);
                    await ps.CopyToAsync(fileStream, CopyBufferSize, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    using var entryStream = entry.Open();
                    using var fileStream = new FileStream(
                        destFilePath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: CopyBufferSize,
                        useAsync: true);
                    await entryStream.CopyToAsync(fileStream, CopyBufferSize, cancellationToken).ConfigureAwait(false);
                }

                // T-F45: Propagate Zone.Identifier ADS from archive to extracted file
                TryPropagateMotw(archivePath, destFilePath);

                bytesRead += entry.Length;
            }
        }
        catch (OperationCanceledException)
        {
            try { if (Directory.Exists(tempDest)) Directory.Delete(tempDest, recursive: true); } catch { }
            throw;
        }

        // Commit: if final dest doesn't exist, fast-path rename; otherwise merge extracted
        // files in so pre-existing files (e.g. skipped/renamed) are preserved.
        if (!Directory.Exists(actualDest))
        {
            Directory.Move(tempDest, actualDest);
        }
        else
        {
            foreach (string file in Directory.EnumerateFiles(tempDest, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(tempDest, file);
                string finalFile = Path.Combine(actualDest, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(finalFile)!);
                File.Move(file, finalFile, overwrite: true);
            }
            Directory.Delete(tempDest, recursive: true);
        }

        return actualDest;
    }

    private static async Task AddEntryFromFileAsync(
        ZipArchive archive,
        string sourcePath,
        string entryName,
        CompressionLevel compressionLevel,
        CancellationToken cancellationToken,
        long totalBytes = 0,
        long startOffset = 0,
        IProgress<ProgressReport>? progress = null)
    {
        // T-F21: Open the source file BEFORE creating the archive entry.
        // If the file has been deleted or locked since it was discovered by
        // Directory.EnumerateFiles, the IOException propagates to the caller
        // without leaving an orphaned 0-byte entry in the archive.
        using var fileStream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: FileStreamBufferSize,
            useAsync: false);
        var entry = archive.CreateEntry(entryName, compressionLevel);
        // T-F31: Pin LastWriteTime to the source file's actual timestamp so that two
        // archive runs over identical inputs produce byte-identical ZIPs.
        // Without this, ZipArchiveEntry defaults to DateTimeOffset.UtcNow (creation time),
        // which makes the archive non-deterministic.
        try { entry.LastWriteTime = File.GetLastWriteTime(sourcePath); } catch { }

        if (progress != null && totalBytes > 0)
        {
            var entryStream = entry.Open();
            await using var ps = new ProgressStream(entryStream, totalBytes, startOffset, progress);
            await fileStream.CopyToAsync(ps, CopyBufferSize, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            using var entryStream = entry.Open();
            await fileStream.CopyToAsync(entryStream, CopyBufferSize, cancellationToken).ConfigureAwait(false);
        }
    }

    // T-F23: Manual recursive traversal so we can inspect FileAttributes before entering each
    // directory. Returns the updated startOffset for progress tracking.
    // T-F21: errors list receives per-file ArchiveErrors so that a single inaccessible file
    // does not abort the rest of the directory — operation continues for all remaining files.
    private static async Task<long> AddDirectoryToArchiveAsync(
        ZipArchive archive,
        string sourceDir,
        string entryPrefix,
        CompressionLevel compressionLevel,
        CancellationToken cancellationToken,
        List<SkippedFile> skippedFiles,
        List<ArchiveError> errors,
        long totalBytes = 0,
        long startOffset = 0,
        IProgress<ProgressReport>? progress = null)
    {
        // T-F32: Sort files and subdirectories for deterministic traversal order.
        // Directory.EnumerateFiles/EnumerateDirectories return items in filesystem order,
        // which is non-deterministic across runs and filesystems.
        foreach (string filePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // T-F23: Skip file-level symlinks (reparse points)
            if (IsReparsePoint(filePath))
            {
                skippedFiles.Add(new SkippedFile
                {
                    Path = filePath,
                    Reason = "Symbolic links and reparse points are not archived."
                });
                continue;
            }

            long fileSize = 0;
            try { fileSize = new FileInfo(filePath).Length; } catch { }

            string relativePath = Path.GetRelativePath(Path.GetDirectoryName(sourceDir)!, filePath);
            string entryName = relativePath.Replace('\\', '/');

            // T-F21: Catch per-file IO failures. A file may be deleted or locked between
            // Directory.EnumerateFiles discovery and the FileStream.Open inside
            // AddEntryFromFileAsync — both FileNotFoundException and sharing-violation
            // IOException are subclasses of IOException and handled here.
            try
            {
                await AddEntryFromFileAsync(archive, filePath, entryName, compressionLevel, cancellationToken,
                    totalBytes, startOffset, progress);
            }
            catch (IOException ex)
            {
                errors.Add(new ArchiveError
                {
                    SourcePath = filePath,
                    Message = $"Cannot access file: {ex.Message}",
                    Exception = ex
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                errors.Add(new ArchiveError
                {
                    SourcePath = filePath,
                    Message = $"Access denied: {ex.Message}",
                    Exception = ex
                });
            }

            startOffset += fileSize;
        }

        // Recurse into subdirectories, skipping junctions and directory symlinks
        foreach (string subDir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // T-F23: Skip NTFS junctions and directory symlinks — prevents infinite loops
            if (IsReparsePoint(subDir))
            {
                skippedFiles.Add(new SkippedFile
                {
                    Path = subDir,
                    Reason = "NTFS junctions and directory symbolic links are not followed during archiving."
                });
                continue;
            }

            startOffset = await AddDirectoryToArchiveAsync(archive, subDir, entryPrefix, compressionLevel,
                cancellationToken, skippedFiles, errors, totalBytes, startOffset, progress);
        }

        return startOffset;
    }

    private static long ComputeTotalBytes(IReadOnlyList<string> paths)
    {
        long total = 0;
        foreach (var p in paths)
        {
            try
            {
                if (IsReparsePoint(p)) continue;
                if (File.Exists(p)) total += new FileInfo(p).Length;
                else if (Directory.Exists(p)) total += ComputeDirectoryBytes(p);
            }
            catch { }
        }
        return total;
    }

    // T-F23: Safe recursive byte count that skips reparse points — prevents infinite loops
    // on circular directory symlinks and NTFS junctions.
    private static long ComputeDirectoryBytes(string dir)
    {
        long total = 0;
        try
        {
            foreach (string filePath in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
            {
                if (!IsReparsePoint(filePath))
                    try { total += new FileInfo(filePath).Length; } catch { }
            }
            foreach (string subDir in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
            {
                if (!IsReparsePoint(subDir))
                    total += ComputeDirectoryBytes(subDir);
            }
        }
        catch { }
        return total;
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

    // T-F38: Reject entries with ':' in name (Alternate Data Streams)
    private static bool EntryHasAlternateDataStream(string entryFullName)
        => entryFullName.Contains(':');

    // T-F39: Reject reserved Windows device names (with or without extension, case-insensitive)
    private static readonly HashSet<string> _reservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static bool EntryHasReservedName(string entryFullName)
    {
        // Use the last path segment from the raw ZIP entry name (before any GetFullPath call)
        string lastSegment = entryFullName.Contains('/')
            ? entryFullName[(entryFullName.LastIndexOf('/') + 1)..]
            : entryFullName;
        string nameWithoutExt = Path.GetFileNameWithoutExtension(lastSegment);
        return _reservedNames.Contains(nameWithoutExt);
    }

    // T-F39: Reject entries with control characters (0x00–0x1F) in name
    private static bool EntryHasControlCharacters(string entryFullName)
        => entryFullName.Any(c => c < 0x20);

    // T-F23: Returns true when path itself carries the ReparsePoint attribute (symlink or junction).
    // Swallows all exceptions — returns false when attributes cannot be read.
    //
    // Filesystem compatibility:
    //   FAT32/exFAT : always false — these filesystems have no reparse points.
    //   ReFS        : correctly true for symlinks and junctions (same as NTFS).
    //   SMB/UNC     : true when the server propagates FILE_ATTRIBUTE_REPARSE_POINT;
    //                 DFS junctions are followed transparently by the SMB redirector
    //                 and appear as normal directories (false) — not detected here.
    //   Linux/Samba : Linux symlinks are NOT exposed as reparse points to Windows
    //                 clients; they resolve to targets and appear as normal files/dirs.
    //   ISO 9660    : always false — no reparse points on optical media.
    //
    // TODO: Cloud storage stubs (OneDrive cloud-only files) carry FILE_ATTRIBUTE_REPARSE_POINT
    //       and are therefore incorrectly added to SkippedFiles rather than being downloaded
    //       and archived. Fixing this requires reading the reparse tag to distinguish
    //       IO_REPARSE_TAG_CLOUD_* from IO_REPARSE_TAG_SYMLINK / IO_REPARSE_TAG_MOUNT_POINT.
    //       Implement when OneDrive compatibility becomes a requirement.
    private static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            // Cannot read attributes (unreachable network, permission denied, path vanished).
            // Return false — let the subsequent file-open produce an ArchiveError instead.
            return false;
        }
    }

    // T-F37: Check whether any directory component of destFilePath (within rootPath) is a reparse point
    // T-F37: No automated unit test — System.IO.Compression cannot create reparse points in test fixtures.
    private static bool PathContainsReparsePoint(string destFilePath, string rootPath)
    {
        string? current = Path.GetDirectoryName(destFilePath);
        while (current != null && current.Length >= rootPath.Length)
        {
            if (Directory.Exists(current))
            {
                var info = new DirectoryInfo(current);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    return true;
            }
            string? parent = Path.GetDirectoryName(current);
            if (parent == current) break;
            current = parent;
        }
        return false;
    }

    // T-F45: Propagate Zone.Identifier ADS from archive to extracted file.
    // Best-effort — swallows all exceptions. Never fatal.
    // Silently no-ops if the archive has no Zone.Identifier ADS.
    private static void TryPropagateMotw(string archivePath, string destFilePath)
    {
        try
        {
            using var source = new FileStream(
                archivePath + ":Zone.Identifier",
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using var dest = new FileStream(
                destFilePath + ":Zone.Identifier",
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            source.CopyTo(dest);
        }
        catch
        {
            // MOTW propagation is best-effort — never surfaces to caller
        }
    }

    // T-F60: Returns true when the temp ZIP at path contains at least one entry.
    // Used to decide whether to commit or discard after an all-failures archive run.
    private static bool HasTempEntries(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.Entries.Count > 0;
        }
        catch
        {
            return false;
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
