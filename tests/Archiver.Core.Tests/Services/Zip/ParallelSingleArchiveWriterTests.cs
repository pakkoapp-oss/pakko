using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services.Zip;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Zip;

public sealed class ParallelSingleArchiveWriterTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public ParallelSingleArchiveWriterTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private string TempArchivePath => Path.Combine(_tempDir, Guid.NewGuid() + ".zip");

    [Fact]
    public async Task RunPipelineAsync_WritesEntries_InEnqueueOrderNotCompletionOrder()
    {
        var items = new[]
        {
            new FileWorkItem("a", "a.txt", FileWorkKind.File, 10, DateTime.Now),
            new FileWorkItem("b", "b.txt", FileWorkKind.File, 10, DateTime.Now),
            new FileWorkItem("c", "c.txt", FileWorkKind.File, 10, DateTime.Now),
        };

        // Item "a" (enqueued first) finishes LAST; "c" (enqueued last) finishes fastest —
        // proves write order follows enqueue order, not completion order.
        var writeOrder = new List<string>();
        Func<FileWorkItem, CancellationToken, Task<WorkResult>> compressItem = async (item, ct) =>
        {
            int delayMs = item.EntryName switch { "a.txt" => 60, "b.txt" => 30, _ => 0 };
            await Task.Delay(delayMs, ct);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(item.EntryName);
            using var ms = new MemoryStream(bytes);
            var compressed = ZipEntryCompressor.Compress(ms, CompressionLevel.NoCompression);
            return WorkResult.ForCompressed(item.EntryName, compressed, item.LastWriteTime);
        };

        string archivePath = TempArchivePath;
        await ParallelSingleArchiveWriter.RunPipelineAsync(
            archivePath, items, compressItem, windowCapacity: 3, CompressionLevel.NoCompression,
            totalBytes: 30, progress: null, reportError: _ => { }, CancellationToken.None);

        using var archive = ZipFile.OpenRead(archivePath);
        archive.Entries.Select(e => e.Name).Should().Equal("a.txt", "b.txt", "c.txt");
    }

    [Fact]
    public async Task RunPipelineAsync_NeverStartsMoreThanWindowCapacityItemsConcurrently()
    {
        const int windowCapacity = 2;
        var items = Enumerable.Range(0, 5)
            .Select(i => new FileWorkItem($"f{i}", $"f{i}.txt", FileWorkKind.File, 10, DateTime.Now))
            .ToArray();

        int concurrentlyRunning = 0;
        int maxObservedConcurrency = 0;
        var releaseGate = new SemaphoreSlim(0);
        var lockObj = new object();

        Func<FileWorkItem, CancellationToken, Task<WorkResult>> compressItem = async (item, ct) =>
        {
            lock (lockObj)
            {
                concurrentlyRunning++;
                maxObservedConcurrency = Math.Max(maxObservedConcurrency, concurrentlyRunning);
            }

            await releaseGate.WaitAsync(ct); // held open until the test releases it below

            lock (lockObj) { concurrentlyRunning--; }

            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(item.EntryName);
            using var ms = new MemoryStream(bytes);
            var compressed = ZipEntryCompressor.Compress(ms, CompressionLevel.NoCompression);
            return WorkResult.ForCompressed(item.EntryName, compressed, item.LastWriteTime);
        };

        string archivePath = TempArchivePath;
        var pipelineTask = ParallelSingleArchiveWriter.RunPipelineAsync(
            archivePath, items, compressItem, windowCapacity, CompressionLevel.NoCompression,
            totalBytes: 50, progress: null, reportError: _ => { }, CancellationToken.None);

        // Give the producer time to race ahead as far as it's willing to (it should stop at
        // windowCapacity, never all 5) before releasing anything.
        await WaitUntilAsync(() => Volatile.Read(ref maxObservedConcurrency) >= windowCapacity, TimeSpan.FromSeconds(2));
        await Task.Delay(150); // settle window: prove it does NOT exceed capacity, not just reach it

        maxObservedConcurrency.Should().Be(windowCapacity,
            "the bounded channel should stop the producer from starting more than windowCapacity compress tasks at once");

        releaseGate.Release(items.Length); // let everything finish
        await pipelineTask;

        using var archive = ZipFile.OpenRead(archivePath);
        archive.Entries.Should().HaveCount(items.Length);
    }

    [Fact]
    public async Task WriteAsync_AlreadyCancelledToken_ProducesEmptyArchiveWithoutThrowing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        string archivePath = TempArchivePath;
        var act = async () => await ParallelSingleArchiveWriter.WriteAsync(
            archivePath, sortedSourcePaths: ["C:\\does-not-matter.txt"], CompressionLevel.Optimal,
            totalBytes: 0, reportSkipped: _ => { }, reportError: _ => { }, progress: null, cts.Token);

        await act.Should().NotThrowAsync();

        using var archive = ZipFile.OpenRead(archivePath);
        archive.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task RunPipelineAsync_OneItemErrors_RestStillWrittenAndErrorReported()
    {
        var items = new[]
        {
            new FileWorkItem("a", "a.txt", FileWorkKind.File, 10, DateTime.Now),
            new FileWorkItem("bad", "bad.txt", FileWorkKind.File, 10, DateTime.Now),
            new FileWorkItem("c", "c.txt", FileWorkKind.File, 10, DateTime.Now),
        };

        Func<FileWorkItem, CancellationToken, Task<WorkResult>> compressItem = (item, ct) =>
        {
            if (item.EntryName == "bad.txt")
                return Task.FromResult(WorkResult.ForError(item.SourcePath, "simulated locked file", null));

            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(item.EntryName);
            using var ms = new MemoryStream(bytes);
            var compressed = ZipEntryCompressor.Compress(ms, CompressionLevel.NoCompression);
            return Task.FromResult(WorkResult.ForCompressed(item.EntryName, compressed, item.LastWriteTime));
        };

        var reportedErrors = new List<ArchiveError>();
        string archivePath = TempArchivePath;
        await ParallelSingleArchiveWriter.RunPipelineAsync(
            archivePath, items, compressItem, windowCapacity: 3, CompressionLevel.NoCompression,
            totalBytes: 30, progress: null, reportError: reportedErrors.Add, CancellationToken.None);

        reportedErrors.Should().ContainSingle(e => e.SourcePath == "bad" && e.Message == "simulated locked file");

        using var archive = ZipFile.OpenRead(archivePath);
        archive.Entries.Select(e => e.Name).Should().Equal("a.txt", "c.txt");
    }

    [Fact]
    public async Task RunPipelineAsync_CancelledMidFlight_NoTasksLeftRunningAfterwards()
    {
        var items = Enumerable.Range(0, 10)
            .Select(i => new FileWorkItem($"f{i}", $"f{i}.txt", FileWorkKind.File, 10, DateTime.Now))
            .ToArray();

        int activeCount = 0;
        int maxActiveObserved = 0;
        using var cts = new CancellationTokenSource();

        Func<FileWorkItem, CancellationToken, Task<WorkResult>> compressItem = async (item, ct) =>
        {
            Interlocked.Increment(ref activeCount);
            Interlocked.Exchange(ref maxActiveObserved, Math.Max(maxActiveObserved, activeCount));
            try
            {
                await Task.Delay(50, ct);
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(item.EntryName);
                using var ms = new MemoryStream(bytes);
                var compressed = ZipEntryCompressor.Compress(ms, CompressionLevel.NoCompression);
                return WorkResult.ForCompressed(item.EntryName, compressed, item.LastWriteTime);
            }
            finally
            {
                Interlocked.Decrement(ref activeCount);
            }
        };

        string archivePath = TempArchivePath;
        var pipelineTask = ParallelSingleArchiveWriter.RunPipelineAsync(
            archivePath, items, compressItem, windowCapacity: 3, CompressionLevel.NoCompression,
            totalBytes: 100, progress: null, reportError: _ => { }, cts.Token);

        await Task.Delay(20); // let a few tasks start
        cts.Cancel();

        var act = async () => await pipelineTask;
        await act.Should().ThrowAsync<OperationCanceledException>();

        await WaitUntilAsync(() => Volatile.Read(ref activeCount) == 0, TimeSpan.FromSeconds(2));
        activeCount.Should().Be(0, "no background compress task should still be running after the pipeline call returns");
        maxActiveObserved.Should().BeLessOrEqualTo(3);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }
}
