using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

// T-F205: the tar-family engine shares ExtractionDestinationPlanner with ZIP — same root-folder
// rules (see ZipArchiveServiceExtractRootFolderTests).
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceRootFolderTests : IDisposable
{
    private readonly TarSandboxedService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string CreateTar(string name, params string[] entries)
    {
        string archivePath = Path.Combine(_temp.Path, name);
        TarBuilder.WriteTar(archivePath,
            [.. entries.Select(e => new TarBuilder.Entry { Name = e, Content = Encoding.ASCII.GetBytes("x") })]);
        return archivePath;
    }

    private async Task<string> ExtractAsync(string archive, ExtractMode mode, bool eliminateDuplicateRoot = false)
    {
        string dest = Path.Combine(_temp.Path, "dest");
        Directory.CreateDirectory(dest);
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive],
            DestinationFolder = dest,
            Mode = mode,
            EliminateDuplicateRootFolder = eliminateDuplicateRoot,
        });
        result.Success.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        return dest;
    }

    [Integration]
    public async Task ExtractAsync_SingleFolder_KeepsTheArchivesRootFolder()
    {
        string tar = CreateTar("singleroot.tar", "root/in/c.txt", "root/d.txt");

        string dest = await ExtractAsync(tar, ExtractMode.SingleFolder);

        File.Exists(Path.Combine(dest, "root", "in", "c.txt")).Should().BeTrue();
        File.Exists(Path.Combine(dest, "root", "d.txt")).Should().BeTrue();
    }

    [Integration]
    public async Task ExtractAsync_EliminateDuplicateRoot_DotSlashRootNamedLikeArchive_IsStripped()
    {
        string tar = CreateTar("root.tar", "./root/in/c.txt");

        string dest = await ExtractAsync(tar, ExtractMode.SingleFolder, eliminateDuplicateRoot: true);

        File.Exists(Path.Combine(dest, "in", "c.txt")).Should().BeTrue();
        Directory.Exists(Path.Combine(dest, "root")).Should().BeFalse();
    }

    [Integration]
    public async Task ExtractAsync_EliminateDuplicateRoot_RootNamedDifferently_IsKept()
    {
        string tar = CreateTar("singleroot.tar", "root/in/c.txt");

        string dest = await ExtractAsync(tar, ExtractMode.SingleFolder, eliminateDuplicateRoot: true);

        File.Exists(Path.Combine(dest, "root", "in", "c.txt")).Should().BeTrue();
    }
}
