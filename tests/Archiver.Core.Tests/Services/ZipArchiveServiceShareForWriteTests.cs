using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F411: <see cref="ArchiveOptions.ReadFilesOpenForWriting"/> (7-Zip's -ssw). Measured on 7za
/// 26.02: a file another process holds open for writing (sharing read and write) is skipped with a
/// warning without -ssw and stored with it. Every way the ZIP engine opens a source is covered:
/// the sequential writer (few files), and the parallel one's in-memory and chunk-file paths.
/// </summary>
public sealed class ZipArchiveServiceShareForWriteTests : IDisposable
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // fileCount 1: the sequential writer; 70 (past the parallel threshold of 64): the parallel one.
    // heldSize 100: compressed in memory; 2 MiB: through a chunk file.
    private (string SourceDir, string HeldFile, byte[] HeldContent) CreateSource(int fileCount, int heldSize)
    {
        string dir = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(dir);
        for (int i = 1; i < fileCount; i++)
            File.WriteAllText(Path.Combine(dir, $"f{i:D3}.txt"), $"file {i}");
        byte[] content = new byte[heldSize];
        new Random(heldSize).NextBytes(content);
        string held = Path.Combine(dir, "held.bin");
        File.WriteAllBytes(held, content);
        return (dir, held, content);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(1, 2 * 1024 * 1024)]
    [InlineData(70, 100)]
    [InlineData(70, 2 * 1024 * 1024)]
    public async Task ArchiveAsync_FileHeldOpenForWriting_IsStoredWithTheOption(int fileCount, int heldSize)
    {
        (string dir, string held, byte[] content) = CreateSource(fileCount, heldSize);

        ArchiveResult result;
        using (new FileStream(held, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            result = await _sut.ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [dir], DestinationFolder = _temp.Path, ArchiveName = "out", ReadFilesOpenForWriting = true,
            });
        }

        result.Errors.Should().BeEmpty();
        using ZipArchive zip = ZipFile.OpenRead(result.CreatedFiles.Should().ContainSingle().Subject);
        zip.Entries.Should().HaveCount(fileCount);
        using Stream stream = zip.GetEntry("src/held.bin")!.Open();
        using var read = new MemoryStream();
        stream.CopyTo(read);
        read.ToArray().Should().Equal(content);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(70, 2 * 1024 * 1024)]
    public async Task ArchiveAsync_FileHeldOpenForWriting_IsAnErrorWithoutTheOption(int fileCount, int heldSize)
    {
        (string dir, string held, _) = CreateSource(fileCount, heldSize);

        ArchiveResult result;
        using (new FileStream(held, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            result = await _sut.ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [dir], DestinationFolder = _temp.Path, ArchiveName = "out",
            });
        }

        result.Errors.Should().ContainSingle(e => e.SourcePath == held);
    }

    // A file nobody may read stays unreadable: the option shares writing, never takes exclusivity away.
    [Fact]
    public async Task ArchiveAsync_FileLockedExclusively_IsStillAnErrorWithTheOption()
    {
        (string dir, string held, _) = CreateSource(1, 100);

        ArchiveResult result;
        using (new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = await _sut.ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [dir], DestinationFolder = _temp.Path, ArchiveName = "out", ReadFilesOpenForWriting = true,
            });
        }

        result.Errors.Should().ContainSingle(e => e.SourcePath == held);
    }
}
