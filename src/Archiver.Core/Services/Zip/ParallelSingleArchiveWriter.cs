using System.IO.Compression;
using System.Threading.Channels;
using Archiver.Core.Models;

namespace Archiver.Core.Services.Zip;

/// <summary>
/// T-F35: the gated, parallel-compress/single-threaded-write path for <c>SingleArchive</c> mode
/// (see <c>ZipArchiveService.ArchiveAsync</c>'s gate and DECISIONS.md's T-F35 entry). Small
/// files are compressed in memory on background <see cref="Task.Run(Action)"/> workers; large
/// files stream sequentially through <see cref="ZipEntryWriter"/> exactly like today's
/// <c>AddEntryFromFileAsync</c>, never buffered. A single writer thread drains results strictly
/// in enqueue order (not completion order), so T-F31/T-F32 determinism holds by construction and
/// no <see cref="Interlocked"/> progress bookkeeping is needed (only one thread ever reports).
/// </summary>
internal static class ParallelSingleArchiveWriter
{
    // Files at or below this size are eligible for the parallel in-memory compression path.
    // Anything larger always streams sequentially (WriteStreamedEntryAsync) — this is the memory-
    // safety boundary, not a performance-transition boundary: see DECISIONS.md's T-F35 entry for
    // why 4 MiB specifically (a provable worst-case ceiling, not a "where parallelism stops
    // helping" measurement).
    public const long ParallelEligibleByteThreshold = 4L * 1024 * 1024;

    private const int SmallFileReadBufferSize = 65536;

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

        await RunPipelineAsync(
                tempPath, items, (item, ct) => CompressEligibleFileAsync(item, compressionLevel, ct),
                ComputeWindowCapacity(), compressionLevel, totalBytes, progress, reportError, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The core dispatch/drain pipeline, decoupled from real file compression so whitebox tests
    /// can inject a controllable <paramref name="compressItem"/> and a small
    /// <paramref name="windowCapacity"/> to prove backpressure actually engages (see
    /// ParallelSingleArchiveWriterTests) instead of only trusting the production numbers.
    /// </summary>
    internal static async Task RunPipelineAsync(
        string tempPath,
        IEnumerable<FileWorkItem> items,
        Func<FileWorkItem, CancellationToken, Task<WorkResult>> compressItem,
        int windowCapacity,
        CompressionLevel compressionLevel,
        long totalBytes,
        IProgress<ProgressReport>? progress,
        Action<ArchiveError> reportError,
        CancellationToken cancellationToken)
    {
        var pipeline = Channel.CreateBounded<Task<WorkResult>>(
            new BoundedChannelOptions(windowCapacity) { SingleReader = true, SingleWriter = true });

        // The channel's bounded capacity alone only throttles how many completed-but-undrained
        // results may sit in the buffer — it does NOT stop the producer from starting the NEXT
        // compress task before the channel has room, since compressItem's returned Task is
        // already running by the time WriteAsync is even called. A real concurrency gate is
        // needed to bound how many files are actively compressing at once (confirmed by a test
        // that caught exactly this: concurrency briefly exceeded windowCapacity before this gate
        // was added).
        using var computeGate = new SemaphoreSlim(windowCapacity, windowCapacity);

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
                    else if (item.FileSize > ParallelEligibleByteThreshold)
                    {
                        resultTask = Task.FromResult(WorkResult.ForLargePassthrough(item.SourcePath, item.EntryName, item.FileSize, item.LastWriteTime));
                    }
                    else
                    {
                        await computeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                        resultTask = RunGatedAsync(item, compressItem, computeGate, cancellationToken);
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

                case WorkResultKind.LargePassthrough:
                    await writer.WriteStreamedEntryAsync(
                            result.SourcePath, result.EntryName, compressionLevel, result.UncompressedLengthHint,
                            result.LastWriteTime, progress, totalBytes, bytesWritten, cancellationToken)
                        .ConfigureAwait(false);
                    bytesWritten += result.UncompressedLengthHint;
                    continue; // WriteStreamedEntryAsync already reports its own progress incrementally

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

    private static async Task<WorkResult> RunGatedAsync(
        FileWorkItem item, Func<FileWorkItem, CancellationToken, Task<WorkResult>> compressItem,
        SemaphoreSlim computeGate, CancellationToken cancellationToken)
    {
        try
        {
            return await compressItem(item, cancellationToken).ConfigureAwait(false);
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
                    FileShare.Read, bufferSize: SmallFileReadBufferSize, useAsync: false);
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
}
