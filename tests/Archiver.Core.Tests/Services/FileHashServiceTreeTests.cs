using System.Runtime.Versioning;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F251: hashing a folder threw out of ComputeAsync on an unreadable subfolder (the one
// enumeration ran outside any try) and walked a junction loop until PathTooLongException; a
// junction to an outside folder silently added foreign files. And the parallel CRC-32 path
// returned a CRC for a file that came up short, as if it were complete.
[SupportedOSPlatform("windows")]
public sealed class FileHashServiceTreeTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string MakeFolder(string name)
    {
        string root = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "a.txt"), "hello world");
        return root;
    }

    [Fact]
    public async Task ComputeAsync_Folder_UnreadableSubfolder_OneErrorEntryAndReadableFilesHashed()
    {
        string root = MakeFolder("h1");
        string locked = Path.Combine(root, "sub", "locked");
        Directory.CreateDirectory(locked);
        File.WriteAllText(Path.Combine(locked, "hidden.txt"), "hidden");
        File.WriteAllText(Path.Combine(root, "sub", "z.txt"), "after");

        HashResult result;
        using (new DeniedFolder(locked))
            result = await FileHashService.ComputeAsync([root], HashAlgorithmKind.Crc32, null, CancellationToken.None);

        result.Folder!.FileCount.Should().Be(2);
        result.Entries.Should().ContainSingle(e => e.Error != null).Which.SourcePath.Should().Be(locked);
        result.Entries.Should().Contain(e => e.SourcePath == Path.Combine(root, "a.txt") && e.Hash == "0D4A1185");
    }

    [Fact]
    public async Task ComputeAsync_Folder_UnreadableRoot_NoThrowOneErrorEntry()
    {
        string root = MakeFolder("h0");

        HashResult result;
        using (new DeniedFolder(root))
            result = await FileHashService.ComputeAsync([root], HashAlgorithmKind.Sha256, null, CancellationToken.None);

        result.Folder!.FileCount.Should().Be(0);
        result.Entries.Should().ContainSingle().Which.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task ComputeAsync_Folder_JunctionLoop_SkippedAndCompletes()
    {
        string root = MakeFolder("h2");
        string loop = Path.Combine(root, "loop");
        if (!ZipArchiveServiceArchiveTests.TryCreateJunction(loop, root))
            return; // junctions not supported on this system

        try
        {
            HashResult result = await FileHashService.ComputeAsync([root], HashAlgorithmKind.Crc32, null, CancellationToken.None);

            result.Folder!.FileCount.Should().Be(1);
            result.Folder.DataSum.Should().Be("0D4A1185");
            result.Entries.Should().ContainSingle(e => e.SourcePath == loop).Which.Error.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(loop, recursive: false);
        }
    }

    [Fact]
    public async Task ComputeAsync_Folder_JunctionToOutsideFolder_OutsideFilesNotCounted()
    {
        string root = MakeFolder("h3");
        string outside = Path.Combine(_temp.Path, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "foreign.txt"), "not mine");
        string link = Path.Combine(root, "link");
        if (!ZipArchiveServiceArchiveTests.TryCreateJunction(link, outside))
            return; // junctions not supported on this system

        try
        {
            HashResult result = await FileHashService.ComputeAsync([root], HashAlgorithmKind.Crc32, null, CancellationToken.None);

            result.Folder!.FileCount.Should().Be(1);
            result.Folder.TotalBytes.Should().Be("hello world".Length);
            result.Entries.Should().NotContain(e => e.SourcePath.Contains("foreign"));
        }
        finally
        {
            Directory.Delete(link, recursive: false);
        }
    }

    [Fact]
    public async Task ParallelCrc32_FileShorterThanDeclaredLength_FailsInsteadOfReturningCrc()
    {
        // Same as a file that shrinks while it is being hashed: the reads end before the length
        // the chunks were planned for.
        string path = Path.Combine(_temp.Path, "shrunk.dat");
        File.WriteAllBytes(path, new byte[9 * 1024 * 1024]);

        Func<Task> act = () => FileHashService.ComputeFileCrc32ParallelAsync(
            path, 12L * 1024 * 1024, tracker: null, "shrunk.dat", CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
    }
}
