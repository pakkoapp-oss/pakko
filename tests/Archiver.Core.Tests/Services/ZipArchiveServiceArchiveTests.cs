using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

public sealed class ZipArchiveServiceArchiveTests : IDisposable
{
    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ArchiveAsync_SingleFile_CreatesZip()
    {
        var file = _temp.CreateFile("document.txt");
        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "output"
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(1);
        File.Exists(result.CreatedFiles[0]).Should().BeTrue();
        result.CreatedFiles[0].Should().EndWith(".zip");
    }

    [Fact]
    public async Task ArchiveAsync_MultipleFiles_SingleArchiveMode_CreatesOneZip()
    {
        var file1 = _temp.CreateFile("a.txt");
        var file2 = _temp.CreateFile("b.txt");
        var options = new ArchiveOptions
        {
            SourcePaths = [file1, file2],
            DestinationFolder = _temp.Path,
            ArchiveName = "combined",
            Mode = ArchiveMode.SingleArchive
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(1);
    }

    [Fact]
    public async Task ArchiveAsync_MultipleFiles_SeparateArchivesMode_CreatesMultipleZips()
    {
        var file1 = _temp.CreateFile("a.txt");
        var file2 = _temp.CreateFile("b.txt");
        var options = new ArchiveOptions
        {
            SourcePaths = [file1, file2],
            DestinationFolder = _temp.Path,
            Mode = ArchiveMode.SeparateArchives
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(2);
    }

    [Fact]
    public async Task ArchiveAsync_NonExistentFile_ReturnsErrorNotThrows()
    {
        var options = new ArchiveOptions
        {
            SourcePaths = [@"C:\does\not\exist.txt"],
            DestinationFolder = _temp.Path
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].SourcePath.Should().Be(@"C:\does\not\exist.txt");
        result.Errors[0].Message.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_CancellationRequested_StopsProcessing()
    {
        var files = Enumerable.Range(1, 10)
            .Select(i => _temp.CreateFile($"file{i}.txt"))
            .ToList();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var options = new ArchiveOptions
        {
            SourcePaths = files,
            DestinationFolder = _temp.Path,
            Mode = ArchiveMode.SeparateArchives
        };

        var result = await _sut.ArchiveAsync(options, cancellationToken: cts.Token);

        // Should process 0 or fewer than 10 items
        result.CreatedFiles.Count.Should().BeLessThan(10);
    }

    [Fact]
    public async Task ArchiveAsync_ReportsProgress()
    {
        var files = Enumerable.Range(1, 5)
            .Select(i => _temp.CreateFile($"file{i}.txt"))
            .ToList();

        var progressValues = new List<int>();
        var progress = new Progress<int>(v => progressValues.Add(v));

        var options = new ArchiveOptions
        {
            SourcePaths = files,
            DestinationFolder = _temp.Path,
            Mode = ArchiveMode.SeparateArchives
        };

        await _sut.ArchiveAsync(options, progress);
        await Task.Delay(50); // let Progress callbacks fire

        progressValues.Should().NotBeEmpty();
        progressValues.Last().Should().Be(100);
    }
}
