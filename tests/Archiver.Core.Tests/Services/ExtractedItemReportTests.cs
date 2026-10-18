using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F408: ExtractOptions.OnItemExtracted names every file and folder an extraction delivered to the
// destination — pakko's -bb1 prints a "- <name>" line per item, which Chocolatey records as the
// installed files. Only what really arrived counts: a locked or skipped file is not named, and a
// failed fast-path commit that falls back to the per-file merge names nothing twice.
public sealed class ExtractedItemReportTests : IDisposable
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string Sep => Path.DirectorySeparatorChar.ToString();

    // --- ExtractionStaging.CommitInto ---

    [Fact]
    public void CommitInto_FastPath_NamesTheNewFolderAndEverythingInIt()
    {
        using ExtractionStaging staging = StagingWithSubFolder();
        string actualDest = Path.Combine(_temp.Path, "out");
        var named = new List<string>();

        staging.CommitInto(actualDest, committed: named.Add);

        named.Should().BeEquivalentTo(
            actualDest + Sep, Path.Combine(actualDest, "Sub") + Sep,
            Path.Combine(actualDest, "loose1.txt"), Path.Combine(actualDest, "Sub", "nested1.txt"));
    }

    [Fact]
    public void CommitInto_FastPathFails_TheMergeNamesEachItemOnce()
    {
        using ExtractionStaging staging = StagingWithSubFolder();
        string actualDest = Path.Combine(_temp.Path, "out");
        var named = new List<string>();

        staging.CommitInto(actualDest, (src, _) => throw new IOException($"Access to the path '{src}' is denied."), named.Add);

        named.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(
            actualDest + Sep, Path.Combine(actualDest, "Sub") + Sep,
            Path.Combine(actualDest, "loose1.txt"), Path.Combine(actualDest, "Sub", "nested1.txt"));
    }

    [Fact]
    public void CommitInto_ExistingDestination_IsNotNamedButWhatArrivesIs()
    {
        using ExtractionStaging staging = StagingWithSubFolder();
        string actualDest = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(actualDest);
        var named = new List<string>();

        staging.CommitInto(actualDest, committed: named.Add);

        named.Should().BeEquivalentTo(
            Path.Combine(actualDest, "Sub") + Sep, Path.Combine(actualDest, "loose1.txt"), Path.Combine(actualDest, "Sub", "nested1.txt"));
    }

    [Fact]
    public void CommitInto_LockedDestinationFile_IsNotNamed()
    {
        using ExtractionStaging staging = StagingWithSubFolder();
        string actualDest = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(actualDest);
        string locked = Path.Combine(actualDest, "loose1.txt");
        File.WriteAllText(locked, "old");
        var named = new List<string>();

        using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
            staging.CommitInto(actualDest, committed: named.Add).Should().Equal("loose1.txt");

        named.Should().NotContain(locked).And.Contain(Path.Combine(actualDest, "Sub", "nested1.txt"));
    }

    // T-F419: a junction already in the destination is not a way out of it; the destination itself may be one.
    [Fact]
    public void CommitInto_JunctionInsideTheDestination_IsNotEnteredAndIsListed()
    {
        using ExtractionStaging staging = StagingWithSubFolder();
        string actualDest = Path.Combine(_temp.Path, "out");
        string outside = Path.Combine(_temp.Path, "outside");
        Directory.CreateDirectory(actualDest);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "nested1.txt"), "precious");
        string junction = Path.Combine(actualDest, "Sub");
        ZipArchiveServiceArchiveTests.TryCreateJunction(junction, outside).Should().BeTrue();
        var named = new List<string>();
        try
        {
            staging.CommitInto(actualDest, committed: named.Add).Should().BeEmpty();

            File.ReadAllText(Path.Combine(outside, "nested1.txt")).Should().Be("precious");
            staging.LeftOutThroughLink.Should().BeEquivalentTo("Sub" + Sep, Path.Combine("Sub", "nested1.txt"));
            named.Should().Equal(Path.Combine(actualDest, "loose1.txt"));
        }
        finally
        {
            // TempDirectory's recursive delete cannot remove a junction.
            Directory.Delete(junction, recursive: false);
        }
    }

    [Fact]
    public void CommitInto_DestinationIsAJunction_IsWrittenThrough()
    {
        using ExtractionStaging staging = StagingWithSubFolder();
        string real = Path.Combine(_temp.Path, "real");
        string actualDest = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(real);
        ZipArchiveServiceArchiveTests.TryCreateJunction(actualDest, real).Should().BeTrue();
        try
        {
            staging.CommitInto(actualDest).Should().BeEmpty();

            staging.LeftOutThroughLink.Should().BeEmpty();
            File.ReadAllText(Path.Combine(real, "Sub", "nested1.txt")).Should().Be("nested1");
        }
        finally
        {
            Directory.Delete(actualDest, recursive: false);
        }
    }

    private ExtractionStaging StagingWithSubFolder()
    {
        var staging = ExtractionStaging.Create(_temp.Path);
        Directory.CreateDirectory(Path.Combine(staging.Path, "Sub"));
        File.WriteAllText(Path.Combine(staging.Path, "loose1.txt"), "loose1");
        File.WriteAllText(Path.Combine(staging.Path, "Sub", "nested1.txt"), "nested1");
        return staging;
    }

    // --- ZipArchiveService.ExtractAsync, every route: what is named is what is on disk ---

    [Theory]
    [InlineData(ExtractMode.SingleFolder, false)]
    [InlineData(ExtractMode.SingleFolder, true)]
    [InlineData(ExtractMode.SeparateFolders, false)]
    public async Task ExtractAsync_NamesExactlyWhatLandsInTheDestination(ExtractMode mode, bool eliminateDuplicateRoot)
    {
        string zip = Zip("pkg.zip", "pkg/a.txt", "pkg/sub/b.txt", "pkg/empty/");
        string dest = NewDestination();
        var named = new List<string>();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip], DestinationFolder = dest, Mode = mode,
            EliminateDuplicateRootFolder = eliminateDuplicateRoot, OnItemExtracted = named.Add,
        });

        result.Success.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        named.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(OnDisk(dest));
    }

    [Fact]
    public async Task ExtractAsync_SelectedEntries_NamesOnlyThose()
    {
        string zip = Zip("pkg.zip", "a.txt", "sub/b.txt", "c.txt");
        string dest = NewDestination();
        var named = new List<string>();

        await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            SelectedEntryPaths = ["sub/b.txt"], OnItemExtracted = named.Add,
        });

        named.Should().BeEquivalentTo(OnDisk(dest));
        named.Should().Contain(Path.Combine(dest, "sub", "b.txt")).And.NotContain(Path.Combine(dest, "a.txt"));
    }

    [Fact]
    public async Task ExtractAsync_FileSkippedBecauseItExists_IsNotNamed()
    {
        string zip = Zip("pkg.zip", "a.txt", "b.txt");
        string dest = NewDestination();
        File.WriteAllText(Path.Combine(dest, "a.txt"), "mine");
        var named = new List<string>();

        await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Skip, OnItemExtracted = named.Add,
        });

        named.Should().Equal(Path.Combine(dest, "b.txt"));
        File.ReadAllText(Path.Combine(dest, "a.txt")).Should().Be("mine");
    }

    [Fact]
    public async Task ExtractAsync_RenamedOnConflict_NamesTheNewName()
    {
        string zip = Zip("pkg.zip", "a.txt");
        string dest = NewDestination();
        File.WriteAllText(Path.Combine(dest, "a.txt"), "mine");
        var named = new List<string>();

        await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Rename, OnItemExtracted = named.Add,
        });

        named.Should().ContainSingle().Which.Should().NotBe(Path.Combine(dest, "a.txt"));
        File.Exists(named[0]).Should().BeTrue();
    }

    private string NewDestination()
    {
        string dest = Path.Combine(_temp.Path, "dest-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dest);
        return dest;
    }

    private string Zip(string name, params string[] entries)
    {
        string path = Path.Combine(_temp.Path, name);
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (string entry in entries)
        {
            ZipArchiveEntry created = archive.CreateEntry(entry);
            if (!entry.EndsWith('/'))
            {
                using var writer = new StreamWriter(created.Open());
                writer.Write(entry);
            }
        }
        return path;
    }

    // Every folder (with a trailing separator) and file under the destination, the destination itself left out.
    internal static IEnumerable<string> OnDisk(string dest) =>
        Directory.EnumerateDirectories(dest, "*", SearchOption.AllDirectories).Select(d => d + Path.DirectorySeparatorChar)
            .Concat(Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories));
}
