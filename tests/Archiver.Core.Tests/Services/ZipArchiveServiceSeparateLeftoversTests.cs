using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F497: one archive per item sweeps each destination folder once per run instead of once per
// archive. What a killed run left beside the sources must still be gone after the next run, and
// that run must still commit only whole archives, one per source.
public sealed class ZipArchiveServiceSeparateLeftoversTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pakko-tf497-" + Path.GetRandomFileName());

    public ZipArchiveServiceSeparateLeftoversTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(_dir, "*", SearchOption.AllDirectories))
            File.SetAttributes(entry, FileAttributes.Normal);
        Directory.Delete(_dir, recursive: true);
    }

    private string[] Sources(int count)
    {
        var paths = new string[count];
        for (int i = 0; i < count; i++)
        {
            paths[i] = Path.Combine(_dir, $"s{i:D3}.txt");
            File.WriteAllText(paths[i], string.Concat(Enumerable.Repeat($"source {i} ", 200)));
        }
        return paths;
    }

    // What a killed run leaves in the folder it wrote to: a half-written temporary archive, the
    // parallel writer's chunk folder, tar's 7z writer folder.
    private (string TempArchive, string ChunkFolder, string SevenZipFolder) KilledRunLeftovers()
    {
        string tempArchive = Path.Combine(_dir, DeadOwner.Name(".pakko-a-", ".tmp"));
        File.WriteAllBytes(tempArchive, [0x50, 0x4B, 0x03, 0x04, 1, 2, 3]);
        string chunkFolder = Path.Combine(_dir, DeadOwner.Name(".pakko-tmp-"));
        Directory.CreateDirectory(chunkFolder);
        File.WriteAllText(Path.Combine(chunkFolder, "chunk.tmp"), "compressed");
        File.SetAttributes(chunkFolder, File.GetAttributes(chunkFolder) | FileAttributes.Hidden);
        string sevenZipFolder = Path.Combine(_dir, DeadOwner.Name(".pakko-7z-"));
        Directory.CreateDirectory(sevenZipFolder);
        return (tempArchive, chunkFolder, sevenZipFolder);
    }

    [Theory]
    [InlineData(CompressionLevel.Optimal, false)]
    [InlineData(CompressionLevel.Fastest, false)]
    [InlineData(CompressionLevel.Optimal, true)]
    public async Task ArchiveAsync_OneArchivePerItemBesideAKilledRunsLeftovers_SweepsThemAndCommitsEveryArchiveWhole(
        CompressionLevel level, bool withPassword)
    {
        string[] sources = Sources(5);
        (string tempArchive, string chunkFolder, string sevenZipFolder) = KilledRunLeftovers();

        ArchiveResult result = await new ZipArchiveService(new GroupPolicyOptions()).ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = sources,
            DestinationFolder = _dir,
            Mode = ArchiveMode.SeparateArchives,
            CompressionLevel = level,
            ResolvePasswordAsync = withPassword ? _ => Task.FromResult(new PasswordDecision { Password = "pw1" }) : null,
        });

        result.Success.Should().BeTrue();
        File.Exists(tempArchive).Should().BeFalse();
        Directory.Exists(chunkFolder).Should().BeFalse();
        Directory.Exists(sevenZipFolder).Should().BeFalse();
        Directory.GetFileSystemEntries(_dir).Select(Path.GetFileName).Order(StringComparer.Ordinal).Should().Equal(
            sources.Select(Path.GetFileName).Concat(sources.Select(s => Path.GetFileNameWithoutExtension(s) + ".zip"))
                .Order(StringComparer.Ordinal));
        result.Sources.Should().OnlyContain(s => s.Outcome == SourceOutcome.Completed);
        foreach (string source in sources)
        {
            using ZipArchive archive = ZipFile.OpenRead(Path.ChangeExtension(source, ".zip"));
            archive.Entries.Should().ContainSingle().Which.FullName.Should().Be(Path.GetFileName(source));
            if (!withPassword)
            {
                using var reader = new StreamReader(archive.Entries[0].Open());
                reader.ReadToEnd().Should().Be(File.ReadAllText(source));
            }
        }
    }

    // The quadratic T-F497 removed: a listing of the destination folder per archive.
    [Theory]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.Fastest)]
    public async Task ArchiveAsync_TwentyArchivesPerItem_SweepsTheDestinationFolderOnce(CompressionLevel level)
    {
        string[] sources = Sources(20);
        var swept = new List<string>();
        var service = new ZipArchiveService(new GroupPolicyOptions())
        {
            SweepTemporaries = folder => { lock (swept) swept.Add(folder); ArchiveTempFile.Sweep(folder); },
        };

        ArchiveResult result = await service.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = sources,
            DestinationFolder = _dir,
            Mode = ArchiveMode.SeparateArchives,
            CompressionLevel = level,
        });

        result.Success.Should().BeTrue();
        result.CreatedFiles.Should().HaveCount(20);
        swept.Should().Equal(Path.GetFullPath(_dir));
        Directory.GetFileSystemEntries(_dir, ".pakko-*").Should().BeEmpty("no chunk folder is made for files this small");
    }

    // T-F497 step 2: whether an archive got an entry is known from writing it, not from opening
    // it again. These pin the answer the second open gave: an archive is committed (and its source
    // can go to the Recycle Bin or -sdel) only when it holds what its source holds.
    private static ArchiveOptions Separate(string[] sources, string destination, CompressionLevel level) => new()
    {
        SourcePaths = sources,
        DestinationFolder = destination,
        Mode = ArchiveMode.SeparateArchives,
        CompressionLevel = level,
    };

    private static byte[] Compressible(int length)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)('a' + i % 23);
        return data;
    }

    [Theory]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.Fastest)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task ArchiveAsync_OneArchivePerItem_ASourceWhoseReadFailsMidCopy_GetsNoArchiveAndIsNotCompleted(CompressionLevel level)
    {
        string[] good = Sources(3);
        string broken = Path.Combine(_dir, "broken.bin");
        File.WriteAllBytes(broken, Compressible(900_000));
        ArchiveResult result;
        using (var holder = new FileStream(broken, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            holder.Lock(400_000, 100_000); // ReadFile past 400,000 fails with a lock violation
            result = await new ZipArchiveService(new GroupPolicyOptions())
                .ArchiveAsync(Separate([.. good, broken], _dir, level));
        }

        File.Exists(Path.Combine(_dir, "broken.zip")).Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.SourcePath.Should().Be(broken);
        result.Sources.Single(s => s.Path == broken).Outcome.Should().NotBe(SourceOutcome.Completed);
        result.Sources.Where(s => s.Path != broken).Should().OnlyContain(s => s.Outcome == SourceOutcome.Completed);
        good.Should().OnlyContain(g => File.Exists(Path.ChangeExtension(g, ".zip")));
        Directory.GetFileSystemEntries(_dir, ".pakko-*").Should().BeEmpty();
    }

    [Theory]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.Fastest)]
    public async Task ArchiveAsync_OneArchivePerItem_EmptyFileAndEmptyFolder_EachGetAnArchive(CompressionLevel level)
    {
        string empty = Path.Combine(_dir, "empty.txt");
        File.WriteAllBytes(empty, []);
        string emptyFolder = Path.Combine(_dir, "nothing");
        Directory.CreateDirectory(emptyFolder);

        ArchiveResult result = await new ZipArchiveService(new GroupPolicyOptions())
            .ArchiveAsync(Separate([empty, emptyFolder], _dir, level));

        result.Success.Should().BeTrue();
        result.Sources.Should().OnlyContain(s => s.Outcome == SourceOutcome.Completed);
        using (ZipArchive file = ZipFile.OpenRead(Path.Combine(_dir, "empty.zip")))
            file.Entries.Should().ContainSingle().Which.Length.Should().Be(0);
        using (ZipArchive folder = ZipFile.OpenRead(Path.Combine(_dir, "nothing.zip")))
            folder.Entries.Should().ContainSingle().Which.FullName.Should().Be("nothing/"); // T-F66
    }

    // T-F60: a folder whose every file fails gets no archive at all.
    [Theory]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.Fastest)]
    public async Task ArchiveAsync_OneArchivePerItem_AFolderWhoseOnlyFileFails_GetsNoArchive(CompressionLevel level)
    {
        string folder = Path.Combine(_dir, "locked");
        Directory.CreateDirectory(folder);
        string only = Path.Combine(folder, "only.txt");
        File.WriteAllText(only, "held");
        ArchiveResult result;
        using (new FileStream(only, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            result = await new ZipArchiveService(new GroupPolicyOptions()).ArchiveAsync(Separate([folder], _dir, level));

        File.Exists(Path.Combine(_dir, "locked.zip")).Should().BeFalse();
        result.Sources.Should().ContainSingle().Which.Outcome.Should().NotBe(SourceOutcome.Completed);
        Directory.GetFileSystemEntries(_dir, ".pakko-*").Should().BeEmpty();
    }

    // A live run's temporary files are never swept by another run in the same folder.
    [Fact]
    public async Task ArchiveAsync_OneArchivePerItem_KeepsALiveRunsTemporaryFile()
    {
        string[] sources = Sources(3);
        string live = Path.Combine(_dir, Archiver.Core.IO.TempOwner.NewName(".pakko-a-", ".tmp"));
        File.WriteAllText(live, "another run of this process is writing it");

        ArchiveResult result = await new ZipArchiveService(new GroupPolicyOptions()).ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = sources,
            DestinationFolder = _dir,
            Mode = ArchiveMode.SeparateArchives,
        });

        result.Success.Should().BeTrue();
        File.Exists(live).Should().BeTrue("its owner is this process, still running");
    }
}
