using System.Collections.Concurrent;
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
        var conflictResolver = new ConflictResolver(options.OnConflict, options.ResolveConflictAsync);

        if (options.Mode == ArchiveMode.SingleArchive)
        {
            // T-F99: Path.GetFileNameWithoutExtension returns "" for a drive root (e.g. "Z:\"),
            // now a reachable single-source selection via the shell extension's Drive ItemType —
            // falls back to "archive" the same way BuildAddToArchiveTitle already does for the
            // context-menu title text, instead of silently naming the archive ".zip".
            string archiveName = options.ArchiveName ?? (options.SourcePaths.Count == 1
                ? Path.GetFileNameWithoutExtension(options.SourcePaths[0]) is { Length: > 0 } name
                    ? name
                    : "archive"
                : "archive");

            string destPath = Path.Combine(options.DestinationFolder, archiveName + ArchiveNaming.GetExtension(ArchiveContainerFormat.Zip));

            Directory.CreateDirectory(options.DestinationFolder);

            if (File.Exists(destPath))
            {
                switch (await conflictResolver.ResolveAsync(destPath).ConfigureAwait(false))
                {
                    case ConflictBehavior.Skip:
                        // T-F87: report every source as skipped (not just a bare empty result) so
                        // MainViewModel's DeleteAfterOperation cleanup can tell these sources were
                        // never archived and must not be deleted.
                        return new ArchiveResult
                        {
                            Success = true,
                            CreatedFiles = [],
                            Errors = [],
                            SkippedFiles = [.. options.SourcePaths.Select(p => new SkippedFile
                            {
                                Path = p,
                                Reason = $"Archive '{Path.GetFileName(destPath)}' already exists at the destination and was skipped."
                            })],
                        };
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
                    // T-F30: multiple top-level SourcePaths can share a basename (e.g. two
                    // selected files both named "report.txt" from different folders) — track
                    // names already claimed at the archive root and rename later occurrences,
                    // the same way GetUniqueFilePath renames colliding output files on disk.
                    var usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                        if (ArchiveEntrySecurity.IsReparsePoint(sourcePath))
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
                                string entryName = GetUniqueEntryName(usedEntryNames, Path.GetFileName(sourcePath));
                                await AddDirectoryToArchiveAsync(archive, sourcePath, sourcePath, entryName,
                                    options.CompressionLevel, cancellationToken, skippedFiles.Add, errors.Add, totalSourceBytes, byteOffset, progress);
                            }
                            else if (File.Exists(sourcePath))
                            {
                                string entryName = GetUniqueEntryName(usedEntryNames, Path.GetFileName(sourcePath));
                                await AddEntryFromFileAsync(archive, sourcePath, entryName,
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

            // A token already cancelled before this call must produce a graceful empty result,
            // matching the old sequential loop's top-of-iteration IsCancellationRequested check
            // (which simply broke out before doing any work). Parallel.ForEachAsync instead
            // throws immediately if handed an already-cancelled token, so that case is guarded
            // here rather than left to the loop itself.
            if (!cancellationToken.IsCancellationRequested)
            {
                // T-F31/T-F32: Sort source paths for deterministic archive entry order (ordinal, case-insensitive).
                var sortedSourcePaths = options.SourcePaths
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // T-F12: each SourcePath produces a fully independent .zip, so the whole batch can
                // run in parallel. But conflict/collision resolution (OnConflict, and two different
                // SourcePaths sharing a basename) must stay a SEQUENTIAL pre-pass: the original
                // sequential loop relied on File.Exists(destPath) reflecting every prior iteration's
                // completed write, which parallel execution can no longer guarantee (two workers
                // could both observe "doesn't exist yet" and race to write the same .tmp path).
                // Resolving every path's final destination up front — using an in-memory
                // claimedDestPaths set alongside the on-disk check — reproduces the same outcome
                // deterministically before any parallel work starts. See DECISIONS.md's T-F12 entry
                // for the one behavior change this introduces (Overwrite + same-run collision).
                var claimedDestPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var plans = new List<(string SourcePath, string? DestPath)>();

                foreach (string sourcePath in sortedSourcePaths)
                {
                    // T-F23: Skip top-level symlinks and NTFS junctions
                    if (ArchiveEntrySecurity.IsReparsePoint(sourcePath))
                    {
                        skippedFiles.Add(new SkippedFile
                        {
                            Path = sourcePath,
                            Reason = "Symbolic links and NTFS junctions are not archived."
                        });
                        plans.Add((sourcePath, null));
                        continue;
                    }

                    string baseName = Path.GetFileNameWithoutExtension(sourcePath);
                    string destPath = Path.Combine(options.DestinationFolder, baseName + ArchiveNaming.GetExtension(ArchiveContainerFormat.Zip));
                    bool onDiskConflict = File.Exists(destPath);
                    bool sameRunConflict = claimedDestPaths.Contains(destPath);

                    if (onDiskConflict || sameRunConflict)
                    {
                        switch (await conflictResolver.ResolveAsync(destPath).ConfigureAwait(false))
                        {
                            case ConflictBehavior.Skip:
                                // T-F87: record the skip so DeleteAfterOperation cleanup (keyed off
                                // SkippedFiles) doesn't delete a source that was never archived.
                                skippedFiles.Add(new SkippedFile
                                {
                                    Path = sourcePath,
                                    Reason = $"Archive '{Path.GetFileName(destPath)}' already exists at the destination and was skipped."
                                });
                                plans.Add((sourcePath, null));
                                continue;
                            case ConflictBehavior.Overwrite:
                                if (onDiskConflict && !sameRunConflict)
                                    File.Delete(destPath);
                                else
                                    // Two SourcePaths in this same batch share a basename — actually
                                    // overwriting one worker's output from another would race under
                                    // parallel execution, so this same-run collision is renamed
                                    // instead (deliberate, narrow deviation from Overwrite's usual
                                    // "replace" semantics for this edge case only).
                                    destPath = GetUniqueFilePath(destPath, claimedDestPaths);
                                break;
                            case ConflictBehavior.Rename:
                                destPath = GetUniqueFilePath(destPath, claimedDestPaths);
                                break;
                        }
                    }

                    claimedDestPaths.Add(destPath);
                    plans.Add((sourcePath, destPath));
                }

                var concurrentErrors = new ConcurrentBag<ArchiveError>();
                var concurrentCreated = new ConcurrentBag<string>();
                var concurrentSkipped = new ConcurrentBag<SkippedFile>();
                long[] completedBytesBox = [0];

                await Parallel.ForEachAsync(
                    plans.Where(p => p.DestPath is not null),
                    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
                    async (plan, token) => await ArchiveSingleSeparatePathAsync(
                        plan.SourcePath, plan.DestPath!, options.CompressionLevel,
                        concurrentErrors, concurrentCreated, concurrentSkipped,
                        totalSourceBytes, completedBytesBox, progress, token).ConfigureAwait(false)
                ).ConfigureAwait(false);

                foreach (var e in concurrentErrors) errors.Add(e);
                foreach (var c in concurrentCreated) createdFiles.Add(c);
                foreach (var s in concurrentSkipped) skippedFiles.Add(s);

                // Concurrent workers report progress off a shared-but-approximate byte baseline
                // (see ArchiveSingleSeparatePathAsync) — force one final, exact 100% report here so
                // callers always observe a deterministic completion value regardless of how the
                // parallel workers' individual reports interleaved.
                progress?.Report(new ProgressReport { Percent = 100, BytesTransferred = totalSourceBytes, TotalBytes = totalSourceBytes });
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

    // T-F12: archives a single SourcePath (already assigned its final, collision-free destPath
    // by ArchiveAsync's sequential planning pass) into its own independent ZIP. Safe to run
    // concurrently with other calls to this method — each has its own ZipArchive instance and
    // its own destPath/tempPath, so there is no shared archive-writer state.
    //
    // Progress: completedBytesBox[0] is a shared byte counter (Interlocked-updated) across all
    // concurrent workers. Each worker reads a snapshot baseline before it starts and adds its
    // own path's bytes to the shared counter once it finishes — this gives a reasonable,
    // thread-safe approximation of overall progress without any concurrent worker needing to
    // touch another worker's per-entry state. It is not byte-exact when multiple workers are
    // mid-flight at once (their in-progress bytes briefly overlap in the reported total), which
    // is acceptable for a progress bar; ArchiveAsync reports an explicit final 100% after all
    // workers complete so callers always see a deterministic completion value.
    private static async Task ArchiveSingleSeparatePathAsync(
        string sourcePath,
        string destPath,
        CompressionLevel compressionLevel,
        ConcurrentBag<ArchiveError> errors,
        ConcurrentBag<string> createdFiles,
        ConcurrentBag<SkippedFile> skippedFiles,
        long totalSourceBytes,
        long[] completedBytesBox,
        IProgress<ProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        long pathSize = 0;
        if (File.Exists(sourcePath))
            try { pathSize = new FileInfo(sourcePath).Length; } catch { }
        else if (Directory.Exists(sourcePath))
            pathSize = ComputeDirectoryBytes(sourcePath);

        long baseOffset = Interlocked.Read(ref completedBytesBox[0]);
        string separateTempPath = destPath + ".tmp";
        try
        {
            if (Directory.Exists(sourcePath))
            {
                using var archive = ZipFile.Open(separateTempPath, ZipArchiveMode.Create);
                await AddDirectoryToArchiveAsync(archive, sourcePath, sourcePath, Path.GetFileName(sourcePath),
                    compressionLevel, cancellationToken, skippedFiles.Add, errors.Add, totalSourceBytes, baseOffset, progress)
                    .ConfigureAwait(false);
            }
            else if (File.Exists(sourcePath))
            {
                using var archive = ZipFile.Open(separateTempPath, ZipArchiveMode.Create);
                await AddEntryFromFileAsync(archive, sourcePath, Path.GetFileName(sourcePath),
                    compressionLevel, cancellationToken, totalSourceBytes, baseOffset, progress)
                    .ConfigureAwait(false);
            }
            else
            {
                errors.Add(new ArchiveError
                {
                    SourcePath = sourcePath,
                    Message = $"Source path does not exist: {sourcePath}"
                });
                Interlocked.Add(ref completedBytesBox[0], pathSize);
                return;
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
            Interlocked.Add(ref completedBytesBox[0], pathSize);
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

        Interlocked.Add(ref completedBytesBox[0], pathSize);
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
        // T-F06: one instance for the whole call, constructed before the loop below, so an
        // "apply to all" decision on one archive's conflict survives across every subsequent
        // archive in this same ArchivePaths batch, not just the current archive's entries.
        var conflictResolver = new ConflictResolver(options.OnConflict, options.ResolveConflictAsync);

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
                ? Path.Combine(options.DestinationFolder,
                    options.SeparateFolderName ?? ArchiveNaming.GetBaseName(archivePath))
                : options.DestinationFolder;

            try
            {
                IProgress<ProgressReport>? archiveProgress = singleArchive ? progress : null;
                bool alreadyIsolated = options.Mode == ExtractMode.SeparateFolders;
                var (actualDest, anyExtracted) = await Task.Run(async () =>
                    await ExtractWithSmartFolderingAsync(archivePath, destDir, alreadyIsolated,
                        conflictResolver, skippedFiles, archiveProgress,
                        options.ConfirmCompressionBombExtraction, options.SelectedEntryPaths,
                        cancellationToken),
                    cancellationToken).ConfigureAwait(false);

                // T-F87: an archive whose entries were all individually skipped (e.g. every
                // entry already exists at the destination with OnConflict=Skip) must not be
                // reported as CreatedFiles — MainViewModel uses this list to decide whether
                // DeleteAfterOperation may delete the source archive.
                if (anyExtracted)
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

    /// <inheritdoc/>
    public async Task<ArchiveResult> TestAsync(
        IReadOnlyList<string> archivePaths,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ArchiveError>();
        var skippedFiles = new List<SkippedFile>();

        int total = archivePaths.Count;
        for (int i = 0; i < total; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            string archivePath = archivePaths[i];

            if (!IsZipFile(archivePath))
            {
                string? reason = GetKnownArchiveReason(archivePath);
                if (reason is not null)
                    skippedFiles.Add(new SkippedFile { Path = archivePath, Reason = reason });
                progress?.Report(new ProgressReport { Percent = (i + 1) * 100 / total, BytesTransferred = 0, TotalBytes = 0 });
                continue;
            }

            if (IsEncryptedZip(archivePath))
            {
                errors.Add(new ArchiveError
                {
                    SourcePath = archivePath,
                    Message = "This archive is password-protected and cannot be tested."
                });
                progress?.Report(new ProgressReport { Percent = (i + 1) * 100 / total, BytesTransferred = 0, TotalBytes = 0 });
                continue;
            }

            try
            {
                await Task.Run(() => TestArchiveEntries(archivePath, errors, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                errors.Add(new ArchiveError
                {
                    SourcePath = archivePath,
                    Message = $"Cannot read archive: {ex.Message}",
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

            progress?.Report(new ProgressReport { Percent = (i + 1) * 100 / total, BytesTransferred = 0, TotalBytes = 0 });
        }

        return new ArchiveResult
        {
            Success = errors.Count == 0,
            Errors = errors,
            SkippedFiles = skippedFiles,
        };
    }

    /// <inheritdoc/>
    public async Task<ArchiveListResult> ListEntriesAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entries = await Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(archivePath);
                return archive.Entries.Select(e => new ArchiveEntryInfo
                {
                    Path = e.FullName.TrimEnd('/'),
                    Size = e.Length,
                    CompressedSize = e.CompressedLength,
                    Modified = e.LastWriteTime.DateTime,
                    IsDirectory = e.FullName.EndsWith('/'),
                    Crc32 = e.FullName.EndsWith('/') ? null : e.Crc32,
                }).ToList();
            }, cancellationToken).ConfigureAwait(false);

            return new ArchiveListResult { Success = true, Entries = entries };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new ArchiveListResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    // Reads every entry's decompressed bytes and compares a freshly computed CRC-32 against
    // the value declared in the entry's header — System.IO.Compression never validates this
    // itself on read, so a bit-flipped-but-structurally-valid entry would otherwise extract
    // "successfully" with silently wrong content.
    private static void TestArchiveEntries(string archivePath, List<ArchiveError> errors, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        foreach (var entry in archive.Entries)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (entry.FullName.EndsWith('/'))
                continue; // directory entry — no data to verify

            uint computed;
            using (var entryStream = entry.Open())
                computed = Crc32.Compute(entryStream);

            if (computed != entry.Crc32)
            {
                errors.Add(new ArchiveError
                {
                    SourcePath = archivePath,
                    Message = $"Entry '{entry.FullName}' failed CRC-32 check " +
                              $"(expected {entry.Crc32:X8}, got {computed:X8})."
                });
            }
        }
    }

    private static async Task<(string ActualDest, bool AnyExtracted)> ExtractWithSmartFolderingAsync(
        string archivePath,
        string destDir,
        bool alreadyIsolated,
        ConflictResolver conflictResolver,
        List<SkippedFile> skippedFiles,
        IProgress<ProgressReport>? progress,
        Func<CompressionBombWarning, Task<bool>>? confirmCompressionBombExtraction,
        IReadOnlyList<string>? selectedEntryPaths,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        var allFileEntries = archive.Entries
            .Where(e => !e.FullName.EndsWith('/'))
            .ToList();

        // T-F05: restrict to just the selected entries (plus anything nested under a selected
        // folder path) before any of the smart-foldering logic below runs. O(entries × selected)
        // is fine here — selections are UI-driven (a user checking boxes), realistically dozens of
        // rows, not thousands, even against a 65,000-entry archive. The compression-bomb check
        // below deliberately still evaluates allFileEntries (the whole archive), not this subset —
        // see DECISIONS.md's T-F05 entry for why (conservative: may over-warn, never under-warns).
        bool isSelectedSubset = selectedEntryPaths is { Count: > 0 };
        var fileEntries = allFileEntries;
        if (isSelectedSubset)
        {
            var selectedSet = new HashSet<string>(selectedEntryPaths!, StringComparer.Ordinal);
            fileEntries = allFileEntries
                .Where(e => selectedSet.Contains(e.FullName)
                         || selectedSet.Any(s => e.FullName.StartsWith(s + "/", StringComparison.Ordinal)))
                .ToList();
        }

        if (fileEntries.Count == 0)
        {
            Directory.CreateDirectory(destDir);
            return (destDir, true);
        }

        // A selected subset has no single meaningful "root" to collapse — it may span multiple
        // top-level folders/files depending on what the user checked. Skip the whole-archive
        // smart-foldering decision entirely and always extract straight into destDir.
        bool isSingleRootFolder = !isSelectedSubset
            && fileEntries.All(e => e.FullName.Contains('/'))
            && fileEntries
                .Select(e => e.FullName[..e.FullName.IndexOf('/')])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 1;

        bool isSingleRootFile = !isSelectedSubset
            && fileEntries.Count == 1 && !fileEntries[0].FullName.Contains('/');

        string actualDest = (isSingleRootFolder || isSingleRootFile || alreadyIsolated || isSelectedSubset)
            ? destDir
            : Path.Combine(destDir, ArchiveNaming.GetBaseName(archivePath));

        // T-F94: whole-archive compression-ratio check, run BEFORE tempDest is created so a
        // declined/blocked bomb leaves nothing to clean up. Deliberately whole-archive rather
        // than the old per-entry model (see DECISIONS.md's T-F94 entry) — matches
        // TarProcessService's model and allows exactly one confirmation per archive. T-F05: uses
        // allFileEntries (not the filtered subset) even when extracting only a selection — see
        // DECISIONS.md's T-F05 entry for why this stays conservative rather than narrowed.
        long declaredUncompressedSize = allFileEntries.Where(e => e.Length > 0).Sum(e => e.Length);
        long compressedFileSize = new FileInfo(archivePath).Length;
        var bombOutcome = await ArchiveEntrySecurity.EvaluateCompressionBombAsync(
            archivePath, declaredUncompressedSize, compressedFileSize,
            ArchiveEntrySecurity.GetAvailableFreeSpace(destDir),
            confirmCompressionBombExtraction).ConfigureAwait(false);

        if (bombOutcome == CompressionBombOutcome.InsufficientDiskSpace)
        {
            skippedFiles.Add(new SkippedFile
            {
                Path = archivePath,
                Reason = $"Archive declares {declaredUncompressedSize:N0} bytes uncompressed, " +
                         $"but the destination only has {ArchiveEntrySecurity.GetAvailableFreeSpace(destDir):N0} bytes free. " +
                         "Extraction was blocked."
            });
            return (destDir, false);
        }

        if (bombOutcome == CompressionBombOutcome.UserDeclined)
        {
            long ratio = compressedFileSize > 0 ? declaredUncompressedSize / compressedFileSize : 0;
            skippedFiles.Add(new SkippedFile
            {
                Path = archivePath,
                Reason = $"Suspicious compression ratio ({ratio}:1, {declaredUncompressedSize:N0} bytes declared). " +
                         "Extraction was declined as a precaution against ZIP bombs."
            });
            return (destDir, false);
        }

        string tempDest = actualDest + "_tmp";

        Directory.CreateDirectory(tempDest);
        string fullTempDest = Path.GetFullPath(tempDest).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        long totalUncompressedBytes = declaredUncompressedSize;
        long bytesRead = 0;
        int extractedCount = 0;

        // T-F30: ZIP format allows two entries with the identical name, and
        // System.IO.Compression does not reject them on read. The existing conflict check
        // below only looks at whether finalFilePath already exists in the FINAL destination —
        // it never sees an earlier duplicate entry from THIS SAME run, since nothing is
        // committed to the final destination until after the whole loop finishes. Tracking
        // claimed paths in memory closes that gap; without it, a duplicate entry would
        // silently overwrite the first one's file in tempDest.
        var claimedFinalPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                if (ArchiveEntrySecurity.HasAlternateDataStreamMarker(entry.FullName))
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
                if (ArchiveEntrySecurity.HasReservedName(entry.FullName))
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
                if (ArchiveEntrySecurity.HasControlCharacters(entry.FullName))
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
                if (ArchiveEntrySecurity.PathContainsReparsePoint(destFilePath, fullTempDest))
                {
                    skippedFiles.Add(new SkippedFile
                    {
                        Path = entry.FullName,
                        Reason = "Entry path traverses a reparse point (symlink or junction) and was skipped."
                    });
                    bytesRead += entry.Length;
                    continue;
                }

                // T-F94: per-entry compression-ratio check removed — superseded by the
                // whole-archive check above (before tempDest was created).

                // Conflict check against the final destination, not the temp dir — plus
                // T-F30: against every finalFilePath already claimed earlier in this same run,
                // which catches a duplicate entry name inside this archive that File.Exists
                // alone can't see (see claimedFinalPaths comment above).
                string finalFilePath = Path.GetFullPath(Path.Combine(actualDest, relativePath));
                if (File.Exists(finalFilePath) || claimedFinalPaths.Contains(finalFilePath))
                {
                    ConflictBehavior resolvedConflict = await conflictResolver.ResolveAsync(finalFilePath).ConfigureAwait(false);
                    if (resolvedConflict == ConflictBehavior.Skip)
                    {
                        bytesRead += entry.Length;
                        continue;
                    }
                    if (resolvedConflict == ConflictBehavior.Rename)
                    {
                        string uniqueFinal = GetUniqueFilePath(finalFilePath, claimedFinalPaths);
                        destFilePath = Path.Combine(Path.GetDirectoryName(destFilePath)!, Path.GetFileName(uniqueFinal));
                        finalFilePath = uniqueFinal;
                    }
                }
                claimedFinalPaths.Add(finalFilePath);

                if (progress != null && totalUncompressedBytes > 0)
                {
                    var entryStream = entry.Open();
                    await using var ps = new ProgressStream(entryStream, totalUncompressedBytes, bytesRead, progress, entry.Name);
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
                ArchiveEntrySecurity.TryPropagateMotw(archivePath, destFilePath);

                extractedCount++;
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

        // T-F87: every entry was individually skipped (conflict/ADS/reserved name/reparse point/
        // zip bomb) — nothing was actually extracted, so the caller must not count this archive
        // as CreatedFiles (that list gates whether DeleteAfterOperation may delete the source).
        if (extractedCount == 0)
        {
            skippedFiles.Add(new SkippedFile
            {
                Path = archivePath,
                Reason = "No entries were extracted from this archive — every entry was skipped."
            });
            return (actualDest, false);
        }

        return (actualDest, true);
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
            await using var ps = new ProgressStream(entryStream, totalBytes, startOffset, progress, entryName);
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
    // T-F75: rootDir is the original top-level directory being archived and stays FIXED across
    // every recursion level — relative paths (and therefore ZIP entry names) are always computed
    // against it. Before this fix, relative paths were computed against each recursion level's
    // own immediate parent, so every level below the first lost its accumulated prefix (e.g.
    // "notes/sub/file.txt" became just "sub/file.txt" — silently wrong, and deep enough nesting
    // could collide two distinct source files into the same entry name). See DECISIONS.md.
    private static async Task<long> AddDirectoryToArchiveAsync(
        ZipArchive archive,
        string sourceDir,
        string rootDir,
        string entryPrefix,
        CompressionLevel compressionLevel,
        CancellationToken cancellationToken,
        Action<SkippedFile> reportSkipped,
        Action<ArchiveError> reportError,
        long totalBytes = 0,
        long startOffset = 0,
        IProgress<ProgressReport>? progress = null)
    {
        // T-F66: A directory with no files and no subdirectories writes no entry at all
        // otherwise — for a top-level empty folder, that leaves HasTempEntries() false and
        // the whole archive gets silently discarded (ArchiveAsync's "no entries" cleanup),
        // so archiving an empty folder produced no output file. Writing an explicit
        // directory entry preserves the folder and keeps the archive from being discarded.
        if (!Directory.EnumerateFileSystemEntries(sourceDir).Any())
        {
            string relativeDir = Path.GetRelativePath(rootDir, sourceDir);
            string emptyEntryName = relativeDir == "."
                ? entryPrefix + "/"
                : entryPrefix + "/" + relativeDir.Replace('\\', '/') + "/";
            archive.CreateEntry(emptyEntryName);
            return startOffset;
        }

        // T-F32: Sort files and subdirectories for deterministic traversal order.
        // Directory.EnumerateFiles/EnumerateDirectories return items in filesystem order,
        // which is non-deterministic across runs and filesystems.
        foreach (string filePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // T-F23: Skip file-level symlinks (reparse points)
            if (ArchiveEntrySecurity.IsReparsePoint(filePath))
            {
                reportSkipped(new SkippedFile
                {
                    Path = filePath,
                    Reason = "Symbolic links and reparse points are not archived."
                });
                continue;
            }

            long fileSize = 0;
            try { fileSize = new FileInfo(filePath).Length; } catch { }

            string relativePath = Path.GetRelativePath(rootDir, filePath).Replace('\\', '/');
            string entryName = entryPrefix + "/" + relativePath;

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
                reportError(new ArchiveError
                {
                    SourcePath = filePath,
                    Message = $"Cannot access file: {ex.Message}",
                    Exception = ex
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                reportError(new ArchiveError
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
            if (ArchiveEntrySecurity.IsReparsePoint(subDir))
            {
                reportSkipped(new SkippedFile
                {
                    Path = subDir,
                    Reason = "NTFS junctions and directory symbolic links are not followed during archiving."
                });
                continue;
            }

            startOffset = await AddDirectoryToArchiveAsync(archive, subDir, rootDir, entryPrefix, compressionLevel,
                cancellationToken, reportSkipped, reportError, totalBytes, startOffset, progress);
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
                if (ArchiveEntrySecurity.IsReparsePoint(p)) continue;
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
                if (!ArchiveEntrySecurity.IsReparsePoint(filePath))
                    try { total += new FileInfo(filePath).Length; } catch { }
            }
            foreach (string subDir in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
            {
                if (!ArchiveEntrySecurity.IsReparsePoint(subDir))
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

    // T-F38/T-F39/T-F23/T-F37/T-F45 checks moved to ArchiveEntrySecurity (T-F49) — shared with
    // TarProcessService so validation cannot drift between extractors.

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

    // T-F30: claimedPaths lets a caller also exclude candidates already reserved in-memory this
    // run (e.g. a rename target chosen for an earlier duplicate entry that hasn't been written
    // to the real destination yet) — File.Exists alone can't see those.
    private static string GetUniqueFilePath(string path, HashSet<string>? claimedPaths = null)
    {
        string dir = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        int i = 1;
        string candidate;
        do { candidate = Path.Combine(dir, $"{name} ({i++}){ext}"); }
        while (File.Exists(candidate) || (claimedPaths?.Contains(candidate) ?? false));
        return candidate;
    }

    // T-F30: same "name (1)", "name (2)", ... renaming convention as GetUniqueFilePath, but
    // against an in-memory set of ZIP entry names already claimed at the archive root rather
    // than the filesystem — two top-level SourcePaths sharing a basename would otherwise become
    // two ZIP entries with the identical name (CreateEntry does not reject duplicates).
    private static string GetUniqueEntryName(HashSet<string> usedNames, string proposedName)
    {
        if (usedNames.Add(proposedName))
            return proposedName;

        string name = Path.GetFileNameWithoutExtension(proposedName);
        string ext = Path.GetExtension(proposedName);
        int i = 1;
        string candidate;
        do { candidate = $"{name} ({i++}){ext}"; }
        while (!usedNames.Add(candidate));
        return candidate;
    }

}
