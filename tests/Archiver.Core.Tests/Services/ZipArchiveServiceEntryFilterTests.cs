using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F410: <see cref="ArchiveOptions.EntryFilter"/> when creating a ZIP (7-Zip's <c>a -x</c>): asked
/// with each entry's name in the archive; false leaves a file out, or a folder with all it holds.
/// Optimal runs the sequential writer, Fastest the hand-rolled parallel one.
/// </summary>
public sealed class ZipArchiveServiceEntryFilterTests : IDisposable
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    public static TheoryData<CompressionLevel> Writers => [CompressionLevel.Optimal, CompressionLevel.Fastest];

    private string MakeTree()
    {
        string dir = Path.Combine(_temp.Path, "src", "dir");
        Directory.CreateDirectory(Path.Combine(dir, "sub", "deep"));
        File.WriteAllText(Path.Combine(dir, "c.txt"), "c");
        File.WriteAllText(Path.Combine(dir, "c.log"), "l");
        File.WriteAllText(Path.Combine(dir, "sub", "s.txt"), "s");
        File.WriteAllText(Path.Combine(dir, "sub", "deep", "d.txt"), "d");
        return dir;
    }

    private async Task<(ArchiveResult Result, string[] Entries)> ArchiveAsync(
        IReadOnlyList<string> sources, Func<string, bool, bool> filter, CompressionLevel level,
        IReadOnlyList<int>? parts = null, ArchiveMode mode = ArchiveMode.SingleArchive)
    {
        string dest = Path.Combine(_temp.Path, "out-" + Path.GetRandomFileName());
        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = sources,
            SourceNameParts = parts,
            EntryFilter = filter,
            DestinationFolder = dest,
            ArchiveName = "o",
            CompressionLevel = level,
            Mode = mode,
        });
        string[] entries = [];
        if (result.CreatedFiles.Count == 1)
        {
            using ZipArchive zip = ZipFile.OpenRead(result.CreatedFiles[0]);
            entries = [.. zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal)];
        }
        return (result, entries);
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public async Task ArchiveAsync_FilterLeavesOutAFile(CompressionLevel level)
    {
        string dir = MakeTree();

        (ArchiveResult result, string[] entries) = await ArchiveAsync([dir], (name, _) => !name.EndsWith(".log", StringComparison.Ordinal), level);

        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        entries.Should().Equal("dir/c.txt", "dir/sub/deep/d.txt", "dir/sub/s.txt");
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public async Task ArchiveAsync_FilterLeavesOutAFolderWithAllItHolds(CompressionLevel level)
    {
        string dir = MakeTree();
        var asked = new List<string>();

        (ArchiveResult result, string[] entries) = await ArchiveAsync([dir], (name, isFolder) =>
        {
            lock (asked)
                asked.Add(name + (isFolder ? "/" : ""));
            return name != "dir/sub";
        }, level);

        result.Errors.Should().BeEmpty();
        entries.Should().Equal("dir/c.log", "dir/c.txt");
        asked.Should().Contain("dir/sub/").And.NotContain(name => name.StartsWith("dir/sub/", StringComparison.Ordinal) && name != "dir/sub/");
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public async Task ArchiveAsync_FilterSeesTheNameWithItsParts(CompressionLevel level)
    {
        string dir = MakeTree();

        (ArchiveResult result, string[] entries) = await ArchiveAsync(
            [Path.Combine(dir, "sub")], (name, _) => name != "dir/sub/s.txt", level, parts: [2]);

        result.Errors.Should().BeEmpty();
        entries.Should().Equal("dir/sub/deep/d.txt");
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public async Task ArchiveAsync_FilterLeavesOutATopLevelSource(CompressionLevel level)
    {
        string dir = MakeTree();
        string file = Path.Combine(_temp.Path, "src", "t.txt");
        File.WriteAllText(file, "t");

        (ArchiveResult result, string[] entries) = await ArchiveAsync([dir, file], (name, _) => name != "dir", level);

        result.Errors.Should().BeEmpty();
        entries.Should().Equal("t.txt");
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public async Task ArchiveAsync_FolderWhoseFilesAreAllLeftOut_IsKeptEmpty(CompressionLevel level)
    {
        string dir = MakeTree();

        (ArchiveResult result, string[] entries) = await ArchiveAsync([dir], (name, isFolder) => isFolder || name == "dir/c.txt", level);

        result.Errors.Should().BeEmpty();
        entries.Should().Equal("dir/c.txt", "dir/sub/deep/");
    }

    // Both engines ask by the name before a clash renames a source (T-F30): "dir (1)" is "dir".
    [Theory]
    [MemberData(nameof(Writers))]
    public async Task ArchiveAsync_RenamedSource_IsAskedAboutByItsOwnName(CompressionLevel level)
    {
        string dir = MakeTree();
        string other = Path.Combine(_temp.Path, "other", "dir");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "c.txt"), "o");

        (ArchiveResult result, string[] entries) = await ArchiveAsync([dir, other], (name, _) => name != "dir/c.txt", level);

        result.Errors.Should().BeEmpty();
        entries.Should().NotContain(e => e.EndsWith("/c.txt", StringComparison.Ordinal));
    }

    // T-F260: what a filter leaves out is not reported, so no source counts as fully archived.
    [Theory]
    [MemberData(nameof(Writers))]
    public async Task ArchiveAsync_WithAFilter_NoSourceIsFullyProcessed(CompressionLevel level)
    {
        string dir = MakeTree();

        (ArchiveResult result, _) = await ArchiveAsync([dir], (name, _) => name != "dir/c.log", level);

        result.CreatedFiles.Should().ContainSingle();
        result.FullyProcessedSources.Should().BeEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_EverythingLeftOut_WritesNoArchive()
    {
        string dir = MakeTree();

        (ArchiveResult result, _) = await ArchiveAsync([dir], (_, _) => false, CompressionLevel.Optimal);

        result.CreatedFiles.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_SeparateArchives_FilterAppliesInEach()
    {
        string dir = MakeTree();

        (ArchiveResult result, _) = await ArchiveAsync([dir], (name, _) => name != "dir/c.log", CompressionLevel.Optimal, mode: ArchiveMode.SeparateArchives);

        result.Errors.Should().BeEmpty();
        using ZipArchive zip = ZipFile.OpenRead(result.CreatedFiles.Should().ContainSingle().Subject);
        zip.Entries.Select(e => e.FullName).Should().NotContain("dir/c.log").And.Contain("dir/c.txt");
    }
}
