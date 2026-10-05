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
    public void Build_EntryWithoutAKnownTime_ShowsTheDateOnly()
    {
        // T-F335: an older tar entry lists with a year and no time.
        ArchiveEntryInfo[] flat = [File("old.txt") with { Modified = new DateTime(2020, 2, 3), ModifiedHasTime = false }];

        ArchiveTreeIndex.Build(flat).At("").Single().ModifiedDisplay.Should().Be("2020-02-03");
    }

    [Fact]
    public void Build_EntryWithAKnownTime_ShowsDateAndTime()
    {
        ArchiveEntryInfo[] flat = [File("new.txt") with { Modified = new DateTime(2026, 9, 3) }];

        ArchiveTreeIndex.Build(flat).At("").Single().ModifiedDisplay.Should().Be("2026-09-03 00:00");
    }

    [Fact]
    public void Build_ZipShapedInput_NoExplicitDirEntries_SynthesizesImpliedFolders()
    {
        // Mirrors valid_nested_folders.zip: no explicit directory entries, folders implied by '/'.
        ArchiveEntryInfo[] flat = new[]
        {
            File("root.txt"),
            File("docs/readme.txt"),
            File("docs/manual.txt"),
            File("docs/sub/appendix.txt"),
            File("src/main.cs"),
        };

        ArchiveTree index = ArchiveTreeIndex.Build(flat);

        index.At("").Select(e => e.Name).Should().BeEquivalentTo(["docs", "src", "root.txt"]);
        index.At("").Should().OnlyContain(e => e.FullPath != "docs" || e.IsFolder);
        index.At("docs").Select(e => e.Name).Should().BeEquivalentTo(["sub", "manual.txt", "readme.txt"]);
        index.At("docs/sub").Select(e => e.Name).Should().BeEquivalentTo(["appendix.txt"]);
        index.At("src").Select(e => e.Name).Should().BeEquivalentTo(["main.cs"]);
    }

    [Fact]
    public void Build_TarShapedInput_ExplicitDirEntries_DoesNotDoubleSynthesize()
    {
        // Mirrors valid_nested_folders.tar: explicit directory entries alongside files.
        ArchiveEntryInfo[] flat = new[]
        {
            Dir("docs"),
            File("docs/readme.txt"),
            File("root.txt"),
        };

        ArchiveTree index = ArchiveTreeIndex.Build(flat);

        index.At("").Should().HaveCount(2);
        ArchiveEntryViewModel docsNode = index.At("").Single(e => e.Name == "docs");
        docsNode.IsFolder.Should().BeTrue();
        index.At("docs").Select(e => e.Name).Should().BeEquivalentTo(["readme.txt"]);
    }

    [Fact]
    public void Build_EmptyFolder_ExplicitDirEntryWithNoChildren_AppearsWithNoChildrenKey()
    {
        ArchiveEntryInfo[] flat = new[] { Dir("empty"), File("root.txt") };

        ArchiveTree index = ArchiveTreeIndex.Build(flat);

        index.At("").Select(e => e.Name).Should().BeEquivalentTo(["empty", "root.txt"]);
        index.TryGetChildren("empty", out _).Should().BeFalse();
    }

    [Fact]
    public void Build_MixedFoldersAndFiles_SortsFoldersFirstThenAlphabetical()
    {
        ArchiveEntryInfo[] flat = new[]
        {
            File("zebra.txt"),
            File("apple.txt"),
            Dir("zzz_folder"),
            Dir("aaa_folder"),
        };

        ArchiveTree index = ArchiveTreeIndex.Build(flat);

        index.At("").Select(e => e.Name).Should().ContainInOrder("aaa_folder", "zzz_folder", "apple.txt", "zebra.txt");
    }

    [Fact]
    public void Build_LargeSyntheticInput_CompletesQuicklyAndEveryLookupIsCorrect()
    {
        const int fileCount = 70_000;
        var flat = new ArchiveEntryInfo[fileCount];
        for (int i = 0; i < fileCount; i++)
            flat[i] = File($"folder{i % 100}/file{i}.txt");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        ArchiveTree index = ArchiveTreeIndex.Build(flat);
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        index.At("").Should().HaveCount(100); // 100 distinct top-level synthesized folders
        index.At("folder0").Should().HaveCount(fileCount / 100);
    }

    [Fact]
    public void Build_ExplicitDirAfterItsImpliedAncestor_FirstNodeWins()
    {
        ArchiveTree index =
            ArchiveTreeIndex.Build([File("a/x.txt"), Dir("a"), File("a/x.txt", size: 7)]);

        ArchiveEntryViewModel a = index.At("").Single();
        a.Should().Be(new ArchiveEntryViewModel { FullPath = "a", Name = "a", IsFolder = true });
        index.At("a").Single().Size.Should().Be(100);
    }

    [Fact]
    public void Build_FileAndFolderWithTheSameName_FileRowKeepsItsChildren()
    {
        ArchiveTree index =
            ArchiveTreeIndex.Build([File("a"), File("a/x.txt")]);

        index.At("").Single().IsFolder.Should().BeFalse();
        index.At("a").Single().FullPath.Should().Be("a/x.txt");
    }

    [Fact]
    public void Build_EmptySegment_IsAFolderWithAnEmptyName()
    {
        ArchiveTree index =
            ArchiveTreeIndex.Build([File("a//b.txt")]);

        index.At("a").Single().Should().Be(new ArchiveEntryViewModel { FullPath = "a/", Name = "", IsFolder = true });
        index.At("a/").Single().FullPath.Should().Be("a//b.txt");
    }

    // Before T-F237 the empty first segment was a nameless root row whose path "" led back to the
    // root, so "/d/y.txt" could never be reached.
    [Fact]
    public void Build_LeadingSlash_BelongsToTheRootAndKeepsTheEntryPath()
    {
        ArchiveTree index = ArchiveTreeIndex.Build([File("/x.txt"), File("/d/y.txt")]);

        index.At("").Select(e => e.FullPath).Should().Equal("/d", "/x.txt");
        index.At("/d").Single().FullPath.Should().Be("/d/y.txt");
    }

    // T-F237 item 2: every ancestor used to be its own full-path string, O(depth^2) — an 80 KB ZIP
    // with one 20,000-segment name held the App at ~1.7 GB.
    [Fact]
    public void Build_TwentyThousandSegmentName_StaysWithinAFixedAllocationBudget()
    {
        const int depth = 20_000;
        string folder = string.Join('/', Enumerable.Repeat("a", depth));
        ArchiveEntryInfo[] flat = [File(folder + "/x.txt")];

        long before = GC.GetAllocatedBytesForCurrentThread();
        ArchiveTree index = ArchiveTreeIndex.Build(flat);
        IReadOnlyList<ArchiveEntryViewModel> deepest = index.At(folder);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        deepest.Single().FullPath.Should().Be(folder + "/x.txt");
        index.At("").Single().Name.Should().Be("a");
        allocated.Should().BeLessThan(32L * 1024 * 1024);
    }
}
