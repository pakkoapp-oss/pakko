using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

// T-F408: ExtractOptions.OnItemExtracted through the real tar.exe, as Archiver.Core.Tests'
// ExtractedItemReportTests does for ZIP: what is named is exactly what landed in the destination.
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceExtractedItemReportTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static TarBuilder.Entry File(string name) => new() { Name = name, Content = Encoding.ASCII.GetBytes(name) };
    private static TarBuilder.Entry Dir(string name) => new() { Name = name, TypeFlag = '5' };

    [Integration]
    public async Task ExtractAsync_SingleFolder_NamesExactlyWhatLandsInTheDestination()
    {
        (List<string> named, string dest) = await ExtractAsync(ExtractMode.SingleFolder, ConflictBehavior.Skip, preexisting: null);

        named.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(OnDisk(dest));
        named.Should().Contain(Path.Combine(dest, "pkg", "sub", "b.txt"));
    }

    [Integration]
    public async Task ExtractAsync_SeparateFolders_NamesTheNewFolderToo()
    {
        (List<string> named, string dest) = await ExtractAsync(ExtractMode.SeparateFolders, ConflictBehavior.Skip, preexisting: null);

        named.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(OnDisk(dest));
    }

    [Integration]
    public async Task ExtractAsync_FileSkippedBecauseItExists_IsNotNamed()
    {
        (List<string> named, string dest) = await ExtractAsync(ExtractMode.SingleFolder, ConflictBehavior.Skip, preexisting: Path.Combine("pkg", "a.txt"));

        named.Should().NotContain(Path.Combine(dest, "pkg", "a.txt")).And.Contain(Path.Combine(dest, "pkg", "sub", "b.txt"));
        System.IO.File.ReadAllText(Path.Combine(dest, "pkg", "a.txt")).Should().Be("mine");
    }

    private async Task<(List<string> Named, string Dest)> ExtractAsync(ExtractMode mode, ConflictBehavior onConflict, string? preexisting)
    {
        string archivePath = Path.Combine(_temp.Path, "pkg.tar");
        TarBuilder.WriteTar(archivePath, [Dir("pkg/"), File("pkg/a.txt"), Dir("pkg/sub/"), File("pkg/sub/b.txt"), Dir("pkg/empty/")]);
        string dest = Path.Combine(_temp.Path, "dest");
        Directory.CreateDirectory(dest);
        if (preexisting is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dest, preexisting))!);
            System.IO.File.WriteAllText(Path.Combine(dest, preexisting), "mine");
        }
        var named = new List<string>();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath], DestinationFolder = dest, Mode = mode, OnConflict = onConflict, OnItemExtracted = named.Add,
        });

        result.Errors.Should().BeEmpty();
        return (named, dest);
    }

    // Every folder (with a trailing separator) and file under the destination that this run wrote.
    private static IEnumerable<string> OnDisk(string dest) =>
        Directory.EnumerateDirectories(dest, "*", SearchOption.AllDirectories).Select(d => d + Path.DirectorySeparatorChar)
            .Concat(Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories));
}
