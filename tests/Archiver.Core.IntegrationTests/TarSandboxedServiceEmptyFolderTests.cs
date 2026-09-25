using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

// T-F197: the tar-family move phase moved files only, so an empty folder in the archive never
// reached the destination (see ZipArchiveServiceExtractEmptyFolderTests for ZIP).
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceEmptyFolderTests : IDisposable
{
    private readonly TarSandboxedService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static TarBuilder.Entry File(string name) => new() { Name = name, Content = Encoding.ASCII.GetBytes("x") };
    private static TarBuilder.Entry Dir(string name) => new() { Name = name, TypeFlag = '5' };

    private async Task<(ArchiveResult Result, string Dest)> ExtractAsync(string tarName, ExtractMode mode, params TarBuilder.Entry[] entries)
    {
        string archivePath = Path.Combine(_temp.Path, tarName);
        TarBuilder.WriteTar(archivePath, entries);
        string dest = Path.Combine(_temp.Path, "dest");
        Directory.CreateDirectory(dest);
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = dest,
            Mode = mode,
        });
        result.Success.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        return (result, dest);
    }

    [Integration]
    public async Task ExtractAsync_SingleFolder_EmptyFoldersTopLevelAndNested_AreCreated()
    {
        var (_, dest) = await ExtractAsync("tree.tar", ExtractMode.SingleFolder,
            Dir("root/"), File("root/a.txt"), Dir("root/empty/"), Dir("root/nested/"), Dir("root/nested/deeper/"));

        System.IO.File.Exists(Path.Combine(dest, "root", "a.txt")).Should().BeTrue();
        Directory.Exists(Path.Combine(dest, "root", "empty")).Should().BeTrue();
        Directory.Exists(Path.Combine(dest, "root", "nested", "deeper")).Should().BeTrue();
    }

    [Integration]
    public async Task ExtractAsync_SeparateFolders_EmptyFolderUnderStrippedRoot_IsCreated()
    {
        var (_, dest) = await ExtractAsync("tree.tar", ExtractMode.SeparateFolders,
            File("./root/a.txt"), Dir("./root/empty/"));

        System.IO.File.Exists(Path.Combine(dest, "tree", "a.txt")).Should().BeTrue();
        Directory.Exists(Path.Combine(dest, "tree", "empty")).Should().BeTrue();
    }

    [Integration]
    public async Task ExtractAsync_ArchiveOfOnlyEmptyFolders_CreatesThem()
    {
        var (_, dest) = await ExtractAsync("dirs.tar", ExtractMode.SingleFolder, Dir("e1/"), Dir("e2/sub/"));

        Directory.Exists(Path.Combine(dest, "e1")).Should().BeTrue();
        Directory.Exists(Path.Combine(dest, "e2", "sub")).Should().BeTrue();
    }

    [Integration]
    public async Task ExtractAsync_SeparateFolders_RootFilePlusEmptyFolder_IsMultiRoot()
    {
        var (_, dest) = await ExtractAsync("pair.tar", ExtractMode.SeparateFolders, File("a.txt"), Dir("empty/"));

        System.IO.File.Exists(Path.Combine(dest, "pair", "a.txt")).Should().BeTrue();
        Directory.Exists(Path.Combine(dest, "pair", "empty")).Should().BeTrue();
    }
}
