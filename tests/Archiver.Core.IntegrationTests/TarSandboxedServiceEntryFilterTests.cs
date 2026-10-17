using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

// T-F410: ExtractOptions.EntryFilter through the real tar.exe, as Archiver.Core.Tests'
// EntryFilterTests does for ZIP. tar.exe gets the names that passed in a list file ("-T"), never
// on its command line, and without recursion ("-n"), so a folder that passes brings only itself.
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceEntryFilterTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static TarBuilder.Entry File(string name) => new() { Name = name, Content = Encoding.ASCII.GetBytes(name) };
    private static TarBuilder.Entry Dir(string name) => new() { Name = name, TypeFlag = '5' };

    private static bool NotNsis(string path, bool isFolder) => !path.EndsWith(".nsis", StringComparison.Ordinal);

    [Integration]
    public async Task ExtractAsync_Filter_ExtractsOnlyWhatPasses()
    {
        string tar = Tar([File("a.txt"), File("b.nsis"), Dir("dir/"), File("dir/c.txt"), Dir("dir/sub/"), File("dir/sub/d.nsis")]);
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(Options(tar, dest, NotNsis));

        result.Errors.Should().BeEmpty();
        Relative(dest).Should().BeEquivalentTo("a.txt", "dir", "dir/c.txt", "dir/sub");
    }

    [Integration]
    public async Task ExtractAsync_FolderPasses_ItsContentDoesNotComeWithIt()
    {
        string tar = Tar([Dir("dir/"), File("dir/c.txt"), Dir("dir/sub/"), File("dir/sub/d.txt")]);
        string dest = NewDestination();

        await _sut.ExtractAsync(Options(tar, dest, (path, _) => !path.StartsWith("dir/sub/", StringComparison.Ordinal)));

        Relative(dest).Should().BeEquivalentTo("dir", "dir/c.txt");
    }

    [Integration]
    public async Task ExtractAsync_Filter_IsAskedWithTheNamesListingShows()
    {
        // T-F196: "./" is how `tar -C dir .` names members; the listing leaves it off, so does the filter.
        string tar = Tar([Dir("./"), File("./a.txt"), Dir("./dir/"), File("./dir/c.txt")]);
        var asked = new List<(string, bool)>();

        await _sut.ExtractAsync(Options(tar, NewDestination(), (path, isFolder) =>
        {
            asked.Add((path, isFolder));
            return true;
        }));

        asked.Should().BeEquivalentTo([("a.txt", false), ("dir/", true), ("dir/c.txt", false)]);
    }

    [Integration]
    public async Task ExtractAsync_NothingPasses_CreatesNothingAndSucceeds()
    {
        string tar = Tar([File("a.txt"), File("b.txt")]);
        string dest = Path.Combine(_temp.Path, "out");

        ArchiveResult result = await _sut.ExtractAsync(Options(tar, dest, (_, _) => false));

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().BeEmpty();
        Directory.Exists(dest).Should().BeFalse();
    }

    [Integration]
    public async Task ExtractAsync_NameWithPatternCharacters_TakesOnlyThatName()
    {
        // T-F284: tar.exe reads a member as a pattern; "a[1].txt" must not take "a1.txt". In the list
        // file a line starting with '-' is a name, "-C" too (bsdtar's "change directory" line in c mode).
        string tar = Tar([File("a[1].txt"), File("a1.txt"), File("-dash.txt"), File("-C"), File("x.txt")]);
        string dest = NewDestination();

        await _sut.ExtractAsync(Options(tar, dest, (path, _) => path != "a1.txt"));

        Relative(dest).Should().BeEquivalentTo("a[1].txt", "-dash.txt", "-C", "x.txt");
    }

    [Integration]
    public async Task ExtractAsync_ThousandsOfNamesPass_NoCommandLineLimit()
    {
        // 5,000 names of ~40 characters are far past the 32,767-character command line.
        List<TarBuilder.Entry> entries = [.. Enumerable.Range(0, 5000).Select(i => File($"folder-with-a-long-name/file-{i:D5}.txt"))];
        entries.Add(File("folder-with-a-long-name/skip.nsis"));
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(Options(Tar(entries), dest, NotNsis));

        result.Errors.Should().BeEmpty();
        Directory.EnumerateFiles(Path.Combine(dest, "folder-with-a-long-name")).Should().HaveCount(5000);
    }

    [Integration]
    public async Task ExtractAsync_UnsafeEntryFilteredOut_StillRefusesTheArchive()
    {
        // T-F49: the whole archive is checked before anything is extracted, whatever the filter keeps.
        string tar = Tar([File("a.txt"), File("../evil.txt")]);
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(Options(tar, dest, (path, _) => path == "a.txt"));

        result.Errors.Should().NotBeEmpty();
        Relative(dest).Should().BeEmpty();
    }

    [Integration]
    public async Task ExtractAsync_FilterAndSelection_BothApply()
    {
        string tar = Tar([File("a.txt"), Dir("dir/"), File("dir/c.txt"), File("dir/d.nsis")]);
        string dest = NewDestination();

        await _sut.ExtractAsync(Options(tar, dest, NotNsis) with { SelectedEntryPaths = ["dir"] });

        Relative(dest).Should().BeEquivalentTo("dir", "dir/c.txt");
    }

    [Integration]
    public async Task ExtractAsync_Filtered_TheArchiveIsNotFullyProcessed()
    {
        // T-F265: a subset never makes the archive deletable ("delete after extraction").
        ArchiveResult result = await _sut.ExtractAsync(Options(Tar([File("a.txt")]), NewDestination(), (_, _) => true));

        result.FullyProcessedSources.Should().BeEmpty();
    }

    private static ExtractOptions Options(string tar, string dest, Func<string, bool, bool> filter) => new()
    {
        ArchivePaths = [tar], DestinationFolder = dest, Mode = ExtractMode.SingleFolder, EntryFilter = filter,
    };

    private string Tar(List<TarBuilder.Entry> entries)
    {
        string path = Path.Combine(_temp.Path, "pkg" + Guid.NewGuid().ToString("N")[..6] + ".tar");
        TarBuilder.WriteTar(path, entries);
        return path;
    }

    private string NewDestination()
    {
        string dest = Path.Combine(_temp.Path, "out" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dest);
        return dest;
    }

    private static IEnumerable<string> Relative(string dest) =>
        Directory.EnumerateFileSystemEntries(dest, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(dest, p).Replace('\\', '/'));
}
