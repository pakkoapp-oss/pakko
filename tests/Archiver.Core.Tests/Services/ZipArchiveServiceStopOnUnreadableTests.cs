using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F411: <see cref="ArchiveOptions.StopOnUnreadableSource"/> (7-Zip's -sse). Measured on 7za
/// 26.02: a file that cannot be opened is exit 2 and no archive with -sse; without it a warning and
/// the archive of the rest. The ZIP engine's three writers are covered: sequential, parallel, and
/// one archive per source.
/// </summary>
public sealed class ZipArchiveServiceStopOnUnreadableTests : IDisposable
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private (string SourceDir, string LockedFile, string DestDir) CreateSource(int fileCount)
    {
        string dir = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(dir);
        for (int i = 0; i < fileCount; i++)
            File.WriteAllText(Path.Combine(dir, $"f{i:D3}.txt"), $"file {i}");
        string dest = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(dest);
        return (dir, Path.Combine(dir, "f000.txt"), dest);
    }

    private async Task<ArchiveResult> ArchiveWhileLockedAsync(string lockedFile, ArchiveOptions options)
    {
        using var hold = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        return await _sut.ArchiveAsync(options);
    }

    [Theory]
    [InlineData(3)]  // the sequential writer
    [InlineData(70)] // the parallel one (past 64 files)
    public async Task ArchiveAsync_UnreadableFile_WritesNoArchive(int fileCount)
    {
        (string dir, string locked, string dest) = CreateSource(fileCount);

        ArchiveResult result = await ArchiveWhileLockedAsync(locked, new ArchiveOptions
        {
            SourcePaths = [dir], DestinationFolder = dest, ArchiveName = "a", StopOnUnreadableSource = true,
        });

        result.CreatedFiles.Should().BeEmpty();
        result.Errors.Should().ContainSingle(e => e.SourcePath == locked);
        result.Outcome.Should().Be(OperationOutcome.Failed);
        Directory.EnumerateFileSystemEntries(dest).Should().BeEmpty("neither the archive nor its temporary file stays");
    }

    [Fact]
    public async Task ArchiveAsync_UnreadableFileFastest_WritesNoArchive()
    {
        (string dir, string locked, string dest) = CreateSource(3);

        ArchiveResult result = await ArchiveWhileLockedAsync(locked, new ArchiveOptions
        {
            SourcePaths = [dir], DestinationFolder = dest, ArchiveName = "a", StopOnUnreadableSource = true,
            CompressionLevel = CompressionLevel.Fastest, // the hand-rolled writer (T-F299)
        });

        result.CreatedFiles.Should().BeEmpty();
        Directory.EnumerateFileSystemEntries(dest).Should().BeEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_SeparateArchives_UnreadableFile_WritesNoArchiveForItsSource()
    {
        (string dir, string locked, string dest) = CreateSource(3);
        string other = Path.Combine(_temp.Path, "other.txt");
        File.WriteAllText(other, "other");

        ArchiveResult result = await ArchiveWhileLockedAsync(locked, new ArchiveOptions
        {
            SourcePaths = [dir, other], DestinationFolder = dest, Mode = ArchiveMode.SeparateArchives,
            StopOnUnreadableSource = true,
        });

        result.CreatedFiles.Select(Path.GetFileName).Should().Equal("other.zip");
        File.Exists(Path.Combine(dest, "src.zip")).Should().BeFalse();
    }

    // -y over an archive that is there: the old one stays as it was, byte for byte.
    [Fact]
    public async Task ArchiveAsync_UnreadableFile_KeepsTheExistingArchive()
    {
        (string dir, string locked, string dest) = CreateSource(3);
        string existing = Path.Combine(dest, "a.zip");
        byte[] oldBytes = [0x50, 0x4B, 0x05, 0x06, .. new byte[18]];
        File.WriteAllBytes(existing, oldBytes);

        ArchiveResult result = await ArchiveWhileLockedAsync(locked, new ArchiveOptions
        {
            SourcePaths = [dir], DestinationFolder = dest, ArchiveName = "a", OnConflict = ConflictBehavior.Overwrite,
            StopOnUnreadableSource = true,
        });

        result.CreatedFiles.Should().BeEmpty();
        File.ReadAllBytes(existing).Should().Equal(oldBytes);
        Directory.EnumerateFileSystemEntries(dest).Should().ContainSingle();
    }

    [Fact]
    public async Task ArchiveAsync_UnreadableFileWithoutTheOption_ArchivesTheRest()
    {
        (string dir, string locked, string dest) = CreateSource(3);

        ArchiveResult result = await ArchiveWhileLockedAsync(locked, new ArchiveOptions
        {
            SourcePaths = [dir], DestinationFolder = dest, ArchiveName = "a",
        });

        result.Errors.Should().ContainSingle(e => e.SourcePath == locked);
        using ZipArchive zip = ZipFile.OpenRead(result.CreatedFiles.Should().ContainSingle().Subject);
        zip.Entries.Should().HaveCount(2);
    }

    // A link is left out as always (a skip, not a file that failed to open), so the archive is written.
    [Fact]
    public async Task ArchiveAsync_LinkLeftOut_StillWritesTheArchive()
    {
        (string dir, _, string dest) = CreateSource(2);
        string outside = Path.Combine(_temp.Path, "outside");
        Directory.CreateDirectory(outside);
        string junction = Path.Combine(dir, "j");
        if (!ZipArchiveServiceArchiveTests.TryCreateJunction(junction, outside))
            return; // no junctions on this volume
        try
        {
            ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [dir], DestinationFolder = dest, ArchiveName = "a", StopOnUnreadableSource = true,
            });

            result.Errors.Should().BeEmpty();
            result.CreatedFiles.Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(junction);
        }
    }
}
