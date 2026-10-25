using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F284: tar.exe reads an extract member as a wildcard pattern, so "a[1].txt" selected "a1.txt".
/// Each archive here holds a bracket-named entry next to the name its pattern would match.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceMemberPatternTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string WriteArchive(params (string Name, string Content)[] entries)
    {
        string archivePath = Path.Combine(_temp.Path, "glob-" + Path.GetRandomFileName() + ".tar");
        TarBuilder.WriteTar(archivePath,
            entries.Select(e => new TarBuilder.Entry { Name = e.Name, Content = Encoding.ASCII.GetBytes(e.Content) }));
        return archivePath;
    }

    private async Task<(ArchiveResult Result, Dictionary<string, string> Tree)> ExtractAsync(
        string archivePath, IReadOnlyList<string>? selected, ConflictBehavior onConflict = ConflictBehavior.Rename)
    {
        string destDir = Path.Combine(_temp.Path, "out-" + Path.GetRandomFileName());
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            OnConflict = onConflict,
            SelectedEntryPaths = selected,
        });
        Dictionary<string, string> tree = Directory.Exists(destDir)
            ? Directory.GetFiles(destDir, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(destDir, f).Replace('\\', '/'), File.ReadAllText)
            : [];
        return (result, tree);
    }

    [Integration]
    public async Task ExtractAsync_SelectedBracketName_ExtractsThatEntryNotItsPatternMatch()
    {
        string archive = WriteArchive(("a[1].txt", "bracket"), ("a1.txt", "plain"));

        (ArchiveResult result, Dictionary<string, string> tree) = await ExtractAsync(archive, ["a[1].txt"]);

        result.Errors.Should().BeEmpty();
        tree.Should().BeEquivalentTo(new Dictionary<string, string> { ["a[1].txt"] = "bracket" });
    }

    // A backslash escapes the next pattern character. T-F418: the listing reads tar.exe's doubled
    // backslash back to one, and the member pattern doubles it again.
    [Integration]
    public async Task ExtractAsync_SelectedNameWithABackslash_ExtractsThatEntryNotItsPatternMatch()
    {
        string archive = WriteArchive(("a\\b.txt", "backslash"), ("ab.txt", "plain"), ("c\\[d.txt", "both"), ("c[d.txt", "bracket"));
        ArchiveListResult listing = await _sut.ListEntriesAsync(archive);
        string[] listed = [.. listing.Entries.Where(e => !e.IsDirectory).Select(e => e.Path)];
        listed.Should().HaveCount(4, string.Join(" | ", listing.Entries.Select(e => e.Path)));

        (ArchiveResult result, Dictionary<string, string> tree) = await ExtractAsync(archive, [listed[0], listed[2]]);

        result.Errors.Should().BeEmpty();
        tree.Values.Should().BeEquivalentTo(["backslash", "both"], string.Join(" | ", listed) + " -> " + string.Join(" | ", tree.Keys));
    }

    [Integration]
    public async Task ExtractAsync_SelectedFileInBracketFolder_ExtractsThatEntryNotItsPatternMatch()
    {
        string archive = WriteArchive(("d[x]/f.txt", "bracket"), ("dx/f.txt", "plain"));

        (ArchiveResult result, Dictionary<string, string> tree) = await ExtractAsync(archive, ["d[x]/f.txt"]);

        result.Errors.Should().BeEmpty();
        tree.Should().BeEquivalentTo(new Dictionary<string, string> { ["d[x]/f.txt"] = "bracket" });
    }
    // T-F418: bsdtar prints a stored backslash doubled ("a\b.txt"); the listing shows the name itself.
    [Integration]
    public async Task ListEntriesAsync_NameWithABackslash_ListsTheNameItself()
    {
        string archive = WriteArchive((@"a\b.txt", "backslash"), ("x\\y.txt", "two"), ("ab.txt", "plain"));

        ArchiveListResult listing = await _sut.ListEntriesAsync(archive);

        listing.Success.Should().BeTrue(listing.ErrorMessage);
        listing.Entries.Select(e => e.Path).Should().BeEquivalentTo([@"a\b.txt", "x\\y.txt", "ab.txt"]);
    }

    // T-F418: a filter is asked the name itself, so "a\b.txt" passes a filter naming it, and not ab.txt.
    [Integration]
    public async Task ExtractAsync_FilterNamingABackslashName_TakesThatEntry()
    {
        string archive = WriteArchive((@"a\b.txt", "backslash"), ("ab.txt", "plain"));
        string destDir = Path.Combine(_temp.Path, "out-" + Path.GetRandomFileName());

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            EntryFilter = (path, _) => path == @"a\b.txt",
        });

        result.Errors.Should().BeEmpty();
        Directory.GetFiles(destDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(destDir, f)).Should().Equal([@"a\b.txt"]);
    }

    // T-F418 / T-F39: the pre-scan used to see "ctl\001.txt", text with no control character in it;
    // read back, the name has one, and the archive is refused as a ZIP with that name is.
    [Integration]
    public async Task ExtractAsync_EntryNameWithAControlCharacter_IsRefusedBeforeExtraction()
    {
        string archive = WriteArchive(("ctl" + (char)1 + ".txt", "control"), ("ok.txt", "plain"));

        (ArchiveResult result, Dictionary<string, string> tree) = await ExtractAsync(archive, null);

        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("unsafe entry path");
        tree.Should().BeEmpty();
    }
}
