using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F410: ExtractOptions.EntryFilter keeps only the entries it accepts — pakko's 7-Zip name
// filters (-x!*.nsis, a folder after the archive). Unlike SelectedEntryPaths a folder that passes
// does not bring what is inside it, so an exclusion inside an included folder holds.
public sealed class EntryFilterTests : IDisposable
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ExtractAsync_Filter_ExtractsOnlyWhatPasses()
    {
        string zip = Zip("pkg.zip", "a.txt", "b.nsis", "dir/", "dir/c.txt", "dir/sub/", "dir/sub/d.nsis");
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(Options(zip, dest, (path, _) => !path.EndsWith(".nsis", StringComparison.Ordinal)));

        result.Success.Should().BeTrue();
        Relative(dest).Should().BeEquivalentTo("a.txt", "dir/", "dir/c.txt", "dir/sub/");
    }

    [Fact]
    public async Task ExtractAsync_Filter_IsAskedWithTheEntryNameAndWhetherItIsAFolder()
    {
        string zip = Zip("pkg.zip", "a.txt", "dir/", "dir/c.txt");
        var asked = new List<(string, bool)>();

        await _sut.ExtractAsync(Options(zip, NewDestination(), (path, isFolder) =>
        {
            asked.Add((path, isFolder));
            return true;
        }));

        asked.Should().BeEquivalentTo([("a.txt", false), ("dir/", true), ("dir/c.txt", false)]);
    }

    [Fact]
    public async Task ExtractAsync_FolderPasses_ItsContentDoesNotComeWithIt()
    {
        string zip = Zip("pkg.zip", "dir/", "dir/c.txt", "dir/sub/", "dir/sub/d.txt");
        string dest = NewDestination();

        await _sut.ExtractAsync(Options(zip, dest, (path, _) => !path.StartsWith("dir/sub/", StringComparison.Ordinal)));

        Relative(dest).Should().BeEquivalentTo("dir/", "dir/c.txt");
    }

    [Fact]
    public async Task ExtractAsync_EmptyFolderPasses_IsCreated()
    {
        string zip = Zip("pkg.zip", "a.txt", "empty/");
        string dest = NewDestination();

        await _sut.ExtractAsync(Options(zip, dest, (_, isFolder) => isFolder));

        Relative(dest).Should().BeEquivalentTo("empty/");
    }

    [Fact]
    public async Task ExtractAsync_NothingPasses_CreatesNothingAndSucceeds()
    {
        // 7-Zip's DecompressArchive returns before CreateComplexDir(outDir): "No files to process".
        string zip = Zip("pkg.zip", "a.txt", "dir/c.txt");
        string dest = Path.Combine(_temp.Path, "out");

        ArchiveResult result = await _sut.ExtractAsync(Options(zip, dest, (_, _) => false));

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
        Directory.Exists(dest).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_FilterAndSelection_BothApply()
    {
        string zip = Zip("pkg.zip", "a.txt", "dir/c.txt", "dir/d.nsis");
        string dest = NewDestination();

        await _sut.ExtractAsync(Options(zip, dest, (path, _) => !path.EndsWith(".nsis", StringComparison.Ordinal)) with
        {
            SelectedEntryPaths = ["dir"],
        });

        Relative(dest).Should().BeEquivalentTo("dir/", "dir/c.txt");
    }

    [Fact]
    public async Task ExtractAsync_Filtered_TheArchiveIsNotFullyProcessed()
    {
        // T-F265: a subset never makes the archive deletable ("delete after extraction").
        string zip = Zip("pkg.zip", "a.txt", "b.txt");

        ArchiveResult result = await _sut.ExtractAsync(Options(zip, NewDestination(), (_, _) => true));

        result.FullyProcessedSources.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_SingleRootFolderFiltered_KeepsTheRootFolder()
    {
        // A filtered extraction is a subset: no root collapse, the names stay as the filter saw them.
        string zip = Zip("pkg.zip", "pkg/", "pkg/a.txt", "pkg/b.nsis");
        string dest = NewDestination();

        await _sut.ExtractAsync(Options(zip, dest, (path, _) => !path.EndsWith(".nsis", StringComparison.Ordinal)) with
        {
            EliminateDuplicateRootFolder = true,
        });

        Relative(dest).Should().BeEquivalentTo("pkg/", "pkg/a.txt");
    }

    private static ExtractOptions Options(string zip, string dest, Func<string, bool, bool> filter) => new()
    {
        ArchivePaths = [zip], DestinationFolder = dest, Mode = ExtractMode.SingleFolder, EntryFilter = filter,
    };

    private string NewDestination()
    {
        string dest = Path.Combine(_temp.Path, "out" + Guid.NewGuid().ToString("N")[..6]);
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

    private static IEnumerable<string> Relative(string dest) =>
        ExtractedItemReportTests.OnDisk(dest).Select(p => Path.GetRelativePath(dest, p).Replace('\\', '/'));
}
