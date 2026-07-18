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
            archivePath, items, compressItem, NeverCalledTempFileCompressor, windowCapacity: 3,
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
            archivePath, items, compressItem, NeverCalledTempFileCompressor, windowCapacity,
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
            archivePath, items, compressItem, NeverCalledTempFileCompressor, windowCapacity: 3,
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
            archivePath, items, compressItem, NeverCalledTempFileCompressor, windowCapacity: 3,
            totalBytes: 100, progress: null, reportError: _ => { }, cts.Token);

        await Task.Delay(20); // let a few tasks start
        cts.Cancel();

        var act = async () => await pipelineTask;
        await act.Should().ThrowAsync<OperationCanceledException>();

        await WaitUntilAsync(() => Volatile.Read(ref activeCount) == 0, TimeSpan.FromSeconds(2));
        activeCount.Should().Be(0, "no background compress task should still be running after the pipeline call returns");
        maxActiveObserved.Should().BeLessOrEqualTo(3);
    }

    [Fact]
    public async Task CompressToTempFileAsync_DeclaredSizeExceedsFreeSpace_ReturnsErrorWithoutTouchingDisk()
    {
        string sourceFile = Path.Combine(_tempDir, "small.bin");
        File.WriteAllBytes(sourceFile, BuildContent(1024));
        string chunkDir = Path.Combine(_tempDir, "chunks");
        Directory.CreateDirectory(chunkDir);

        // No real disk has this much free space — forces the insufficient-space branch without
        // needing to actually fill a disk. The source file itself is tiny and real.
        var item = new FileWorkItem(sourceFile, "small.bin", FileWorkKind.File, long.MaxValue / 2, DateTime.Now);

        WorkResult result = await ParallelSingleArchiveWriter.CompressToTempFileAsync(
            item, chunkDir, CompressionLevel.Optimal, CancellationToken.None);

        result.Kind.Should().Be(WorkResultKind.Error);
        result.ErrorMessage.Should().Contain("Not enough free disk space");
        Directory.GetFiles(chunkDir).Should().BeEmpty("no temp file should ever be created once the pre-check already fails");
    }

    [Fact]
    public async Task WriteAsync_FilesAboveInMemoryThreshold_UseTempFilePathAndCleanUpAfterSuccess()
    {
        string sourceDir = Path.Combine(_tempDir, "source");
        Directory.CreateDirectory(sourceDir);
        byte[] tinyContent = System.Text.Encoding.UTF8.GetBytes("tiny content");
        byte[] justAboveThresholdContent = BuildContent(1024 * 1024 + 1024); // just over 1 MiB
        byte[] wellAboveThresholdContent = BuildContent(3 * 1024 * 1024); // 3 MiB
        File.WriteAllBytes(Path.Combine(sourceDir, "tiny.bin"), tinyContent);
        File.WriteAllBytes(Path.Combine(sourceDir, "just-above.bin"), justAboveThresholdContent);
        File.WriteAllBytes(Path.Combine(sourceDir, "well-above.bin"), wellAboveThresholdContent);
        long totalBytes = tinyContent.Length + justAboveThresholdContent.Length + wellAboveThresholdContent.Length;

        string archivePath = TempArchivePath;
        await ParallelSingleArchiveWriter.WriteAsync(
            archivePath, [sourceDir], CompressionLevel.Optimal, totalBytes,
            reportSkipped: _ => { }, reportError: _ => { }, progress: null, CancellationToken.None);

        using (var archive = ZipFile.OpenRead(archivePath))
        {
            VerifyEntryContent(archive, "tiny.bin", tinyContent);
            VerifyEntryContent(archive, "just-above.bin", justAboveThresholdContent);
            VerifyEntryContent(archive, "well-above.bin", wellAboveThresholdContent);
        }

        FindChunkDirectories().Should()
            .BeEmpty("the per-operation hidden chunk folder must be removed once every entry is consumed");
    }

    [Fact]
    public async Task WriteAsync_TempFileCompressionLockedFile_ReportsErrorCleansUpNoOrphanAndArchivesRest()
    {
        string sourceDir = Path.Combine(_tempDir, "source");
        Directory.CreateDirectory(sourceDir);
        byte[] okContent = BuildContent(2 * 1024 * 1024);
        string lockedPath = Path.Combine(sourceDir, "locked.bin");
        File.WriteAllBytes(Path.Combine(sourceDir, "ok.bin"), okContent);
        File.WriteAllBytes(lockedPath, BuildContent(2 * 1024 * 1024));

        var reportedErrors = new List<ArchiveError>();
        string archivePath = TempArchivePath;

        using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await ParallelSingleArchiveWriter.WriteAsync(
                archivePath, [sourceDir], CompressionLevel.Optimal, totalBytes: 4 * 1024 * 1024,
                reportSkipped: _ => { }, reportError: reportedErrors.Add, progress: null, CancellationToken.None);
        }

        reportedErrors.Should().ContainSingle(e => e.SourcePath == lockedPath);

        using (var archive = ZipFile.OpenRead(archivePath))
            VerifyEntryContent(archive, "ok.bin", okContent);

        FindChunkDirectories().Should()
            .BeEmpty("a failed temp-file compression must clean up its own partial temp file and folder");
    }

    [Fact]
    public async Task WriteAsync_CancelledMidFlight_NoOrphanedChunkTempFilesRemain()
    {
        string sourceDir = Path.Combine(_tempDir, "source");
        Directory.CreateDirectory(sourceDir);
        for (int i = 0; i < 6; i++)
            File.WriteAllBytes(Path.Combine(sourceDir, $"f{i}.bin"), BuildContent(4 * 1024 * 1024));

        using var cts = new CancellationTokenSource();
        string archivePath = TempArchivePath;

        var task = ParallelSingleArchiveWriter.WriteAsync(
            archivePath, [sourceDir], CompressionLevel.Optimal, totalBytes: 24 * 1024 * 1024,
            reportSkipped: _ => { }, reportError: _ => { }, progress: null, cts.Token);

        await Task.Delay(15); // let some temp-file compression start
        cts.Cancel();

        try { await task; } catch (OperationCanceledException) { }

        await WaitUntilAsync(() => FindChunkDirectories().Length == 0, TimeSpan.FromSeconds(3));
        FindChunkDirectories().Should()
            .BeEmpty("cancellation must not leave an orphaned chunk folder behind");
    }

    [Fact]
    public async Task WriteAsync_WhileRunning_ChunkFolderIsHiddenAndNextToDestination()
    {
        string sourceDir = Path.Combine(_tempDir, "source");
        Directory.CreateDirectory(sourceDir);
        for (int i = 0; i < 8; i++)
            File.WriteAllBytes(Path.Combine(sourceDir, $"f{i}.bin"), BuildContent(8 * 1024 * 1024));

        string archivePath = TempArchivePath;
        var task = ParallelSingleArchiveWriter.WriteAsync(
            archivePath, [sourceDir], CompressionLevel.Optimal, totalBytes: 64 * 1024 * 1024,
            reportSkipped: _ => { }, reportError: _ => { }, progress: null, CancellationToken.None);

        string[] seenDuringRun = [];
        await WaitUntilAsync(() => (seenDuringRun = FindChunkDirectories()).Length > 0, TimeSpan.FromSeconds(2));
        seenDuringRun.Should().ContainSingle("exactly one per-operation chunk folder should exist while archiving runs");
        string chunkDir = seenDuringRun[0];
        Path.GetDirectoryName(chunkDir).Should().Be(_tempDir.TrimEnd(Path.DirectorySeparatorChar),
            "the chunk folder must sit next to the destination archive, not in a shared system location");
        File.GetAttributes(chunkDir).HasFlag(FileAttributes.Hidden).Should().BeTrue(
            "the chunk folder must be hidden so it doesn't visibly flicker in Explorer by default");

        await task;
        FindChunkDirectories().Should().BeEmpty("the folder must be gone once archiving completes");
    }

    // The per-operation hidden chunk folder sits directly inside the destination directory,
    // named ".pakko-tmp-{guid}" (see ParallelSingleArchiveWriter.WriteAsync). Directory.GetDirectories
    // finds it regardless of the Hidden attribute (unlike Explorer's default view).
    private string[] FindChunkDirectories() =>
        Directory.GetDirectories(_tempDir, ".pakko-tmp-*");

    private static byte[] BuildContent(int length)
    {
        var rng = new Random(20260719);
        var bytes = new byte[length];
        rng.NextBytes(bytes);
        return bytes;
    }

    private static void VerifyEntryContent(ZipArchive archive, string entryName, byte[] expected)
    {
        var entry = archive.Entries.Should().ContainSingle(e => e.Name == entryName).Subject;
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.ToArray().Should().Equal(expected);
    }

    // All items in these tests use FileSize=10 (well below InMemoryCompressByteThreshold), so the
    // temp-file compressor should never be invoked — throwing here would fail any test that
    // violated that assumption instead of silently masking it.
    private static readonly Func<FileWorkItem, CancellationToken, Task<WorkResult>> NeverCalledTempFileCompressor =
        (_, _) => throw new InvalidOperationException("Temp-file compressor should not be invoked for small items in these tests.");

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }
}
