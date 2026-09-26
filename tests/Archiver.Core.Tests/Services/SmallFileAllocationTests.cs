using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// CA1711: xUnit's CollectionDefinition marker-class convention (see docs/CONVENTIONS.md).
#pragma warning disable CA1711
[CollectionDefinition("ProcessAllocationCount", DisableParallelization = true)]
public sealed class ProcessAllocationCountCollection;
#pragma warning restore CA1711

// T-F271 follow-up: hashing and the sequential archive path used to allocate a fresh 256 KiB
// buffer (large-object heap) per file, even for a 4 KiB file. Measures the whole process's
// allocations, so it runs alone — a DisableParallelization collection runs after every parallel
// one, with nothing else in flight in this process.
[Collection("ProcessAllocationCount")]
public sealed class SmallFileAllocationTests : IDisposable
{
    private const int FileCount = 40;
    private const long PerFileBudget = 64 * 1024;

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData(HashAlgorithmKind.Crc32)]
    [InlineData(HashAlgorithmKind.Sha256)]
    public async Task HashComputeAsync_SmallFiles_DoesNotAllocateLargeBuffersPerFile(HashAlgorithmKind algorithm)
    {
        string[] files = CreateSmallFiles("hash");
        await FileHashService.ComputeAsync(files, algorithm, null, CancellationToken.None); // JIT and pool warm-up

        long before = GC.GetTotalAllocatedBytes(precise: true);
        var result = await FileHashService.ComputeAsync(files, algorithm, null, CancellationToken.None);
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        result.Entries.Should().HaveCount(FileCount).And.OnlyContain(e => e.Error == null);
        allocated.Should().BeLessThan(FileCount * PerFileBudget);
    }

    [Fact]
    public async Task ArchiveAsync_SequentialPathSmallFiles_DoesNotAllocateLargeBuffersPerFile()
    {
        string[] files = CreateSmallFiles("zip");
        var service = new ZipArchiveService();
        await ArchiveAsync(service, files, "warmup"); // JIT and pool warm-up

        long before = GC.GetTotalAllocatedBytes(precise: true);
        var result = await ArchiveAsync(service, files, "measured");
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        result.Success.Should().BeTrue();
        allocated.Should().BeLessThan(FileCount * PerFileBudget);
    }

    private Task<ArchiveResult> ArchiveAsync(ZipArchiveService service, string[] files, string name) =>
        service.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = files,
            DestinationFolder = _temp.Path,
            ArchiveName = name,
        }, null, CancellationToken.None);

    private string[] CreateSmallFiles(string subfolder)
    {
        string dir = Path.Combine(_temp.Path, subfolder);
        Directory.CreateDirectory(dir);
        var rng = new Random(42);
        var files = new string[FileCount];
        for (int i = 0; i < FileCount; i++)
        {
            var data = new byte[4096];
            rng.NextBytes(data.AsSpan(0, 2048));
            files[i] = Path.Combine(dir, $"f{i}.dat");
            File.WriteAllBytes(files[i], data);
        }
        return files;
    }
}
