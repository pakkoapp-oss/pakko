using System.IO.Compression;
using System.Runtime.Versioning;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F236: one unreadable subfolder used to abort the whole archive on the parallel path (the
// producer's enumeration threw) and drop the rest of the top-level source on the sequential path.
// It must become one per-item error while every readable file is still archived — and the source
// must not count as Completed, so "Delete after operation" never recycles it.
[SupportedOSPlatform("windows")]
public sealed class ZipArchiveServiceUnreadableFolderTests : IDisposable
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // Files on both sides of the locked folder in sort order, so an abort at the folder would
    // lose the later ones.
    private (string SourceDir, string LockedDir) BuildTree(int filesPerSide)
    {
        string source = Path.Combine(_temp.Path, "src");
        string locked = Path.Combine(source, "m_locked");
        Directory.CreateDirectory(locked);
        File.WriteAllText(Path.Combine(locked, "hidden.txt"), "unreadable");
        for (int i = 0; i < filesPerSide; i++)
        {
            File.WriteAllText(Path.Combine(source, $"a{i:D3}.txt"), $"before {i}");
            Directory.CreateDirectory(Path.Combine(source, "z_after"));
            File.WriteAllText(Path.Combine(source, "z_after", $"z{i:D3}.txt"), $"after {i}");
        }
        return (source, locked);
    }

    private static List<string> EntryNames(string zipPath)
    {
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        return [.. zip.Entries.Select(e => e.FullName)];
    }

    [Theory]
    [InlineData(2)]   // sequential writer (<= 64 files)
    [InlineData(40)]  // parallel writer (> 64 files)
    public async Task ArchiveAsync_SingleArchive_UnreadableSubfolder_OneErrorAndEveryReadableFileArchived(int filesPerSide)
    {
        (string source, string locked) = BuildTree(filesPerSide);
        ArchiveResult result;
        using (new DeniedFolder(locked))
        {
            result = await _sut.ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [source],
                DestinationFolder = _temp.Path,
                ArchiveName = "out",
            });
        }

        result.Errors.Should().ContainSingle().Which.SourcePath.Should().Be(locked);
        result.CreatedFiles.Should().ContainSingle();
        List<string> names = EntryNames(result.CreatedFiles[0]);
        names.Should().HaveCount(filesPerSide * 2);
        names.Should().Contain($"src/a{filesPerSide - 1:D3}.txt").And.Contain($"src/z_after/z{filesPerSide - 1:D3}.txt");
        names.Should().NotContain(n => n.Contains("hidden.txt"));
        result.Sources.Should().ContainSingle().Which.Outcome.Should().NotBe(SourceOutcome.Completed);
    }

    [Fact]
    public async Task ArchiveAsync_SeparateArchives_UnreadableSubfolder_OneErrorAndSourceNotCompleted()
    {
        (string source, string locked) = BuildTree(2);
        string other = _temp.CreateFile("other.txt", "fine");
        ArchiveResult result;
        using (new DeniedFolder(locked))
        {
            result = await _sut.ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [source, other],
                DestinationFolder = _temp.Path,
                Mode = ArchiveMode.SeparateArchives,
            });
        }

        result.Errors.Should().ContainSingle().Which.SourcePath.Should().Be(locked);
        result.CreatedFiles.Should().HaveCount(2);
        EntryNames(Path.Combine(_temp.Path, "src.zip")).Should().HaveCount(4);
        result.Sources.Single(s => s.Path == source).Outcome.Should().NotBe(SourceOutcome.Completed);
        result.Sources.Single(s => s.Path == other).Outcome.Should().Be(SourceOutcome.Completed);
    }

    [Theory]
    [InlineData(ArchiveMode.SingleArchive)]
    [InlineData(ArchiveMode.SeparateArchives)]
    public async Task ArchiveAsync_TopLevelSourceUnreadable_ErrorForItAndOtherSourceArchived(ArchiveMode mode)
    {
        string locked = Path.Combine(_temp.Path, "locked");
        Directory.CreateDirectory(locked);
        File.WriteAllText(Path.Combine(locked, "hidden.txt"), "unreadable");
        string other = _temp.CreateFile("other.txt", "fine");
        ArchiveResult result;
        using (new DeniedFolder(locked))
        {
            result = await _sut.ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [locked, other],
                DestinationFolder = _temp.Path,
                ArchiveName = "out",
                Mode = mode,
            });
        }

        result.Errors.Should().ContainSingle().Which.SourcePath.Should().Be(locked);
        result.CreatedFiles.Should().NotBeEmpty();
        result.CreatedFiles.SelectMany(EntryNames).Should().Contain("other.txt").And.NotContain(n => n.Contains("hidden.txt"));
    }
}
