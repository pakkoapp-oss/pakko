using System.Collections.Concurrent;
using System.IO.Compression;
using System.Threading.Channels;
using Archiver.Core.Models;

namespace Archiver.Core.Services.Zip;

/// <summary>
/// T-F35: the gated, parallel-compress/single-threaded-write path for <c>SingleArchive</c> mode
/// (see <c>ZipArchiveService.ArchiveAsync</c>'s gate and DECISIONS.md's T-F35 entry). Small
/// files are compressed fully in memory on background workers; everything else is ALSO
/// compressed in parallel, but into a private temp file instead of a `byte[]` (T-F35 follow-up —
/// removes the earlier design's file-size ceiling, since a worker streaming a file into its own
/// temp file uses bounded memory regardless of file size, same as the original single-threaded
/// sequential path did). Chunk temp files live inside a per-operation, uniquely-named, hidden
/// subfolder created next to the destination archive (not scattered loose in that folder, and not
/// routed through the system-wide <c>%TEMP%</c> either) — on-device verification showed loose
/// chunk files visibly appearing/disappearing directly in the user's own destination folder
/// mid-operation, which reads as confusing rather than as an implementation detail; a shared
/// system <c>%TEMP%</c> location was considered and rejected in turn, since it can be on a
/// different, possibly smaller/fuller volume than the destination, and this design no longer has
/// any per-file size ceiling to keep that gap small. Same-volume-as-destination plus a hidden
/// folder gets both properties: natural disk-space locality and invisible-by-default in Explorer.
/// A single writer thread drains results strictly in enqueue order (not completion order), so
/// T-F31/T-F32 determinism holds by construction and no <see cref="Interlocked"/> progress
/// bookkeeping is needed (only one thread ever reports).
/// </summary>
internal static class ParallelSingleArchiveWriter
{
    // Files at or below this size are compressed fully into memory (a `byte[]`); anything larger
    // is ALSO compressed in parallel, but streamed into a private temp file instead — bounding
    // per-worker memory to a fixed copy-buffer size regardless of file size. This is a memory-
    // shape boundary (buffer-in-RAM vs. buffer-on-disk), not a parallelism-eligibility boundary —
    // every non-placeholder file is compressed in parallel now. Lowered from an earlier 4 MiB once
    // the temp-file path removed the reason a size ceiling existed at all — see DECISIONS.md's
    // T-F35 follow-up entry.
    public const long InMemoryCompressByteThreshold = 1L * 1024 * 1024;

    private const int FileReadBufferSize = 65536;
    private const int CopyBufferSize = 81920;

    public static int ComputeWindowCapacity() => Math.Clamp(Environment.ProcessorCount, 2, 16);

