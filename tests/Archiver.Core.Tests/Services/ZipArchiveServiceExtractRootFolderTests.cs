using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F205: SingleFolder mode dropped an archive's only root folder. It now keeps it ("extract with
// full paths", like 7-Zip/NanaZip "Extract here" and `7z x`); "Extract to <name>\" strips the
// root only when it is named like the archive (NanaZip's default ElimDup).
public sealed class ZipArchiveServiceExtractRootFolderTests : IDisposable
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string CreateZip(string name, params string[] entries)
    {
        string zipPath = Path.Combine(_temp.Path, name);
        using ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (string entry in entries)
        {
            using var w = new StreamWriter(archive.CreateEntry(entry).Open());
            w.Write("x");
        }
        return zipPath;
    }

    private async Task<string> ExtractAsync(string zip, ExtractMode mode, bool eliminateDuplicateRoot = false)
    {
        string dest = Path.Combine(_temp.Path, "dest");
        Directory.CreateDirectory(dest);
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = dest,
            Mode = mode,
            EliminateDuplicateRootFolder = eliminateDuplicateRoot,
        });
        result.Success.Should().BeTrue();
        return dest;
    }

    [Fact]
    public async Task ExtractAsync_SingleFolder_KeepsTheArchivesRootFolder()
    {
        string zip = CreateZip("singleroot.zip", "root/in/c.txt", "root/d.txt");

        string dest = await ExtractAsync(zip, ExtractMode.SingleFolder);

        File.Exists(Path.Combine(dest, "root", "in", "c.txt")).Should().BeTrue();
        File.Exists(Path.Combine(dest, "root", "d.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_EliminateDuplicateRoot_RootNamedLikeArchive_IsStripped()
    {
        string zip = CreateZip("root.zip", "root/in/c.txt");

        string dest = await ExtractAsync(zip, ExtractMode.SingleFolder, eliminateDuplicateRoot: true);

        File.Exists(Path.Combine(dest, "in", "c.txt")).Should().BeTrue();
        Directory.Exists(Path.Combine(dest, "root")).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_EliminateDuplicateRoot_RootNamedDifferently_IsKept()
    {
        string zip = CreateZip("singleroot.zip", "root/in/c.txt");

        string dest = await ExtractAsync(zip, ExtractMode.SingleFolder, eliminateDuplicateRoot: true);

        File.Exists(Path.Combine(dest, "root", "in", "c.txt")).Should().BeTrue();
    }

    // "Extract here (smart)" is unchanged: the archive-named folder stands in for the root.
    [Fact]
    public async Task ExtractAsync_SeparateFolders_StillReplacesTheRootWithTheArchiveFolder()
    {
        string zip = CreateZip("singleroot.zip", "root/in/c.txt");

        string dest = await ExtractAsync(zip, ExtractMode.SeparateFolders);

        File.Exists(Path.Combine(dest, "singleroot", "in", "c.txt")).Should().BeTrue();
    }

    // --- T-F411: DuplicateRootName (7-Zip's -spe: the root named like -o's last part) ---

    private async Task<string> ExtractNamedRootAsync(
        string zip, string rootName, Func<string, bool, bool>? filter = null, IReadOnlyList<string>? selected = null)
    {
        string dest = Path.Combine(_temp.Path, "dest");
        Directory.CreateDirectory(dest);
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            DuplicateRootName = rootName, EntryFilter = filter, SelectedEntryPaths = selected,
        });
        result.Errors.Should().BeEmpty();
        return dest;
    }

    [Theory]
    [InlineData("src")]
    [InlineData("SRC")]
    public async Task ExtractAsync_DuplicateRootName_RootNamedSo_IsStripped(string rootName)
    {
        string zip = CreateZip("any.zip", "src/a.txt", "src/d/b.txt");

        string dest = await ExtractNamedRootAsync(zip, rootName);

        File.Exists(Path.Combine(dest, "a.txt")).Should().BeTrue();
        File.Exists(Path.Combine(dest, "d", "b.txt")).Should().BeTrue();
        Directory.Exists(Path.Combine(dest, "src")).Should().BeFalse();
    }

    [Theory]
    [InlineData("other")]
    [InlineData(".")]
    [InlineData("sr")]
    public async Task ExtractAsync_DuplicateRootName_RootNamedOtherwise_IsKept(string rootName)
    {
        string zip = CreateZip("src.zip", "src/a.txt");

        string dest = await ExtractNamedRootAsync(zip, rootName);

        File.Exists(Path.Combine(dest, "src", "a.txt")).Should().BeTrue();
    }

    // 7za: a file named like -o's last part is not a folder to drop.
    [Fact]
    public async Task ExtractAsync_DuplicateRootName_RootIsAFile_IsKept()
    {
        string zip = CreateZip("f.zip", "src");

        string dest = await ExtractNamedRootAsync(zip, "src");

        File.Exists(Path.Combine(dest, "src")).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_DuplicateRootName_SecondRoot_IsKept()
    {
        string zip = CreateZip("two.zip", "src/a.txt", "x.txt");

        string dest = await ExtractNamedRootAsync(zip, "src");

        File.Exists(Path.Combine(dest, "src", "a.txt")).Should().BeTrue();
        File.Exists(Path.Combine(dest, "x.txt")).Should().BeTrue();
    }

    // 7za: -spe with -x!/-i! still drops the root, decided over every entry of the archive.
    [Fact]
    public async Task ExtractAsync_DuplicateRootName_WithAFilter_IsStripped()
    {
        string zip = CreateZip("any.zip", "src/a.txt", "src/d/b.txt");

        string dest = await ExtractNamedRootAsync(zip, "src", filter: (name, _) => name != "src/a.txt");

        File.Exists(Path.Combine(dest, "d", "b.txt")).Should().BeTrue();
        File.Exists(Path.Combine(dest, "a.txt")).Should().BeFalse();
        Directory.Exists(Path.Combine(dest, "src")).Should().BeFalse();
    }

    // The filter cannot make a second root disappear: an entry it leaves out still counts.
    [Fact]
    public async Task ExtractAsync_DuplicateRootName_FilterLeavesOutTheSecondRoot_IsKept()
    {
        string zip = CreateZip("two.zip", "src/a.txt", "x.txt");

        string dest = await ExtractNamedRootAsync(zip, "src", filter: (name, _) => name != "x.txt");

        File.Exists(Path.Combine(dest, "src", "a.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_DuplicateRootName_WithASelection_IsStripped()
    {
        string zip = CreateZip("any.zip", "src/a.txt", "src/d/b.txt");

        string dest = await ExtractNamedRootAsync(zip, "src", selected: ["src/d"]);

        File.Exists(Path.Combine(dest, "d", "b.txt")).Should().BeTrue();
        File.Exists(Path.Combine(dest, "a.txt")).Should().BeFalse();
    }
}
