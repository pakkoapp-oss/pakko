using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F197: Pakko writes an empty folder as a "name/" entry (T-F66), but extraction filtered
// directory entries out, so the folder never came back.
public sealed class ZipArchiveServiceExtractEmptyFolderTests : IDisposable
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
            var e = archive.CreateEntry(entry);
            if (!entry.EndsWith('/'))
            {
                using var w = new StreamWriter(e.Open());
                w.Write("x");
            }
        }
        return zipPath;
    }

    private async Task<(ArchiveResult Result, string Dest)> ExtractAsync(string zip, ExtractMode mode)
    {
        string dest = Path.Combine(_temp.Path, "dest");
        Directory.CreateDirectory(dest);
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = dest,
            Mode = mode,
        });
        return (result, dest);
    }

    [Theory]
    [InlineData(ExtractMode.SingleFolder, "src")]
    [InlineData(ExtractMode.SeparateFolders, "out")]
    public async Task ArchiveThenExtract_EmptyFoldersTopLevelAndNested_SurviveTheRoundTrip(ExtractMode mode, string rootOnDisk)
    {
        string src = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(Path.Combine(src, "empty"));
        Directory.CreateDirectory(Path.Combine(src, "nested", "deeper"));
        File.WriteAllText(Path.Combine(src, "f.txt"), "file");
        var archived = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src],
            DestinationFolder = _temp.Path,
            ArchiveName = "out",
        });
        archived.Success.Should().BeTrue();

        var (result, dest) = await ExtractAsync(archived.CreatedFiles[0], mode);

        result.Success.Should().BeTrue();
        string root = Path.Combine(dest, rootOnDisk);
        File.Exists(Path.Combine(root, "f.txt")).Should().BeTrue();
        Directory.Exists(Path.Combine(root, "empty")).Should().BeTrue();
        Directory.Exists(Path.Combine(root, "nested", "deeper")).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_ArchiveOfOnlyEmptyFolders_CreatesThem()
    {
        string zip = CreateZip("dirs.zip", "e1/", "e2/sub/");

        var (result, dest) = await ExtractAsync(zip, ExtractMode.SingleFolder);

        result.Success.Should().BeTrue();
        Directory.Exists(Path.Combine(dest, "e1")).Should().BeTrue();
        Directory.Exists(Path.Combine(dest, "e2", "sub")).Should().BeTrue();
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Completed);
    }

    // A root file next to an empty folder is two roots, not a single file: the folder must not be
    // lost to the single-file unwrap (T-F154).
    [Fact]
    public async Task ExtractAsync_SeparateFolders_RootFilePlusEmptyFolder_IsMultiRoot()
    {
        string zip = CreateZip("pair.zip", "a.txt", "empty/");

        var (result, dest) = await ExtractAsync(zip, ExtractMode.SeparateFolders);

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(dest, "pair", "a.txt")).Should().BeTrue();
        Directory.Exists(Path.Combine(dest, "pair", "empty")).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_SingleRootWithEmptySiblingFolder_KeepsBothUnderStrip()
    {
        string zip = CreateZip("root.zip", "root/a.txt", "root/empty/");

        var (result, dest) = await ExtractAsync(zip, ExtractMode.SeparateFolders);

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(dest, "root", "a.txt")).Should().BeTrue();
        Directory.Exists(Path.Combine(dest, "root", "empty")).Should().BeTrue();
    }

    // T-F87 must still hold with folder entries: a folder that already exists is not "extracted".
    [Fact]
    public async Task ExtractAsync_SecondRunWithSkip_ReportsNothingExtracted()
    {
        string zip = CreateZip("again.zip", "dir/", "dir/a.txt");
        string dest = Path.Combine(_temp.Path, "dest");
        ExtractOptions Options() => new()
        {
            ArchivePaths = [zip],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Skip,
        };
        (await _sut.ExtractAsync(Options())).CreatedFiles.Should().NotBeEmpty();

        var second = await _sut.ExtractAsync(Options());

        second.CreatedFiles.Should().BeEmpty();
        second.SkippedFiles.Should().Contain(s => s.Path == zip);
    }

    [Theory]
    [InlineData("../evil/")]
    [InlineData("CON/")]
    public async Task ExtractAsync_UnsafeFolderEntry_IsNotCreated(string unsafeFolder)
    {
        string zip = CreateZip("bad.zip", "ok.txt", unsafeFolder);

        var (result, dest) = await ExtractAsync(zip, ExtractMode.SingleFolder);

        File.Exists(Path.Combine(dest, "ok.txt")).Should().BeTrue();
        Directory.Exists(Path.Combine(_temp.Path, "evil")).Should().BeFalse();
        Directory.GetDirectories(dest).Should().BeEmpty();
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Partial);
    }
}