    public static async Task WriteAsync(
        string tempPath,
        IReadOnlyList<string> sortedSourcePaths,
        CompressionLevel compressionLevel,
        long totalBytes,
        Action<SkippedFile> reportSkipped,
        Action<ArchiveError> reportError,
        IProgress<ProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        // T-F12 lesson: an already-cancelled token must produce a graceful, empty result (no
        // throw) — Channel/Task.Run both behave differently than a cooperative `for` loop's
        // top-of-iteration check when handed a token that's already cancelled, so this must be
        // guarded before any of that machinery is entered, same as SeparateArchives mode's guard.
        if (cancellationToken.IsCancellationRequested)
        {
            await using var _ = new ZipEntryWriter(tempPath);
            return;
        }

        var items = WorkItemEnumerator.Enumerate(sortedSourcePaths, reportSkipped, reportError);

        // A per-operation hidden subfolder next to the destination archive — not loose files
        // scattered in that folder (confusing, per on-device verification), and not the system
        // %TEMP% either (a different, possibly smaller/fuller volume than the destination, which
        // matters now that there's no per-file size ceiling). The GUID suffix keeps two concurrent
        // archive operations targeting the same destination folder from colliding.
        string destinationDir = Path.GetDirectoryName(tempPath) is { Length: > 0 } dir ? dir : ".";
        string chunkDirectory = Path.Combine(destinationDir, $".pakko-tmp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(chunkDirectory);
        File.SetAttributes(chunkDirectory, File.GetAttributes(chunkDirectory) | FileAttributes.Hidden);

        try
        {
            await RunPipelineAsync(
                    tempPath, items,
                    (item, ct) => CompressEligibleFileAsync(item, compressionLevel, ct),
                    (item, ct) => CompressToTempFileAsync(item, chunkDirectory, compressionLevel, ct),
                    ComputeWindowCapacity(), totalBytes, progress, reportError, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            // The inner pipeline already deletes every individual chunk file it created (see
            // RunPipelineAsync's own cleanup) — this just removes the now-empty per-operation
            // folder itself. Best-effort: if something unexpected is still in there, leave it
            // rather than risk deleting content that isn't ours.
            try { Directory.Delete(chunkDirectory); } catch { }
        }
    }

    /// <summary>
    /// The core dispatch/drain pipeline, decoupled from real file compression so whitebox tests
    /// can inject a controllable <paramref name="compressInMemory"/> and a small
    /// <paramref name="windowCapacity"/> to prove backpressure actually engages (see
    /// ParallelSingleArchiveWriterTests) instead of only trusting the production numbers.
    /// </summary>
    internal static async Task RunPipelineAsync(
        string tempPath,
        IEnumerable<FileWorkItem> items,
        Func<FileWorkItem, CancellationToken, Task<WorkResult>> compressInMemory,
        Func<FileWorkItem, CancellationToken, Task<WorkResult>> compressToTempFile,
        int windowCapacity,
        long totalBytes,
        IProgress<ProgressReport>? progress,
        Action<ArchiveError> reportError,
        CancellationToken cancellationToken)
    {
        var pipeline = Channel.CreateBounded<Task<WorkResult>>(
            new BoundedChannelOptions(windowCapacity) { SingleReader = true, SingleWriter = true });

        // The channel's bounded capacity alone only throttles how many completed-but-undrained
        // results may sit in the buffer — it does NOT stop the producer from starting the NEXT
        // compress task before the channel has room, since a compress task is already running by
        // the time WriteAsync is even called. A real concurrency gate is needed to bound how many
        // files are actively compressing at once (confirmed by a test that caught exactly this:
        // concurrency briefly exceeded windowCapacity before this gate was added).
        using var computeGate = new SemaphoreSlim(windowCapacity, windowCapacity);

        // Temp files created by compressToTempFile workers, tracked so a cancelled/failed run
        // never leaves orphans behind — normal consumption removes an entry once the writer has
        // copied and deleted it; anything still here when the pipeline ends (any reason) is swept
        // in the outer finally below.
        var pendingTempFiles = new ConcurrentDictionary<string, byte>();

        // Every dispatched compress task, tracked so the outer finally can wait for all of them
        // to actually finish before sweeping pendingTempFiles. Without this, a straggler task
        // still writing its temp file at the moment the consumer loop exits (e.g. cancellation)
        // could add to pendingTempFiles AFTER the sweep already ran and returned — a real race
        // caught by a test that failed intermittently under full-suite parallel load, not just
        // in isolation, before this fix.
        var dispatchedTasks = new ConcurrentBag<Task<WorkResult>>();

        var producer = Task.Run(async () =>
        {
            try
            {
                foreach (var item in items)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    Task<WorkResult> resultTask;
                    if (item.Kind == FileWorkKind.DirectoryPlaceholder)
                    {
                        resultTask = Task.FromResult(WorkResult.ForDirectoryPlaceholder(item.EntryName, item.LastWriteTime));
                    }
                    else
                    {
                        await computeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                        bool inMemory = item.FileSize <= InMemoryCompressByteThreshold;
                        var compressor = inMemory ? compressInMemory : compressToTempFile;
                        resultTask = RunGatedAsync(item, compressor, computeGate, pendingTempFiles, cancellationToken);
                        dispatchedTasks.Add(resultTask);
                    }

                    await pipeline.Writer.WriteAsync(resultTask, cancellationToken).ConfigureAwait(false);
                }
                pipeline.Writer.Complete();
            }
            catch (Exception ex)
            {
                pipeline.Writer.Complete(ex);
            }
        });

        try
        {
            await using var writer = new ZipEntryWriter(tempPath);
            long bytesWritten = 0;

            await foreach (var task in pipeline.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                WorkResult result = await task.ConfigureAwait(false);

                switch (result.Kind)
                {
                    case WorkResultKind.Compressed:
                        await writer.WriteCompressedEntryAsync(result.EntryName, result.Compressed, result.LastWriteTime, cancellationToken)
                            .ConfigureAwait(false);
                        bytesWritten += result.Compressed.UncompressedLength;
                        break;

                    case WorkResultKind.TempFileCompressed:
                        using (var tempStream = new FileStream(result.TempFilePath, FileMode.Open, FileAccess.Read,
                            FileShare.None, bufferSize: CopyBufferSize, useAsync: false))
                        {
                            await writer.WriteCompressedEntryFromStreamAsync(
                                    result.EntryName, tempStream, result.CompressedSize, result.UncompressedSize,
                                    result.Crc32, result.Method, result.LastWriteTime, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        TryDeleteTempFile(result.TempFilePath, pendingTempFiles);
                        bytesWritten += result.UncompressedSize;
                        break;

                    case WorkResultKind.DirectoryPlaceholder:
                        await writer.WriteDirectoryPlaceholderAsync(result.EntryName, result.LastWriteTime, cancellationToken)
                            .ConfigureAwait(false);
                        break;

                    case WorkResultKind.Error:
                        reportError(new ArchiveError
                        {
                            SourcePath = result.SourcePath,
                            Message = result.ErrorMessage ?? "Unknown error while archiving.",
                            Exception = result.ErrorException,
                        });
                        break;
                }

                if (progress != null && totalBytes > 0)
                {
                    progress.Report(new ProgressReport
                    {
                        Percent = (int)(bytesWritten * 100L / totalBytes),
                        BytesTransferred = bytesWritten,
                        TotalBytes = totalBytes,
                    });
                }
            }

            await producer.ConfigureAwait(false);
        }
        finally
        {
            // Wait for the producer and every dispatched compress task to actually finish before
            // sweeping — otherwise a straggler task still writing its temp file at this exact
            // moment (e.g. the consumer loop exited early on cancellation, before draining
            // everything the producer had already dispatched) could add to pendingTempFiles AFTER
            // the sweep below already ran, leaving a real orphaned temp file on disk.
            try { await producer.ConfigureAwait(false); } catch { }
            foreach (var dispatched in dispatchedTasks)
            {
                try { await dispatched.ConfigureAwait(false); } catch { }
            }

            // Best-effort sweep: anything still tracked here was produced by a worker but never
            // reached (or was fully processed by) the consumer loop above — e.g. cancellation or
            // an unhandled exception cut the operation short after some temp files were written.
            foreach (var leftoverPath in pendingTempFiles.Keys)
            {
                try { File.Delete(leftoverPath); } catch { }
            }
        }
    }

    private static async Task<WorkResult> RunGatedAsync(
        FileWorkItem item, Func<FileWorkItem, CancellationToken, Task<WorkResult>> compressor,
        SemaphoreSlim computeGate, ConcurrentDictionary<string, byte> pendingTempFiles, CancellationToken cancellationToken)
    {
        try
        {
            var result = await compressor(item, cancellationToken).ConfigureAwait(false);
            if (result.Kind == WorkResultKind.TempFileCompressed)
                pendingTempFiles.TryAdd(result.TempFilePath, 0);
            return result;
        }
        finally
        {
            computeGate.Release();
        }
    }

    private static Task<WorkResult> CompressEligibleFileAsync(
        FileWorkItem item, CompressionLevel compressionLevel, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var fileStream = new FileStream(item.SourcePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, bufferSize: FileReadBufferSize, useAsync: false);
                var compressed = ZipEntryCompressor.Compress(fileStream, compressionLevel);
                return WorkResult.ForCompressed(item.EntryName, compressed, item.LastWriteTime);
            }
            catch (IOException ex)
            {
                return WorkResult.ForError(item.SourcePath, $"Cannot access file: {ex.Message}", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return WorkResult.ForError(item.SourcePath, $"Access denied: {ex.Message}", ex);
            }
        }, cancellationToken);

    // internal (not private) so a test can drive the disk-space pre-check directly with a
    // hand-crafted FileWorkItem (a real small source file, but an artificially huge declared
    // FileSize) — no real disk has enough free space to fail this check "for real" otherwise.
    internal static Task<WorkResult> CompressToTempFileAsync(
        FileWorkItem item, string chunkDirectory, CompressionLevel compressionLevel,
        CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Best-effort guard against the disk filling up mid-batch: a temp chunk file can now
            // be as large as the source file itself (no size ceiling — see this class's own
            // remarks), which is new risk this design introduced (the old direct-streaming design
            // never needed any extra disk space beyond the final archive). Reuses the same
            // GetDiskFreeSpaceExW-based helper the extraction-side compression-bomb check (T-F94)
            // already uses — not airtight against several workers racing the same free-space
            // number down concurrently, but catches the common case (a file plainly too big for
            // what's left) before ever touching disk for it, instead of writing partway and
            // failing with a less clear IOException.
            long availableFreeSpace = ArchiveEntrySecurity.GetAvailableFreeSpace(chunkDirectory);
            if (availableFreeSpace < item.FileSize)
            {
                return WorkResult.ForError(item.SourcePath,
                    $"Not enough free disk space to compress this file: it is {item.FileSize:N0} bytes, " +
                    $"but only {availableFreeSpace:N0} bytes are free.", null);
            }

            string tempFilePath = Path.Combine(chunkDirectory, $"chunk-{Guid.NewGuid():N}.tmp");

            try
            {
                using var source = new FileStream(item.SourcePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, bufferSize: FileReadBufferSize, useAsync: false);
                using var tempOut = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write,
                    FileShare.None, bufferSize: CopyBufferSize, useAsync: false);

                ushort method = ZipEntryWriter.SelectMethod(compressionLevel);
                var buffer = new byte[CopyBufferSize];
                long uncompressedTotal;
                uint crc;

                if (method == ZipEntryWriter.StoredMethod)
                {
                    (uncompressedTotal, crc) = await ZipEntryWriter.CopyWithCrcAsync(
                        source, tempOut, buffer, progress: null, totalBytes: 0, startOffset: 0,
                        item.EntryName, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var deflate = new DeflateStream(tempOut, compressionLevel, leaveOpen: true);
                    await using (deflate.ConfigureAwait(false))
                    {
                        (uncompressedTotal, crc) = await ZipEntryWriter.CopyWithCrcAsync(
                            source, deflate, buffer, progress: null, totalBytes: 0, startOffset: 0,
                            item.EntryName, cancellationToken).ConfigureAwait(false);
                    }
                }

                long compressedSize = tempOut.Length;
                return WorkResult.ForTempFileCompressed(
                    item.EntryName, tempFilePath, crc, compressedSize, uncompressedTotal, method, item.LastWriteTime);
            }
            catch (IOException ex)
            {
                TryDeleteTempFile(tempFilePath, pendingTempFiles: null);
                return WorkResult.ForError(item.SourcePath, $"Cannot access file: {ex.Message}", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                TryDeleteTempFile(tempFilePath, pendingTempFiles: null);
                return WorkResult.ForError(item.SourcePath, $"Access denied: {ex.Message}", ex);
            }
            catch
            {
                // Cancellation or an unexpected failure — clean up the partial temp file (it was
                // never handed to pendingTempFiles since this method never returned normally) and
                // let the exception propagate, matching the in-memory path's behavior.
                TryDeleteTempFile(tempFilePath, pendingTempFiles: null);
                throw;
            }
        }, cancellationToken);

    private static void TryDeleteTempFile(string path, ConcurrentDictionary<string, byte>? pendingTempFiles)
    {
        try { File.Delete(path); } catch { }
        pendingTempFiles?.TryRemove(path, out _);
    }
}
