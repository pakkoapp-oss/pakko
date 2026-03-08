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
    public async Task ArchiveAsync_DeleteSourceFiles_SucceedsWithoutDeletingSource()
    {
        // Deletion is now handled by MainViewModel (RunCleanupAsync), not the service.
        // The service must accept the option and complete successfully; source is NOT deleted.
        var file = _temp.CreateFile("to-delete.txt");
        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "output",
            DeleteSourceFiles = true
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(1);
        File.Exists(file).Should().BeTrue(); // service no longer deletes — ViewModel does
    }

    [Fact]
    public async Task ArchiveAsync_ConflictSkip_DoesNotOverwriteExistingZip()
    {
        var file = _temp.CreateFile("source.txt");
        var existingZip = _temp.CreateFile("output.zip");
        var originalWriteTime = File.GetLastWriteTimeUtc(existingZip);

        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "output",
            OnConflict = ConflictBehavior.Skip
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().BeEmpty();
        File.GetLastWriteTimeUtc(existingZip).Should().Be(originalWriteTime);
    }

    [Fact]
    public async Task ArchiveAsync_ConflictRename_CreatesNumberedZipWhenOutputExists()
    {
        var file = _temp.CreateFile("source.txt");
        _temp.CreateFile("output.zip");

        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "output",
            OnConflict = ConflictBehavior.Rename
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(1);
        result.CreatedFiles[0].Should().EndWith("output (1).zip");
        File.Exists(result.CreatedFiles[0]).Should().BeTrue();
    }

    [Fact]
    public async Task ArchiveAsync_ConflictOverwrite_ReplacesExistingZip()
    {
        var file = _temp.CreateFile("source.txt");
        var existingZip = _temp.CreateFile("output.zip");

        var options = new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "output",
            OnConflict = ConflictBehavior.Overwrite
        };

        var result = await _sut.ArchiveAsync(options);

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(1);
        result.CreatedFiles[0].Should().Be(existingZip);
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

    [Fact]
    public async Task ArchiveAsync_CancelMidArchive_NoUnhandledException()
    {
        // Create 3 files with ~64 KB each so the operation takes measurable time
        string largeContent = new string('x', 64 * 1024);
        var files = Enumerable.Range(1, 3)
            .Select(i => _temp.CreateFile($"large{i}.txt", largeContent))
            .ToList();

        using var destDir = new TempDirectory();
        using var cts = new CancellationTokenSource();

        var options = new ArchiveOptions
        {
            SourcePaths = files,
            DestinationFolder = destDir.Path,
            ArchiveName = "cancel_test",
            Mode = ArchiveMode.SingleArchive
        };

        // Cancel after a short delay — may fire before, during, or after the operation
        _ = Task.Delay(5).ContinueWith(_ => cts.Cancel());

        ArchiveResult? result = null;
        try
        {
            result = await _sut.ArchiveAsync(options, cancellationToken: cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation fires mid-file via CopyToAsync
        }

        // If we got a result it should have no errors (completed before cancel or cancel was a no-op)
        result?.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_Cancelled_LeavesNoTempFile()
    {
        var file1 = _temp.CreateFile("a.txt", "content a");
        var file2 = _temp.CreateFile("b.txt", "content b");
        var file3 = _temp.CreateFile("c.txt", "content c");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var options = new ArchiveOptions
        {
            SourcePaths = [file1, file2, file3],
            DestinationFolder = _temp.Path,
            ArchiveName = "cancelled_output",
            Mode = ArchiveMode.SingleArchive
        };

        try
        {
            await _sut.ArchiveAsync(options, cancellationToken: cts.Token);
        }
        catch (OperationCanceledException) { }

        Directory.GetFiles(_temp.Path, "*.tmp").Should().BeEmpty();
    }
}
