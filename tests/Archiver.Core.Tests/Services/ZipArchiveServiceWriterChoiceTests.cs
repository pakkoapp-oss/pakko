using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Zip;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F352: which writer a <c>SingleArchive</c> run takes, and that a few large files behave on the
/// parallel writer as they did on the sequential one. Large files here hold random bytes: the
/// parallel writer stores an entry Deflate did not shrink (T-F299) and <c>ZipArchive</c> does not,
/// so a stored entry tells which writer ran.
/// </summary>
public sealed class ZipArchiveServiceWriterChoiceTests : IDisposable
{
    private const long MiB = 1024 * 1024;
    private const int LargeFileBytes = 9 * 1024 * 1024;

    private static readonly ParallelSingleArchiveWriter.CompressionSettings Optimal = new(CompressionLevel.Optimal);

    // The disk answer is pinned: these tests are about the rule, not about the machine's disk.
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions()) { HasNoSeekPenalty = _ => true };
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData(1, 300, 300, false)]   // one file: nothing to run beside it
    [InlineData(2, 301, 300, false)]   // a large file and a small one
    [InlineData(2, 116, 100, false)]   // 16 MiB beside 100: under a quarter of the largest
    [InlineData(2, 14, 7, false)]      // 7 MiB beside the largest: under the floor
    [InlineData(2, 16, 8, true)]       // 8 MiB beside the largest
    [InlineData(3, 300, 100, true)]
    [InlineData(2, 125, 100, true)]    // exactly a quarter
    [InlineData(64, 640, 10, true)]
    public void UsesParallelWriter_FewFiles_DependsOnTheBytesBesideTheLargest(
        int fileCount, long totalMiB, long largestMiB, bool expected)
    {
        ZipArchiveService.UsesParallelWriter(Optimal, fileCount, totalMiB * MiB, largestMiB * MiB, freeBytes: long.MaxValue, () => true)
            .Should().Be(expected);
    }

    [Fact]
    public void UsesParallelWriter_NotEnoughRoomForTheChunkFiles_StaysSequential()
    {
        ZipArchiveService.UsesParallelWriter(Optimal, 3, 300 * MiB, 100 * MiB, freeBytes: 600 * MiB, () => true).Should().BeTrue();
        ZipArchiveService.UsesParallelWriter(Optimal, 3, 300 * MiB, 100 * MiB, freeBytes: 600 * MiB - 1, () => true).Should().BeFalse();
    }

    [Fact]
    public void UsesParallelWriter_DiskWithASeekPenaltyOrUnknown_StaysSequential()
    {
        ZipArchiveService.UsesParallelWriter(Optimal, 3, 300 * MiB, 100 * MiB, long.MaxValue, () => false).Should().BeFalse();
    }

    [Fact]
    public void UsesParallelWriter_NoCompression_StaysSequential()
    {
        // Nothing to compute in parallel; the chunk files would only add a second copy.
        ZipArchiveService.UsesParallelWriter(new(CompressionLevel.NoCompression), 3, 300 * MiB, 100 * MiB, long.MaxValue, () => true)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(65, CompressionLevel.Optimal, null)]
    [InlineData(65, CompressionLevel.NoCompression, null)]
    [InlineData(1, CompressionLevel.Fastest, null)]
    [InlineData(1, CompressionLevel.Optimal, "secret")]
    public void UsesParallelWriter_ManyFilesPasswordOrFastest_AlwaysParallel(int fileCount, CompressionLevel level, string? password)
    {
        // What shipped before T-F352 does not depend on the disk, and does not ask about it.
        ZipArchiveService.UsesParallelWriter(new(level, password), fileCount, 1024, 1024, freeBytes: 0,
            () => throw new InvalidOperationException("the disk must not be asked")).Should().BeTrue();
    }

    [Theory]
    [InlineData("out")]    // the destination
    [InlineData("large")]  // the sources
    public async Task ArchiveAsync_TwoLargeFilesOnADiskWithASeekPenalty_StaysOnTheSequentialWriter(string slowFolder)
    {
        string[] sources = [CreateRandomFile("a.bin", 1), CreateRandomFile("b.bin", 2)];
        string slowRoot = Path.Combine(_temp.Path, slowFolder);
        var sut = new ZipArchiveService(new GroupPolicyOptions())
        {
            HasNoSeekPenalty = path => !path.StartsWith(slowRoot, StringComparison.OrdinalIgnoreCase),
        };

        ArchiveResult result = await sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = sources, DestinationFolder = Path.Combine(_temp.Path, "out"), ArchiveName = "slow",
        });

        using ZipArchive zip = ZipFile.OpenRead(result.CreatedFiles.Single());
        zip.Entries.Should().HaveCount(2).And.OnlyContain(e => e.CompressedLength > e.Length, "ZipArchive wrote them");
    }

    [Fact]
    public async Task ArchiveAsync_TwoLargeFiles_TakesTheParallelWriterAndRoundTrips()
    {
        string[] sources = [CreateRandomFile("a.bin", 1), CreateRandomFile("b.bin", 2)];

        ArchiveResult result = await ArchiveAsync(sources, "two");

        result.Errors.Should().BeEmpty();
        using ZipArchive zip = ZipFile.OpenRead(result.CreatedFiles.Single());
        zip.Entries.Select(e => e.FullName).Should().Equal("a.bin", "b.bin");
        zip.Entries.Should().OnlyContain(e => e.CompressedLength == e.Length, "the parallel writer stores what Deflate did not shrink");
        foreach (string source in sources)
            ReadEntry(zip, Path.GetFileName(source)).Should().Equal(File.ReadAllBytes(source));
        Directory.GetDirectories(_temp.Path, ".pakko-tmp-*").Should().BeEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_OneLargeFileAndASmallOne_StaysOnTheSequentialWriter()
    {
        string[] sources = [CreateRandomFile("a.bin", 1), _temp.CreateFile("note.txt")];

        ArchiveResult result = await ArchiveAsync(sources, "one");

        using ZipArchive zip = ZipFile.OpenRead(result.CreatedFiles.Single());
        ZipArchiveEntry large = zip.GetEntry("a.bin")!;
        large.CompressedLength.Should().BeGreaterThan(large.Length, "ZipArchive keeps the Deflate stream even when it grew");
    }

    [Fact]
    public async Task ArchiveAsync_FolderWithOneLargeFileAndASmallOne_StaysOnTheSequentialWriter()
    {
        string folder = Path.GetDirectoryName(CreateRandomFile("a.bin", 1))!;
        CreateFileIn("large", "note.txt", [1]);

        ArchiveResult result = await ArchiveAsync([folder], "folder");

        using ZipArchive zip = ZipFile.OpenRead(result.CreatedFiles.Single());
        ZipArchiveEntry large = zip.GetEntry("large/a.bin")!;
        large.CompressedLength.Should().BeGreaterThan(large.Length);
    }

    [Fact]
    public async Task ArchiveAsync_LargeFilesWithTheSameName_AreNamedAsTheSequentialWriterNamesThem()
    {
        string[] small = [CreateFileIn("x", "report.dat", [1]), CreateFileIn("y", "report.dat", [2])];
        string[] large = [CreateRandomFile("report.dat", 1, "p"), CreateRandomFile("report.dat", 2, "q")];

        ArchiveResult sequential = await ArchiveAsync(small, "small");
        ArchiveResult parallel = await ArchiveAsync(large, "large");

        EntryNames(parallel).Should().Equal(EntryNames(sequential));
        EntryNames(parallel).Should().HaveCount(2).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ArchiveAsync_LockedFileAmongLargeFiles_ReportsItAsTheSequentialWriterDoes()
    {
        string[] small = [CreateFileIn("s", "a.bin", [1]), CreateFileIn("s", "b.bin", [2]), CreateFileIn("s", "c.bin", [3])];
        string[] large = [CreateRandomFile("a.bin", 1), CreateRandomFile("b.bin", 2), CreateRandomFile("c.bin", 3)];

        ArchiveResult sequential = await ArchiveWithLockedAsync(small, small[1], "small");
        ArchiveResult parallel = await ArchiveWithLockedAsync(large, large[1], "large");

        ArchiveError error = parallel.Errors.Should().ContainSingle().Subject;
        error.SourcePath.Should().Be(large[1]);
        error.Text!.Code.Should().Be(sequential.Errors.Single().Text!.Code);
        EntryNames(parallel).Should().Equal("a.bin", "c.bin");
    }

    [Fact]
    public async Task ArchiveAsync_LinkAmongLargeFiles_IsSkippedAsTheSequentialWriterSkipsIt()
    {
        string[] large = [CreateRandomFile("a.bin", 1), CreateRandomFile("b.bin", 2)];
        string smallTarget = _temp.CreateFile("t.txt");
        string link = Path.Combine(_temp.Path, "link.bin");
        try
        {
            File.CreateSymbolicLink(link, large[0]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // symbolic links are not available on this machine
        }

        ArchiveResult sequential = await ArchiveAsync([smallTarget, link], "small");
        ArchiveResult parallel = await ArchiveAsync([.. large, link], "large");

        SkippedFile skipped = parallel.SkippedFiles.Should().ContainSingle().Subject;
        skipped.Path.Should().Be(link);
        skipped.Reason.Should().Be(sequential.SkippedFiles.Single().Reason);
        EntryNames(parallel).Should().Equal("a.bin", "b.bin");
    }

    [Fact]
    public async Task ArchiveAsync_LargeFilesCancelledMidway_LeavesNoArchiveAndNoChunkFolder()
    {
        string[] sources = [CreateRandomFile("a.bin", 1), CreateRandomFile("b.bin", 2), CreateRandomFile("c.bin", 3)];
        string destination = Path.Combine(_temp.Path, "out");
        using var cts = new CancellationTokenSource();
        var progress = new SynchronousProgress(report => { if (report.Percent > 0) cts.Cancel(); });

        Func<Task> act = () => _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = sources, DestinationFolder = destination, ArchiveName = "cancelled",
        }, progress, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.EnumerateFileSystemEntries(destination).Should().BeEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_LargeFiles_ProgressNeverGoesBackAndEndsAtOneHundred()
    {
        string[] sources = [CreateRandomFile("a.bin", 1), CreateRandomFile("b.bin", 2)];
        var percents = new List<int>();
        var progress = new SynchronousProgress(report => { lock (percents) percents.Add(report.Percent); });

        await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = sources, DestinationFolder = Path.Combine(_temp.Path, "out"), ArchiveName = "progress",
        }, progress);

        percents.Should().BeInAscendingOrder();
        percents[^1].Should().Be(100);
        percents.Should().Contain(p => p > 0 && p < 100, "the bar moves while the files are compressed");
    }

    // T-F359: one question per folder the sources sit in, not one per path - an Explorer selection
    // can be thousands of files, and each question opens the volume.
    [Fact]
    public void AnyDiskHasSeekPenalty_ManySourcesInOneFolder_AsksOncePerFolder()
    {
        var asked = new List<string>();
        string[] sources = [.. Enumerable.Range(0, 500).Select(i => $@"C:\data\f{i}.bin"), @"D:\other\g.bin"];

        bool any = ZipArchiveService.AnyDiskHasSeekPenalty(path => { asked.Add(path); return false; }, @"E:\out", sources);

        any.Should().BeFalse();
        asked.Should().BeEquivalentTo(@"E:\out", @"C:\data", @"D:\other");
    }

    [Theory]
    [InlineData(@"E:\out")]
    [InlineData(@"D:\other")]
    public void AnyDiskHasSeekPenalty_DestinationOrAnySource_IsEnough(string slow)
    {
        ZipArchiveService.AnyDiskHasSeekPenalty(path => path == slow, @"E:\out", [@"C:\data\a.bin", @"D:\other\g.bin"])
            .Should().BeTrue();
    }

    [Fact]
    public void AnyDiskHasSeekPenalty_DriveRootAsASource_IsAskedAboutItself()
    {
        var asked = new List<string>();

        ZipArchiveService.AnyDiskHasSeekPenalty(path => { asked.Add(path); return false; }, @"E:\out", [@"C:\"]);

        asked.Should().BeEquivalentTo(@"E:\out", @"C:\");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ArchiveAsync_ManyFilesOnADiskWithASeekPenalty_LargeFilesTakeTurnsAndTheArchiveIsTheSame(bool singleArchive)
    {
        string folder = Path.Combine(_temp.Path, "many");
        for (int i = 0; i < 70; i++)
            CreateFileIn("many", $"s{i:D2}.txt", [(byte)i]);
        foreach (int i in Enumerable.Range(0, 4))
            CreateCompressibleFile("many", $"large{i}.log", i);
        // The first large file to report holds its worker inside the report; any other large file
        // that reports meanwhile was being compressed beside it.
        string? held = null;
        using var anotherLargeFileReported = new ManualResetEventSlim();
        bool overlapped = false;
        var progress = new SynchronousProgress(report =>
        {
            if (report.CurrentFile is not { } file || !file.Contains("large", StringComparison.Ordinal))
                return;
            if (Interlocked.CompareExchange(ref held, file, null) is null)
                overlapped = anotherLargeFileReported.Wait(TimeSpan.FromSeconds(1));
            else if (held != file)
                anotherLargeFileReported.Set();
        });
        var slow = new ZipArchiveService(new GroupPolicyOptions()) { HasSeekPenalty = _ => true };
        var fast = new ZipArchiveService(new GroupPolicyOptions()) { HasSeekPenalty = _ => false };
        ArchiveOptions Options(string outFolder) => new()
        {
            SourcePaths = [folder], DestinationFolder = Path.Combine(_temp.Path, outFolder), ArchiveName = "many",
            Mode = singleArchive ? ArchiveMode.SingleArchive : ArchiveMode.SeparateArchives,
            // SeparateArchives takes the parallel writer only where ZipArchive cannot write the archive.
            ResolvePasswordAsync = singleArchive ? null : _ => Task.FromResult(new PasswordDecision { Password = "secret" }),
        };

        ArchiveResult onSlow = await slow.ArchiveAsync(Options("slow"), progress);
        ArchiveResult onFast = await fast.ArchiveAsync(Options("fast"));

        held.Should().NotBeNull("a large file reports progress");
        overlapped.Should().BeFalse();
        // An encrypted archive has a random salt per entry, so only the plain one can be compared.
        if (singleArchive)
            File.ReadAllBytes(onSlow.CreatedFiles.Single()).Should().Equal(File.ReadAllBytes(onFast.CreatedFiles.Single()));
        else
            new FileInfo(onSlow.CreatedFiles.Single()).Length.Should().Be(new FileInfo(onFast.CreatedFiles.Single()).Length);
    }

    private string CreateCompressibleFile(string folder, string name, int seed)
    {
        // Text-like: Deflate has real work to do, so the files stay open long enough to overlap.
        var random = new Random(seed);
        byte[] content = new byte[LargeFileBytes];
        for (int i = 0; i < content.Length; i++)
            content[i] = (byte)('a' + random.Next(0, 16));
        return CreateFileIn(folder, name, content);
    }

    private Task<ArchiveResult> ArchiveAsync(string[] sources, string name) =>
        _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = sources, DestinationFolder = Path.Combine(_temp.Path, "out"), ArchiveName = name,
        });

    private async Task<ArchiveResult> ArchiveWithLockedAsync(string[] sources, string locked, string name)
    {
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            return await ArchiveAsync(sources, name);
        }
    }

    private static List<string> EntryNames(ArchiveResult result)
    {
        using ZipArchive zip = ZipFile.OpenRead(result.CreatedFiles.Single());
        return [.. zip.Entries.Select(e => e.FullName)];
    }

    private static byte[] ReadEntry(ZipArchive zip, string name)
    {
        using Stream stream = zip.GetEntry(name)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private string CreateRandomFile(string name, int seed, string folder = "large")
    {
        byte[] content = new byte[LargeFileBytes];
        new Random(seed).NextBytes(content);
        return CreateFileIn(folder, name, content);
    }

    private string CreateFileIn(string folder, string name, byte[] content)
    {
        string directory = Path.Combine(_temp.Path, folder);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private sealed class SynchronousProgress(Action<ProgressReport> onReport) : IProgress<ProgressReport>
    {
        public void Report(ProgressReport value) => onReport(value);
    }
}
