using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

// T-F410: ExtractOptions.EntryFilter through the real tar.exe, as Archiver.Core.Tests'
// EntryFilterTests does for ZIP. tar.exe extracts what it would without the filter, and the move
// out of the quarantine leaves out what did not pass (a list of thousands of names costs
// libarchive N^2 matches). A selection (SelectedEntryPaths) goes to tar.exe as a list file ("-T"),
// never on its command line, and without recursion ("-n").
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
    public async Task ExtractAsync_ThousandsOfNamesPass_AllOfThemArrive()
    {
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

    [Integration]
    public async Task ExtractAsync_ThousandsOfNamesSelected_NoCommandLineLimit()
    {
        // 5,000 names of ~40 characters are far past the 32,767-character command line.
        List<TarBuilder.Entry> entries = [.. Enumerable.Range(0, 5000).Select(i => File($"folder-with-a-long-name/file-{i:D5}.txt"))];
        entries.Add(File("folder-with-a-long-name/skip.nsis"));
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [Tar(entries)], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            SelectedEntryPaths = [.. entries.Where(e => !e.Name.EndsWith(".nsis", StringComparison.Ordinal)).Select(e => e.Name)],
        });

        result.Errors.Should().BeEmpty();
        Directory.EnumerateFiles(Path.Combine(dest, "folder-with-a-long-name")).Should().HaveCount(5000);
    }

    [Integration]
    public async Task ExtractAsync_SelectedNamesWithPatternCharacters_TakeOnlyThoseNames()
    {
        // T-F284 through the list file: escaped, and a line starting with '-' ("-C" too) is a name.
        string tar = Tar([File("a[1].txt"), File("a1.txt"), File("-dash.txt"), File("-C"), File("x.txt")]);
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [tar], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            SelectedEntryPaths = ["a[1].txt", "-dash.txt", "-C"],
        });

        result.Errors.Should().BeEmpty();
        Relative(dest).Should().BeEquivalentTo("a[1].txt", "-dash.txt", "-C");
    }

    [Integration]
    public async Task ExtractAsync_NonAsciiNameInAUtf8Header_BySelectionAndByFilter()
    {
        // A UTF-8 header makes the scope read names with hdrcharset=UTF-8; the list file must name
        // the member so that tar.exe finds it (measured: ANSI bytes, as on the command line). A
        // name outside the ANSI code page is listed as "????.txt" (bsdtar 3.5.2 on 1252), so the
        // name is one this machine's code pages share.
        if (TarCodePage.PortableNonAsciiName() is not { } name)
            return;
        TarBuilder.Entry cyrillic = new() { Name = name, NameBytes = Encoding.UTF8.GetBytes(name), Content = [1] };
        string tar = Tar([cyrillic, File("other.txt")]);
        string bySelection = NewDestination();
        string byFilter = NewDestination();

        ArchiveResult selected = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [tar], DestinationFolder = bySelection, Mode = ExtractMode.SingleFolder, SelectedEntryPaths = [name],
        });
        ArchiveResult filtered = await _sut.ExtractAsync(Options(tar, byFilter, (path, _) => path == name));

        selected.Errors.Should().BeEmpty();
        filtered.Errors.Should().BeEmpty();
        Relative(bySelection).Should().Equal(name);
        Relative(byFilter).Should().Equal(name);
    }

    [Integration]
    public async Task ExtractAsync_NameTarWritesDifferently_IsNotLostToTheFilter()
    {
        // libarchive writes "dir\a.txt" as dir/a.txt on Windows; the filter saw the listed name.
        // Only a file the filter left out is dropped, so this one still arrives.
        TarBuilder.Entry backslash = new() { Name = "dir\\a.txt", Content = [1] };
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(Options(Tar([backslash, File("x.tmp")]), dest,
            (path, _) => !path.EndsWith(".tmp", StringComparison.Ordinal)));

        result.Errors.Should().BeEmpty();
        Relative(dest).Should().NotContain("x.tmp").And.Contain(p => p.EndsWith("a.txt", StringComparison.Ordinal));
    }

    [Integration]
    public async Task ExtractAsync_SelectionNamingNoEntry_ExtractsNothing()
    {
        // A selected name the listing does not have (a name tar.exe lists as "????.txt" on 1252)
        // expanded to an empty member list, and an empty list extracted the whole archive.
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [Tar([File("a.txt"), File("b.txt")])], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            SelectedEntryPaths = ["no-such.txt"],
        });

        result.Errors.Should().BeEmpty();
        Relative(dest).Should().BeEmpty();
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
