using System.IO.Compression;
using Archiver.Core.IO;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>One deep chain shared by every test below — building it is the slow part.</summary>
public sealed class DeepFolderTreeFixture : IDisposable
{
    public const int Depth = 5000;
    private readonly TempDirectory _temp = new();
    private readonly DeepTree _tree;

    public DeepFolderTreeFixture()
    {
        _tree = new DeepTree(_temp.Path, "deep", Depth);
        File.WriteAllText(Path.Combine(_tree.Deepest, "f.txt"), "12345");
        ManyFiles = Path.Combine(_temp.Path, "many");
        Directory.CreateDirectory(ManyFiles);
        for (int i = 0; i < 70; i++)
            File.WriteAllText(Path.Combine(ManyFiles, $"m{i:D2}.txt"), $"content {i}");
    }

    public string Root => _tree.Root;
    public string ManyFiles { get; }

    public string NewDestination() => Path.Combine(_temp.Path, "out_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _tree.Dispose();
        _temp.Dispose();
    }
}

// T-F237: a 2,000-deep folder used to kill Pakko with an uncatchable stack overflow — every
// recursive walk (the SingleArchive totals, the SeparateArchives byte count, the sequential
// writer, the parallel producer) had no depth bound. 5,000 levels is past that point (3,000 still fit a test thread's larger stack) and far
// inside Windows' 32,767-character path limit. Slow: every Windows path call walks the whole
// path, so each pass over such a tree costs O(depth^2) in the kernel.
[Trait("Category", "Slow")]
public sealed class DeepFolderTreeTests(DeepFolderTreeFixture tree) : IClassFixture<DeepFolderTreeFixture>
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());

    private static readonly string ExpectedDeepEntry =
        "deep/" + string.Concat(Enumerable.Repeat("d/", DeepFolderTreeFixture.Depth)) + "f.txt";

    private static List<string> EntryNames(string zipPath)
    {
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        return [.. zip.Entries.Select(e => e.FullName)];
    }

    [Fact]
    public async Task ArchiveAsync_SingleArchive_Sequential_DeepTree_ArchivesTheFile()
    {
        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [tree.Root],
            DestinationFolder = tree.NewDestination(),
            ArchiveName = "deep",
        });

        result.Errors.Should().BeEmpty();
        EntryNames(result.CreatedFiles.Single()).Should().Equal(ExpectedDeepEntry);
    }

    [Fact]
    public async Task ArchiveAsync_SingleArchive_Parallel_DeepTree_ArchivesEveryFile()
    {
        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [tree.Root, tree.ManyFiles], // 71 files: the parallel writer
            DestinationFolder = tree.NewDestination(),
            ArchiveName = "deep",
        });

        result.Errors.Should().BeEmpty();
        EntryNames(result.CreatedFiles.Single()).Should().HaveCount(71).And.Contain(ExpectedDeepEntry);
    }

    [Fact]
    public async Task ArchiveAsync_SeparateArchives_DeepTree_ArchivesTheFile()
    {
        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [tree.Root],
            DestinationFolder = tree.NewDestination(),
            Mode = ArchiveMode.SeparateArchives,
        });

        result.Errors.Should().BeEmpty();
        EntryNames(result.CreatedFiles.Single()).Should().Equal(ExpectedDeepEntry);
    }

    [Fact]
    public void TarPreScanCount_DeepTree_CountsEveryLevel()
    {
        (long entries, long bytes, string? unrepresentable) = TarSandboxedService.CountRecursiveEntriesAndBytes(tree.Root);

        entries.Should().Be(DeepFolderTreeFixture.Depth + 2); // root + every level + the file
        bytes.Should().Be(5);
        unrepresentable.Should().BeNull();
    }

    [Fact]
    public void Walk_DeepTree_ReachesTheBottom()
    {
        List<WalkEntry> entries = [.. DirectoryWalker.Walk(tree.Root)];

        entries.Count(e => e.Kind == WalkEntryKind.Directory).Should().Be(DeepFolderTreeFixture.Depth + 1);
        entries.Should().ContainSingle(e => e.Kind == WalkEntryKind.File);
    }
}
