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
    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string CreateZip(string name, params string[] entries)
    {
        string zipPath = Path.Combine(_temp.Path, name);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
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
        var result = await _sut.ExtractAsync(new ExtractOptions
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
}
