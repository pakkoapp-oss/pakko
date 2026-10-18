using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F414: <see cref="ArchiveOptions.SourceNameParts"/> keeps the last parts of a source's path in
/// its entry names (7-Zip's "path as typed": <c>a o.zip dir\sub</c> stores <c>dir/sub/...</c>).
/// Optimal runs the sequential writer, Fastest the hand-rolled parallel one; both are checked.
/// </summary>
public sealed class ZipArchiveServiceSourceNamePartsTests : IDisposable
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    public static TheoryData<CompressionLevel> Writers => [CompressionLevel.Optimal, CompressionLevel.Fastest];

    private string MakeTree()
    {
        string sub = Path.Combine(_temp.Path, "src", "dir", "sub");
        Directory.CreateDirectory(Path.Combine(sub, "deep"));
        File.WriteAllText(Path.Combine(sub, "s.txt"), "s");
        File.WriteAllText(Path.Combine(sub, "deep", "d.txt"), "d");
        File.WriteAllText(Path.Combine(_temp.Path, "src", "dir", "c.txt"), "c");
        return Path.Combine(_temp.Path, "src");
    }

    private async Task<(ArchiveResult Result, string[] Entries)> ArchiveAsync(
        IReadOnlyList<string> sources, IReadOnlyList<int>? parts, CompressionLevel level, ArchiveMode mode = ArchiveMode.SingleArchive)
    {
        string dest = Path.Combine(_temp.Path, "out-" + Path.GetRandomFileName());
        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = sources,
            SourceNameParts = parts,
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
    public async Task ArchiveAsync_FolderWithTwoParts_KeepsParentInEntryNames(CompressionLevel level)
    {
        string src = MakeTree();

        (ArchiveResult result, string[] entries) = await ArchiveAsync([Path.Combine(src, "dir", "sub")], [2], level);

        result.Errors.Should().BeEmpty();
        entries.Should().Equal("dir/sub/deep/d.txt", "dir/sub/s.txt");
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public async Task ArchiveAsync_FileWithThreeParts_KeepsBothFolders(CompressionLevel level)
    {
        string src = MakeTree();

        (ArchiveResult result, string[] entries) = await ArchiveAsync([Path.Combine(src, "dir", "sub", "s.txt")], [3], level);

        result.Errors.Should().BeEmpty();
        entries.Should().Equal("dir/sub/s.txt");
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public async Task ArchiveAsync_NullParts_NamesEachSourceAfterItself(CompressionLevel level)
    {
        string src = MakeTree();

        (ArchiveResult result, string[] entries) = await ArchiveAsync([Path.Combine(src, "dir", "sub")], null, level);

        result.Errors.Should().BeEmpty();
        entries.Should().Equal("sub/deep/d.txt", "sub/s.txt");
    }

    [Fact]
    public async Task ArchiveAsync_TrailingSeparatorAndOtherCase_StillTakesTheParts()
    {
        string src = MakeTree();
        string typed = Path.Combine(src, "DIR", "sub") + Path.DirectorySeparatorChar;

        (ArchiveResult result, string[] entries) = await ArchiveAsync([typed], [2], CompressionLevel.Optimal);

        result.Errors.Should().BeEmpty();
        entries.Should().Equal("DIR/sub/deep/d.txt", "DIR/sub/s.txt");
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public async Task ArchiveAsync_TwoSourcesSameLastName_DifferentParents_NoRename(CompressionLevel level)
    {
        string src = MakeTree();
        string other = Path.Combine(src, "dir2", "sub");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "x.txt"), "x");

        (ArchiveResult result, string[] entries) = await ArchiveAsync(
            [Path.Combine(src, "dir", "sub"), other], [2, 2], level);

        result.Errors.Should().BeEmpty();
        entries.Should().Equal("dir/sub/deep/d.txt", "dir/sub/s.txt", "dir2/sub/x.txt");
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public async Task ArchiveAsync_OneNameInsideAnother_RefusedBeforeAnythingIsWritten(CompressionLevel level)
    {
        string src = MakeTree();

        (ArchiveResult result, _) = await ArchiveAsync(
            [Path.Combine(src, "dir"), Path.Combine(src, "dir", "c.txt")], [1, 2], level);

        result.CreatedFiles.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.SourceNamesOverlap);
    }

    // "dir-x" sorts between "dir" and "dir/c.txt" ('-' is below '/'), so a check of neighbours
    // in name order alone would miss the overlap.
    [Fact]
    public async Task ArchiveAsync_OverlapWithANameSortedBetween_IsStillFound()
    {
        string src = MakeTree();
        string between = Path.Combine(src, "dir-x");
        File.WriteAllText(between, "x");

        (ArchiveResult result, _) = await ArchiveAsync(
            [Path.Combine(src, "dir"), between, Path.Combine(src, "dir", "c.txt")], [1, 1, 2], CompressionLevel.Optimal);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.SourceNamesOverlap);
    }

    [Fact]
    public async Task ArchiveAsync_OverlapIgnoresCase()
    {
        string src = MakeTree();

        (ArchiveResult result, _) = await ArchiveAsync(
            [Path.Combine(src, "dir"), Path.Combine(src, "DIR", "c.txt")], [1, 2], CompressionLevel.Optimal);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.SourceNamesOverlap);
    }

    [Fact]
    public async Task ArchiveAsync_SameFullNameTwice_IsRenamedNotRefused()
    {
        string src = MakeTree();
        string file = Path.Combine(src, "dir", "c.txt");

        (ArchiveResult result, string[] entries) = await ArchiveAsync([file, file], [2, 2], CompressionLevel.Optimal);

        result.Errors.Should().BeEmpty();
        entries.Should().Equal("dir/c (1).txt", "dir/c.txt");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99)]
    public async Task ArchiveAsync_PartsOutOfRange_IsAnError(int parts)
    {
        string src = MakeTree();

        (ArchiveResult result, _) = await ArchiveAsync([Path.Combine(src, "dir")], [parts], CompressionLevel.Optimal);

        result.CreatedFiles.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.SourceNamePartsInvalid);
    }

    [Fact]
    public async Task ArchiveAsync_PartsCountDiffersFromSources_IsAnError()
    {
        string src = MakeTree();

        (ArchiveResult result, _) = await ArchiveAsync([Path.Combine(src, "dir")], [1, 1], CompressionLevel.Optimal);

        result.CreatedFiles.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.SourceNamePartsInvalid);
    }

    [Fact]
    public async Task ArchiveAsync_PartsReachingADotDotOfAQualifiedPath_IsAnError()
    {
        string src = MakeTree();
        string typed = Path.Combine(src, "dir", "..", "dir", "c.txt");

        (ArchiveResult result, _) = await ArchiveAsync([typed], [3], CompressionLevel.Optimal);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.SourceNamePartsInvalid);
    }

    [Fact]
    public async Task ArchiveAsync_SeparateArchives_KeepsThePartsToo()
    {
        string src = MakeTree();

        (ArchiveResult result, _) = await ArchiveAsync([Path.Combine(src, "dir", "sub")], [2], CompressionLevel.Optimal, ArchiveMode.SeparateArchives);

        result.Errors.Should().BeEmpty();
        using ZipArchive zip = ZipFile.OpenRead(result.CreatedFiles.Should().ContainSingle().Subject);
        zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).Should().Equal("dir/sub/deep/d.txt", "dir/sub/s.txt");
    }
}
