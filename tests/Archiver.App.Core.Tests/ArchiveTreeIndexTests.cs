using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

public sealed class ArchiveTreeIndexTests
{
    private static ArchiveEntryInfo File(string path, long size = 100) => new()
    {
        Path = path,
        Size = size,
        IsDirectory = false,
    };

    private static ArchiveEntryInfo Dir(string path) => new()
    {
        Path = path,
        IsDirectory = true,
    };

    [Fact]
    public void Build_ZipShapedInput_NoExplicitDirEntries_SynthesizesImpliedFolders()
    {
        // Mirrors valid_nested_folders.zip: no explicit directory entries, folders implied by '/'.
        var flat = new[]
        {
            File("root.txt"),
            File("docs/readme.txt"),
            File("docs/manual.txt"),
            File("docs/sub/appendix.txt"),
            File("src/main.cs"),
        };

        var index = ArchiveTreeIndex.Build(flat);

        index[""].Select(e => e.Name).Should().BeEquivalentTo(["docs", "src", "root.txt"]);
        index[""].Should().OnlyContain(e => e.FullPath != "docs" || e.IsFolder);
        index["docs"].Select(e => e.Name).Should().BeEquivalentTo(["sub", "manual.txt", "readme.txt"]);
        index["docs/sub"].Select(e => e.Name).Should().BeEquivalentTo(["appendix.txt"]);
        index["src"].Select(e => e.Name).Should().BeEquivalentTo(["main.cs"]);
    }

    [Fact]
    public void Build_TarShapedInput_ExplicitDirEntries_DoesNotDoubleSynthesize()
    {
        // Mirrors valid_nested_folders.tar: explicit directory entries alongside files.
        var flat = new[]
        {
            Dir("docs"),
            File("docs/readme.txt"),
            File("root.txt"),
        };

        var index = ArchiveTreeIndex.Build(flat);

        index[""].Should().HaveCount(2);
        var docsNode = index[""].Single(e => e.Name == "docs");
        docsNode.IsFolder.Should().BeTrue();
        index["docs"].Select(e => e.Name).Should().BeEquivalentTo(["readme.txt"]);
    }

    [Fact]
    public void Build_EmptyFolder_ExplicitDirEntryWithNoChildren_AppearsWithNoChildrenKey()
    {
        var flat = new[] { Dir("empty"), File("root.txt") };

        var index = ArchiveTreeIndex.Build(flat);

        index[""].Select(e => e.Name).Should().BeEquivalentTo(["empty", "root.txt"]);
        index.Should().NotContainKey("empty");
    }

    [Fact]
    public void Build_MixedFoldersAndFiles_SortsFoldersFirstThenAlphabetical()
    {
        var flat = new[]
        {
            File("zebra.txt"),
            File("apple.txt"),
            Dir("zzz_folder"),
            Dir("aaa_folder"),
        };

        var index = ArchiveTreeIndex.Build(flat);

        index[""].Select(e => e.Name).Should().ContainInOrder("aaa_folder", "zzz_folder", "apple.txt", "zebra.txt");
    }

    [Fact]
    public void Build_LargeSyntheticInput_CompletesQuicklyAndEveryLookupIsCorrect()
    {
        const int fileCount = 70_000;
        var flat = new ArchiveEntryInfo[fileCount];
        for (int i = 0; i < fileCount; i++)
            flat[i] = File($"folder{i % 100}/file{i}.txt");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var index = ArchiveTreeIndex.Build(flat);
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        index[""].Should().HaveCount(100); // 100 distinct top-level synthesized folders
        index["folder0"].Should().HaveCount(fileCount / 100);
    }
}
