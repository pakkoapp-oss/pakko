using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

public sealed class ZipArchiveServiceExtractTests : IDisposable
{
    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string CreateTestZip(string zipName, params string[] fileNames)
    {
        var zipPath = Path.Combine(_temp.Path, zipName);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var name in fileNames)
            archive.CreateEntryFromFile(_temp.CreateFile(name), name);
        return zipPath;
    }

    [Fact]
    public async Task ExtractAsync_ValidZip_ExtractsFiles()
    {
        var zip = CreateTestZip("archive.zip", "file1.txt", "file2.txt");
        var destDir = Path.Combine(_temp.Path, "output");

        var options = new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = destDir
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        Directory.Exists(destDir).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_SeparateFoldersMode_CreatesSubfolderPerArchive()
    {
        var zip1 = CreateTestZip("first.zip", "a.txt");
        var zip2 = CreateTestZip("second.zip", "b.txt");
        var destDir = Path.Combine(_temp.Path, "extracted");

        var options = new ExtractOptions
        {
            ArchivePaths = [zip1, zip2],
            DestinationFolder = destDir,
            Mode = ExtractMode.SeparateFolders
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeTrue();
        Directory.Exists(Path.Combine(destDir, "first")).Should().BeTrue();
        Directory.Exists(Path.Combine(destDir, "second")).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_InvalidZipPath_ReturnsErrorNotThrows()
    {
        var options = new ExtractOptions
        {
            ArchivePaths = [@"C:\fake\archive.zip"],
            DestinationFolder = _temp.Path
        };

        var result = await _sut.ExtractAsync(options);

        result.Success.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
    }
}
