using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F410: <see cref="ArchiveOptions.EntryFilter"/> through the real tar.exe (7-Zip's <c>a -x</c>).
/// What the filter leaves out goes to tar.exe as an anchored <c>-X</c> pattern (T-F391's list), so a
/// name holding pattern characters must leave out that entry and no other.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceCreationFilterTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

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

    private async Task<(ArchiveResult Result, string[] Files)> CompressAsync(
        IReadOnlyList<string> sources, Func<string, bool, bool> filter,
        ArchiveContainerFormat format = ArchiveContainerFormat.Tar, IReadOnlyList<int>? parts = null)
    {
        string dest = Path.Combine(_temp.Path, "out-" + Path.GetRandomFileName());
        ArchiveResult result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = sources,
            SourceNameParts = parts,
            EntryFilter = filter,
            DestinationFolder = dest,
            ArchiveName = "o",
            Format = format,
        });
        string[] files = [];
        if (result.CreatedFiles.Count == 1)
        {
            ArchiveListResult listed = await _sut.ListEntriesAsync(result.CreatedFiles[0]);
            listed.Success.Should().BeTrue(because: listed.ErrorMessage);
            files = [.. listed.Entries.Where(e => !e.IsDirectory).Select(e => e.Path.Replace('\\', '/')).Order(StringComparer.Ordinal)];
        }
        return (result, files);
    }

    [Integration]
    public async Task CompressAsync_FilterLeavesOutAFile()
    {
        string dir = MakeTree();

        (ArchiveResult result, string[] files) = await CompressAsync([dir], (name, _) => !name.EndsWith(".log", StringComparison.Ordinal));

        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        files.Should().Equal("dir/c.txt", "dir/sub/deep/d.txt", "dir/sub/s.txt");
    }

    [Integration]
    public async Task CompressAsync_FilterLeavesOutAFolderWithAllItHolds_NotASiblingStartingTheSame()
    {
        string dir = MakeTree();
        Directory.CreateDirectory(Path.Combine(dir, "sub2"));
        File.WriteAllText(Path.Combine(dir, "sub2", "x.txt"), "x");

        (ArchiveResult result, string[] files) = await CompressAsync([dir], (name, _) => name != "dir/sub");

        result.Errors.Should().BeEmpty();
        files.Should().Equal("dir/c.log", "dir/c.txt", "dir/sub2/x.txt");
    }

    [Integration]
    public async Task CompressAsync_NamesWithPatternCharacters_LeaveOutOnlyThatEntry()
    {
        string dir = MakeTree();
        File.WriteAllText(Path.Combine(dir, "a[1]$.txt"), "p");
        File.WriteAllText(Path.Combine(dir, "a1$.txt"), "q");

        (ArchiveResult result, string[] files) = await CompressAsync([dir], (name, _) => name != "dir/a[1]$.txt");

        result.Errors.Should().BeEmpty();
        files.Should().Contain("dir/a1$.txt").And.NotContain("dir/a[1]$.txt");
    }

    [Integration]
    public async Task CompressAsync_FilterLeavesOutATopLevelSource()
    {
        string dir = MakeTree();
        string file = Path.Combine(_temp.Path, "src", "t.txt");
        File.WriteAllText(file, "t");

        (ArchiveResult result, string[] files) = await CompressAsync([dir, file], (name, _) => name != "dir");

        result.Errors.Should().BeEmpty();
        files.Should().Equal("t.txt");
    }

    [Integration]
    public async Task CompressAsync_FilterSeesTheNameWithItsParts()
    {
        string dir = MakeTree();

        (ArchiveResult result, string[] files) = await CompressAsync(
            [Path.Combine(dir, "sub")], (name, _) => name != "dir/sub/s.txt", parts: [2]);

        result.Errors.Should().BeEmpty();
        files.Should().Equal("dir/sub/deep/d.txt");
    }

    [Integration]
    public async Task CompressAsync_RenamedSource_IsAskedAboutByItsOwnName()
    {
        string dir = MakeTree();
        string other = Path.Combine(_temp.Path, "other", "dir");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "c.txt"), "o");

        (ArchiveResult result, string[] files) = await CompressAsync([dir, other], (name, _) => name != "dir/c.txt");

        result.Errors.Should().BeEmpty();
        files.Should().NotContain(f => f.EndsWith("/c.txt", StringComparison.Ordinal));
    }

    [Integration]
    public async Task CompressAsync_WithAFilter_NoSourceIsFullyProcessed()
    {
        string dir = MakeTree();

        (ArchiveResult result, _) = await CompressAsync([dir], (name, _) => name != "dir/c.log");

        result.CreatedFiles.Should().ContainSingle();
        result.FullyProcessedSources.Should().BeEmpty();
    }

    [Integration]
    public async Task CompressAsync_EverythingLeftOut_WritesNoArchive()
    {
        string dir = MakeTree();

        (ArchiveResult result, _) = await CompressAsync([dir], (_, _) => false);

        result.CreatedFiles.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }

    [SevenZipWriterFact]
    public async Task CompressAsync_SevenZip_FilterLeavesOutAFolder()
    {
        string dir = MakeTree();

        (ArchiveResult result, string[] files) = await CompressAsync([dir], (name, _) => name != "dir/sub", ArchiveContainerFormat.SevenZip);

        result.Errors.Should().BeEmpty();
        files.Should().Equal("dir/c.log", "dir/c.txt");
    }
}
