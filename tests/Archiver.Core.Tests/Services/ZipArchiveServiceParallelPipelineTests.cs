using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F35: exercises <c>ZipArchiveService.ArchiveAsync</c>'s <c>SingleArchive</c> mode with enough
/// files (above <c>ParallelPipelineFileCountThreshold</c>, 64) to route through the new parallel
/// pipeline, through the real public API — not <c>ParallelSingleArchiveWriter</c> directly.
/// <see cref="ZipArchiveServiceArchiveTests"/>'s existing determinism/cancellation/error-isolation
/// tests all use small fixtures that never cross the gate threshold, so none of them would ever
/// catch a regression specific to this new path — these are the tests that do.
/// </summary>
public sealed class ZipArchiveServiceParallelPipelineTests : IDisposable
{
    // Comfortably above ParallelPipelineFileCountThreshold (64) so every test here reliably
    // exercises the parallel pipeline, not the sequential fallback.
    private const int ManyFilesCount = 120;

    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string CreateManyFilesDirectory(string dirName, int fileCount = ManyFilesCount)
    {
        string dir = Path.Combine(_temp.Path, dirName);
        Directory.CreateDirectory(dir);
        var rng = new Random(20260718);
        for (int i = 0; i < fileCount; i++)
        {
            byte[] content = new byte[rng.Next(100, 2000)];
            rng.NextBytes(content);
            File.WriteAllBytes(Path.Combine(dir, $"file{i:D4}.bin"), content);
        }
        return dir;
    }

    [Fact]
    public async Task ArchiveAsync_ManyFiles_SameDirectoryTwice_ProducesByteIdenticalZips()
    {
        string sourceDir = CreateManyFilesDirectory("source");

        var result1 = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir],
            DestinationFolder = _temp.Path,
            ArchiveName = "run1",
            CompressionLevel = CompressionLevel.NoCompression, // removes compression-variance, matches existing T-F31 test convention
        });
        var result2 = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir],
            DestinationFolder = _temp.Path,
            ArchiveName = "run2",
            CompressionLevel = CompressionLevel.NoCompression,
        });

        result1.Success.Should().BeTrue();
        result2.Success.Should().BeTrue();
        File.ReadAllBytes(result1.CreatedFiles[0]).Should().Equal(File.ReadAllBytes(result2.CreatedFiles[0]));
    }

    [Fact]
    public async Task ArchiveAsync_ManyFiles_SameDirectoryTwice_EntryOrderIdentical()
    {
        string sourceDir = CreateManyFilesDirectory("source");

        var result1 = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir], DestinationFolder = _temp.Path, ArchiveName = "order1",
        });
        var result2 = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir], DestinationFolder = _temp.Path, ArchiveName = "order2",
        });

        using var zip1 = ZipFile.OpenRead(result1.CreatedFiles[0]);
        using var zip2 = ZipFile.OpenRead(result2.CreatedFiles[0]);

        var names1 = zip1.Entries.Select(e => e.FullName).ToList();
        var names2 = zip2.Entries.Select(e => e.FullName).ToList();

        names1.Should().Equal(names2);
        names1.Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        names1.Should().HaveCount(ManyFilesCount);
    }

    [Fact]
    public async Task ArchiveAsync_ManyFiles_AlreadyCancelledToken_GracefulNoThrow()
    {
        string sourceDir = CreateManyFilesDirectory("source");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir], DestinationFolder = _temp.Path, ArchiveName = "precancelled",
        }, cancellationToken: cts.Token);

        await act.Should().NotThrowAsync();
        Directory.GetFiles(_temp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_ManyFiles_CancelMidArchive_NoUnhandledException()
    {
        string sourceDir = CreateManyFilesDirectory("source");
        using var cts = new CancellationTokenSource();

        _ = Task.Delay(10).ContinueWith(_ => cts.Cancel());

        ArchiveResult? result = null;
        try
        {
            result = await _sut.ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [sourceDir], DestinationFolder = _temp.Path, ArchiveName = "cancel_mid",
            }, cancellationToken: cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation fires mid-pipeline
        }

        result?.Errors.Should().BeEmpty();
        Directory.GetFiles(_temp.Path, "*.tmp").Should().BeEmpty();

        // No orphaned background compression work should still be running — give any leftover
        // thread-pool work a moment to surface, then confirm the archive (if any) is not corrupt.
        await Task.Delay(100);
        string? archivePath = Directory.GetFiles(_temp.Path, "cancel_mid*.zip").FirstOrDefault();
        if (archivePath != null)
        {
            var act = () => ZipFile.OpenRead(archivePath).Dispose();
            act.Should().NotThrow("a partially-committed archive from a graceful cancellation must still be structurally valid");
        }
    }

    [Fact]
    public async Task ArchiveAsync_ManyFiles_OneFileLockedMidBatch_PerFileErrorRestArchived()
    {
        string sourceDir = CreateManyFilesDirectory("source");
        string lockedFile = Path.Combine(sourceDir, "file0060.bin"); // roughly mid-batch

        ArchiveResult result;
        using (new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = await _sut.ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [sourceDir], DestinationFolder = _temp.Path, ArchiveName = "locked_mid",
            });
        }

        result.Errors.Should().ContainSingle(e => e.SourcePath == lockedFile);
        result.CreatedFiles.Should().HaveCount(1);

        using var zip = ZipFile.OpenRead(result.CreatedFiles[0]);
        zip.Entries.Should().HaveCount(ManyFilesCount - 1);
        zip.Entries.Should().NotContain(e => e.Name == "file0060.bin");
    }

    [Fact]
    public async Task ArchiveAsync_ManyFiles_MixedSmallAndLargeFiles_AllContentRoundTripsCorrectly()
    {
        string sourceDir = CreateManyFilesDirectory("source", fileCount: 70);
        // A few files above the 4 MiB parallel-eligible threshold, interleaved by name so they
        // land in the middle of the sorted sequence, not just at the edges.
        var rng = new Random(1);
        byte[] largeContent1 = new byte[5 * 1024 * 1024];
        byte[] largeContent2 = new byte[6 * 1024 * 1024];
        rng.NextBytes(largeContent1);
        rng.NextBytes(largeContent2);
        File.WriteAllBytes(Path.Combine(sourceDir, "file0030-large.bin"), largeContent1);
        File.WriteAllBytes(Path.Combine(sourceDir, "file0050-large.bin"), largeContent2);

        var result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourceDir], DestinationFolder = _temp.Path, ArchiveName = "hybrid",
        });

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();

        using var zip = ZipFile.OpenRead(result.CreatedFiles[0]);
        zip.Entries.Should().HaveCount(72);

        VerifyEntryContent(zip, "file0030-large.bin", largeContent1);
        VerifyEntryContent(zip, "file0050-large.bin", largeContent2);
    }

    private static void VerifyEntryContent(ZipArchive zip, string entryName, byte[] expected)
    {
        var entry = zip.Entries.Should().ContainSingle(e => e.Name == entryName).Subject;
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.ToArray().Should().Equal(expected);
    }
}
