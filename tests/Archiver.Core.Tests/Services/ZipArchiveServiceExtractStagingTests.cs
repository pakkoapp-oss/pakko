using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F227: extraction used to stage into a fixed "<destDir>_tmp" folder, reuse it if it already
// existed, move everything in it into the destination and then delete it — taking a user's own
// folder of that name with it.
public sealed class ZipArchiveServiceExtractStagingTests : IDisposable
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
            w.Write("content of " + entry);
        }
        return zipPath;
    }

    private string CreateUserFile(string relativePath, string content)
    {
        string path = Path.Combine(_temp.Path, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static void AssertNoStagingLeftIn(string folder) =>
        Directory.GetDirectories(folder, ".pakko-x-*").Should().BeEmpty("the staging folder must be removed");

    [Fact]
    public async Task ExtractAsync_SeparateFolders_ExistingSiblingTmpFolder_IsLeftUntouched()
    {
        string zip = CreateZip("multi.zip", "a.txt", "b.txt");
        string note = CreateUserFile(Path.Combine("multi_tmp", "my-important-notes.txt"), "mine");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = _temp.Path,
            Mode = ExtractMode.SeparateFolders,
        });

        result.Success.Should().BeTrue();
        File.ReadAllText(note).Should().Be("mine");
        File.Exists(Path.Combine(_temp.Path, "multi", "my-important-notes.txt")).Should().BeFalse();
        File.Exists(Path.Combine(_temp.Path, "multi", "a.txt")).Should().BeTrue();
        AssertNoStagingLeftIn(_temp.Path);
    }

    [Fact]
    public async Task ExtractAsync_SingleFolder_ExistingSiblingTmpFolder_IsLeftUntouched()
    {
        string zip = CreateZip("multi.zip", "a.txt", "b.txt");
        string projects = Path.Combine(_temp.Path, "Projects");
        Directory.CreateDirectory(projects);
        string backup = CreateUserFile(Path.Combine("Projects_tmp", "backup.txt"), "mine");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = projects,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeTrue();
        File.ReadAllText(backup).Should().Be("mine");
        File.Exists(Path.Combine(projects, "backup.txt")).Should().BeFalse();
        File.Exists(Path.Combine(projects, "a.txt")).Should().BeTrue();
        AssertNoStagingLeftIn(projects);
    }

    [Fact]
    public async Task ExtractAsync_FailedRun_ExistingSiblingTmpFolder_IsLeftUntouched()
    {
        // An entry that climbs out of the staging folder fails the run after staging has begun.
        string zip = CreateZip("broken.zip", "a.txt", "../escape.txt");
        string note = CreateUserFile(Path.Combine("broken_tmp", "note.txt"), "mine");

        await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = _temp.Path,
            Mode = ExtractMode.SeparateFolders,
        });

        File.ReadAllText(note).Should().Be("mine");
        AssertNoStagingLeftIn(_temp.Path);
    }

    [Fact]
    public async Task ExtractAsync_Cancelled_LeavesNoStagingFolder()
    {
        string zip = CreateZip("multi.zip", "a.txt", "b.txt");
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(_ => cts.Cancel());

        var act = () => _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = _temp.Path,
            Mode = ExtractMode.SeparateFolders,
        }, progress, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        AssertNoStagingLeftIn(_temp.Path);
        Directory.Exists(Path.Combine(_temp.Path, "multi")).Should().BeFalse();
    }

    // The fast path renames the staging folder into place — a Hidden staging folder must not
    // turn the user's extracted folder hidden.
    [Fact]
    public async Task ExtractAsync_SeparateFolders_ExtractedFolderIsNotHidden()
    {
        string zip = CreateZip("multi.zip", "a.txt", "b.txt");

        await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = _temp.Path,
            Mode = ExtractMode.SeparateFolders,
        });

        new DirectoryInfo(Path.Combine(_temp.Path, "multi")).Attributes
            .HasFlag(FileAttributes.Hidden).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_TwoConcurrentRunsIntoSameFolder_BothComplete()
    {
        string zip1 = CreateZip("one.zip", "one-a.txt", "one-b.txt");
        string zip2 = CreateZip("two.zip", "two-a.txt", "two-b.txt");
        string dest = Path.Combine(_temp.Path, "dest");
        Directory.CreateDirectory(dest);

        ExtractOptions Options(string zip) => new()
        {
            ArchivePaths = [zip],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
        };

        var results = await Task.WhenAll(_sut.ExtractAsync(Options(zip1)), _sut.ExtractAsync(Options(zip2)));

        results.Should().OnlyContain(r => r.Success);
        foreach (string name in new[] { "one-a.txt", "one-b.txt", "two-a.txt", "two-b.txt" })
            File.Exists(Path.Combine(dest, name)).Should().BeTrue(name);
        AssertNoStagingLeftIn(dest);
    }

    private sealed class SyncProgress(Action<ProgressReport> onReport) : IProgress<ProgressReport>
    {
        public void Report(ProgressReport value) => onReport(value);
    }
}
