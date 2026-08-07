using System.Collections.Concurrent;
using System.Diagnostics;
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
/// T-F31/T-F32 determinism holds by construction regardless of progress reporting. Progress itself
/// (T-F140 fix) is reported live, from every compression worker as it reads chunks — not only when
/// the single writer thread finishes draining a completed entry — since waiting for whole-file
/// completion left the reported percentage frozen for as long as a single large file took to
/// compress. This needs real <see cref="Interlocked"/> bookkeeping: see <see cref="ProgressTracker"/>.
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

    /// <summary>
    /// T-F140: thread-safe, time-throttled, monotonic byte-progress reporting shared across every
    /// concurrent compression worker. Workers call <see cref="ReportBytes"/> as they read chunks
    /// (or once, for a small in-memory file compressed in a single shot) — this is what makes the
    /// dialog move DURING compression of a large file instead of only once it's fully done. Reports
    /// are throttled to roughly every 100ms (not every chunk) to avoid flooding
    /// <c>IProgressDialog</c>'s COM/UI-thread marshaling under up to 16 concurrent workers, and are
    /// clamped to 99% here — the single writer thread can still be mid-copy on the last large temp
    /// file (or a skipped/errored source can leave a permanent byte-count gap) after every worker
    /// has finished, so the real 100% is only ever reported once by <see cref="ReportComplete"/>
    /// after the whole pipeline actually finishes draining.
    /// </summary>
    internal sealed class ProgressTracker
    {
        private static readonly long ThrottleTicks = Stopwatch.Frequency / 10;

        private readonly IProgress<ProgressReport>? _progress;
        private readonly long _totalBytes;
        private long _processedBytes;
        private int _lastReportedPercent = -1;

        // Backdated by a full throttle window so the very first ReportBytes call is never
        // swallowed just because it happens quickly after the tracker was constructed — an
        // operation dominated by small/fast files could otherwise complete inside the first
        // throttle window and emit no worker-side report at all (only the terminal 100%).
        private long _lastReportTimestamp = Stopwatch.GetTimestamp() - ThrottleTicks;
        private string? _currentFile;

        public ProgressTracker(IProgress<ProgressReport>? progress, long totalBytes)
        {
            _progress = progress;
            _totalBytes = totalBytes;
        }

        // currentFile is whichever entry the calling worker is compressing — with up to 16 workers
        // reporting concurrently, the name shown in a given throttled report is simply whichever one
        // last called this before the throttle window elapsed (a plain, non-Interlocked reference
        // write is fine here: string reference assignment is already atomic, and which of several
        // simultaneously-compressing files' names appears is cosmetic, not a correctness concern —
        // unlike Percent/BytesTransferred, which must be exact and monotonic).
        public void ReportBytes(long delta, string? currentFile = null)
        {
            if (currentFile != null) _currentFile = currentFile;
            if (_progress == null || _totalBytes <= 0 || delta == 0) return;

            long processed = Interlocked.Add(ref _processedBytes, delta);

            long now = Stopwatch.GetTimestamp();
            long lastReport = Volatile.Read(ref _lastReportTimestamp);
            if (now - lastReport < ThrottleTicks) return;
            if (Interlocked.CompareExchange(ref _lastReportTimestamp, now, lastReport) != lastReport) return;

            int percent = (int)Math.Min(99, processed * 100L / _totalBytes);
            int previousPercent = Volatile.Read(ref _lastReportedPercent);
            while (percent > previousPercent)
            {
                if (Interlocked.CompareExchange(ref _lastReportedPercent, percent, previousPercent) == previousPercent)
                {
                    _progress.Report(new ProgressReport
                    {
                        Percent = percent,
                        BytesTransferred = Math.Min(processed, _totalBytes),
                        TotalBytes = _totalBytes,
                        CurrentFile = _currentFile,
                    });
                    break;
                }
                previousPercent = Volatile.Read(ref _lastReportedPercent);
            }
        }
    }

    // The two report-sink callbacks WriteAsync's caller (ZipArchiveService) provides — bundled to
    // cut S107's parameter count on this public entry point.
    public sealed record ReportCallbacks(Action<SkippedFile> ReportSkipped, Action<ArchiveError> ReportError);

    public static async Task WriteAsync(
        string tempPath,
        IReadOnlyList<string> sortedSourcePaths,
        CompressionLevel compressionLevel,
        long totalBytes,
        ReportCallbacks callbacks,
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

        var items = WorkItemEnumerator.Enumerate(sortedSourcePaths, callbacks.ReportSkipped, callbacks.ReportError);

        // A per-operation hidden subfolder next to the destination archive — not loose files
        // scattered in that folder (confusing, per on-device verification), and not the system
        // %TEMP% either (a different, possibly smaller/fuller volume than the destination, which
        // matters now that there's no per-file size ceiling). The GUID suffix keeps two concurrent
        // archive operations targeting the same destination folder from colliding.
        string destinationDir = Path.GetDirectoryName(tempPath) is { Length: > 0 } dir ? dir : ".";
        string chunkDirectory = Path.Combine(destinationDir, $".pakko-tmp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(chunkDirectory);
        File.SetAttributes(chunkDirectory, File.GetAttributes(chunkDirectory) | FileAttributes.Hidden);

        var tracker = new ProgressTracker(progress, totalBytes);

        try
        {
            await RunPipelineAsync(
                    tempPath, items,
                    (item, ct) => CompressEligibleFileAsync(item, compressionLevel, tracker, ct),
                    (item, ct) => CompressToTempFileAsync(item, chunkDirectory, compressionLevel, tracker, ct),
                    ComputeWindowCapacity(), totalBytes, progress, callbacks.ReportError, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            // The inner pipeline already deletes every individual chunk file it created (see
            // RunPipelineAsync's own cleanup) — this just removes the now-empty per-operation
            // folder itself. Best-effort: if something unexpected is still in there, leave it
            // rather than risk deleting content that isn't ours.
            try { Directory.Delete(chunkDirectory); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// The core dispatch/drain pipeline, decoupled from real file compression so whitebox tests
    /// can inject a controllable <paramref name="compressInMemory"/> and a small
    /// <paramref name="windowCapacity"/> to prove backpressure actually engages (see
    /// ParallelSingleArchiveWriterTests) instead of only trusting the production numbers.
    /// </summary>
    // T-F147: complexity/param-count both left as-is deliberately — the switch's two heaviest
    // cases are already extracted (WriteTempFileResultAsync), and every remaining branch here
    // exists because of a specific, previously-debugged race (the dispatchedTasks-await-before-
    // sweep ordering in the outer finally, the computeGate concurrency fix documented inline
    // above) — see CONVENTIONS.md's "Method Complexity & Parameter Count" section for why this
    // project doesn't force a split here. The 9 parameters are the same "decoupled for whitebox
    // testing" design this method's own doc comment already explains, not accidental sprawl.
    internal static async Task RunPipelineAsync( // NOSONAR: S3776, S107 — see comment above
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
        }, cancellationToken);

        try
        {
            await using var writer = new ZipEntryWriter(tempPath);

            await foreach (var task in pipeline.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                WorkResult result = await task.ConfigureAwait(false);

                switch (result.Kind)
                {
                    case WorkResultKind.Compressed:
                        await writer.WriteCompressedEntryAsync(result.EntryName, result.Compressed, result.LastWriteTime, cancellationToken)
                            .ConfigureAwait(false);
                        break;

                    case WorkResultKind.TempFileCompressed:
                        await WriteTempFileResultAsync(writer, result, pendingTempFiles, cancellationToken).ConfigureAwait(false);
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
            }

            await producer.ConfigureAwait(false);

            // Real progress (see ProgressTracker) is reported live by the compression workers
            // themselves and clamped to 99% there — this is the only place that ever reports 100%,
            // once every entry has actually been drained and written, not merely once accumulated
            // compressed bytes reach totalBytes (a skipped/errored source file would otherwise leave
            // a permanent gap and the dialog would never reach 100).
            if (progress != null && totalBytes > 0)
            {
                progress.Report(new ProgressReport { Percent = 100, BytesTransferred = totalBytes, TotalBytes = totalBytes });
            }
        }
        finally
        {
            // Wait for the producer and every dispatched compress task to actually finish before
            // sweeping — otherwise a straggler task still writing its temp file at this exact
            // moment (e.g. the consumer loop exited early on cancellation, before draining
            // everything the producer had already dispatched) could add to pendingTempFiles AFTER
            // the sweep below already ran, leaving a real orphaned temp file on disk.
            try { await producer.ConfigureAwait(false); } catch { /* best-effort */ }
            foreach (var dispatched in dispatchedTasks)
            {
                try { await dispatched.ConfigureAwait(false); } catch { /* best-effort */ }
            }

            // Best-effort sweep: anything still tracked here was produced by a worker but never
            // reached (or was fully processed by) the consumer loop above — e.g. cancellation or
            // an unhandled exception cut the operation short after some temp files were written.
            foreach (var leftoverPath in pendingTempFiles.Keys)
            {
                try { File.Delete(leftoverPath); } catch { /* best-effort */ }
            }
        }
    }

    // T-F141: FileShare.Read, not FileShare.None. This chunk file was already fully written and
    // closed by its worker (the write side's own FileShare.None, in CompressToTempFileAsync
    // below, is what actually prevents anyone from reading a half-written chunk) -- nothing about
    // this read-back needs exclusivity. Requesting it anyway meant a transient external reader
    // (AV real-time scanner, cloud-sync client's file watcher, Search Indexer -- the same class
    // of process T-F96 already documented racing AppPackages) touching a finished chunk in this
    // window threw a sharing-violation IOException here, uncaught, aborting the entire archive
    // operation instead of just this one entry. See DECISIONS.md's T-F141 entry.
    private static async Task WriteTempFileResultAsync(
        ZipEntryWriter writer, WorkResult result, ConcurrentDictionary<string, byte> pendingTempFiles, CancellationToken cancellationToken)
    {
        using (var tempStream = new FileStream(result.TempFilePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: CopyBufferSize, useAsync: false))
        {
            await writer.WriteCompressedEntryFromStreamAsync(
                    result.EntryName, tempStream, result.CompressedSize, result.UncompressedSize,
                    result.Crc32, result.Method, result.LastWriteTime, cancellationToken)
                .ConfigureAwait(false);
        }
        TryDeleteTempFile(result.TempFilePath, pendingTempFiles);
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
        FileWorkItem item, CompressionLevel compressionLevel, ProgressTracker? tracker, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var fileStream = new FileStream(item.SourcePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, bufferSize: FileReadBufferSize, useAsync: false);
                var compressed = ZipEntryCompressor.Compress(fileStream, compressionLevel);
                // In-memory files are small (<= InMemoryCompressByteThreshold) and compressed in one
                // shot, not chunked — a single report on completion is enough; they never cause the
                // "frozen mid-file" symptom the temp-file path's per-chunk reporting below fixes.
                tracker?.ReportBytes(compressed.UncompressedLength, item.EntryName);
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
        ProgressTracker? tracker, CancellationToken cancellationToken) =>
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

                // T-F140: real live progress for this file comes from onBytesRead (per chunk, fed
                // into the shared ProgressTracker) — the progress/totalBytes/startOffset params
                // below stay unused (null/0/0) because they model a single-stream, sequential
                // report shape that doesn't fit several concurrent workers sharing one percentage.
                void OnChunkRead(long delta) => tracker?.ReportBytes(delta, item.EntryName);

                if (method == ZipEntryWriter.StoredMethod)
                {
                    (uncompressedTotal, crc) = await ZipEntryWriter.CopyWithCrcAsync(
                        source, tempOut, buffer, progress: null, totalBytes: 0, startOffset: 0,
                        item.EntryName, cancellationToken, onBytesRead: OnChunkRead).ConfigureAwait(false);
                }
                else
                {
                    var deflate = new DeflateStream(tempOut, compressionLevel, leaveOpen: true);
                    await using (deflate.ConfigureAwait(false))
                    {
                        (uncompressedTotal, crc) = await ZipEntryWriter.CopyWithCrcAsync(
                            source, deflate, buffer, progress: null, totalBytes: 0, startOffset: 0,
                            item.EntryName, cancellationToken, onBytesRead: OnChunkRead).ConfigureAwait(false);
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
        try { File.Delete(path); } catch { /* best-effort */ }
        pendingTempFiles?.TryRemove(path, out _);
    }
}
