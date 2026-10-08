using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Zip;
using Archiver.Core.Services.Zip.Decryption;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F299: an entry Deflate cannot shrink is written as Stored. .NET 10's Fastest level (zlib-ng
/// level 1) grows random data by ~5.5%, and the App's default level is Fastest. Every test goes
/// through <c>ArchiveAsync</c>, since the reported case is one file at Fastest — the shape the
/// sequential <c>ZipArchive</c> path used to take.
/// </summary>
public sealed class ZipArchiveServiceIncompressibleTests : IDisposable
{
    private const ushort StoredMethod = 0;
    private const ushort DeflateMethod = 8;
    private const string Password = "s3cret-pass";

    // One size under ParallelSingleArchiveWriter.InMemoryCompressByteThreshold (the in-memory
    // compressor), one above it (the temp-file compressor).
    public static TheoryData<int> Sizes => new()
    {
        (int)(ParallelSingleArchiveWriter.InMemoryCompressByteThreshold / 4),
        (int)(ParallelSingleArchiveWriter.InMemoryCompressByteThreshold * 3),
    };

    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string WriteRandomFile(string name, int size, int seed = 299)
    {
        byte[] content = new byte[size];
        new Random(seed).NextBytes(content);
        string path = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private string WriteTextFile(string name, int size)
    {
        string line = "The quick brown fox jumps over the lazy dog. 0123456789\n";
        string text = string.Concat(Enumerable.Repeat(line, size / line.Length + 1))[..size];
        string path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, text);
        return path;
    }

    private async Task<string> ArchiveAsync(
        IReadOnlyList<string> sources, CompressionLevel level, ArchiveMode mode = ArchiveMode.SingleArchive, string? password = null)
    {
        string outDir = Path.Combine(_temp.Path, "out-" + Path.GetRandomFileName());
        Directory.CreateDirectory(outDir);
        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = sources,
            DestinationFolder = outDir,
            ArchiveName = mode == ArchiveMode.SingleArchive ? "out" : null,
            Mode = mode,
            CompressionLevel = level,
            ResolvePasswordAsync = password is null ? null : _ => Task.FromResult(new PasswordDecision { Password = password }),
        });
        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        return result.CreatedFiles.Should().ContainSingle().Subject;
    }

    private static List<(string Name, LocatedZipEntry Located)> ReadRawEntries(string archivePath)
    {
        using ZipArchive zip = ZipFile.OpenRead(archivePath);
        using FileStream fs = File.OpenRead(archivePath);
        List<LocatedZipEntry> located = RawZipEntryLocator.LocateAll(fs);
        return zip.Entries.Select((e, i) => (e.FullName, located[i])).ToList();
    }

    private async Task AssertRoundTripsAsync(string archivePath, string sourcePath, string? password = null)
    {
        string destDir = Path.Combine(_temp.Path, "x-" + Path.GetRandomFileName());
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = password is null ? null : _ => Task.FromResult(new PasswordDecision { Password = password }),
        });
        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        string extracted = Directory.EnumerateFiles(destDir, "*", SearchOption.AllDirectories).Should().ContainSingle().Subject;
        File.ReadAllBytes(extracted).Should().Equal(File.ReadAllBytes(sourcePath));
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public async Task ArchiveAsync_OneRandomFileAtFastest_IsStoredNotLarger(int size)
    {
        string source = WriteRandomFile("random.bin", size);

        string archive = await ArchiveAsync([source], CompressionLevel.Fastest);

        (string _, LocatedZipEntry entry) = ReadRawEntries(archive).Should().ContainSingle().Subject;
        entry.RealCompressionMethod.Should().Be(StoredMethod);
        entry.CompressedSize.Should().Be(size);
        new FileInfo(archive).Length.Should().BeLessThan(size + 1024);
        await AssertRoundTripsAsync(archive, source);
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public async Task ArchiveAsync_SeparateArchivesAtFastest_RandomFileIsStored(int size)
    {
        string source = WriteRandomFile("random.bin", size);

        string archive = await ArchiveAsync([source], CompressionLevel.Fastest, ArchiveMode.SeparateArchives);

        ReadRawEntries(archive).Should().ContainSingle().Which.Located.RealCompressionMethod.Should().Be(StoredMethod);
        await AssertRoundTripsAsync(archive, source);
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public async Task ArchiveAsync_EncryptedRandomFileAtOptimal_IsStoredInsideAes(int size)
    {
        string source = WriteRandomFile("random.bin", size);

        string archive = await ArchiveAsync([source], CompressionLevel.Optimal, password: Password);

        (string _, LocatedZipEntry entry) = ReadRawEntries(archive).Should().ContainSingle().Subject;
        entry.CompressionMethod.Should().Be(99);
        entry.RealCompressionMethod.Should().Be(StoredMethod);
        entry.CompressedSize.Should().Be(size + WinZipAesEncryptStream.Overhead);
        await AssertRoundTripsAsync(archive, source, Password);
    }

    [Fact]
    public async Task ArchiveAsync_ManyRandomFilesAtOptimal_EveryEntryIsStored()
    {
        string dir = Path.Combine(_temp.Path, "many");
        for (int i = 0; i < 80; i++)
            WriteRandomFile(Path.Combine("many", $"f{i:D3}.bin"), 4096, seed: i);

        string archive = await ArchiveAsync([dir], CompressionLevel.Optimal);

        ReadRawEntries(archive).Where(e => !e.Name.EndsWith('/'))
            .Should().HaveCount(80).And.OnlyContain(e => e.Located.RealCompressionMethod == StoredMethod);
    }

    // T-F357: a stored entry above the in-memory threshold is copied from its source, which is held
    // open until then; when ArchiveAsync returns every source is free again (Delete after operation
    // comes next), and each entry still round-trips.
    [Theory]
    [InlineData(ArchiveMode.SingleArchive)]
    [InlineData(ArchiveMode.SeparateArchives)]
    public async Task ArchiveAsync_LargeStoredAndDeflatedFiles_RoundTripAndTheSourcesAreFreeAfterwards(ArchiveMode mode)
    {
        int large = (int)(ParallelSingleArchiveWriter.InMemoryCompressByteThreshold * 3);
        string[] sources =
        [
            WriteRandomFile("a-random.bin", large, seed: 1),
            WriteRandomFile("b-random.bin", large + 5, seed: 2),
            WriteTextFile("c-text.txt", large),
        ];
        string outDir = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(outDir);

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = sources, DestinationFolder = outDir, ArchiveName = mode == ArchiveMode.SingleArchive ? "out" : null,
            Mode = mode, CompressionLevel = CompressionLevel.Fastest,
        });

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        var methods = result.CreatedFiles.SelectMany(ReadRawEntries).ToDictionary(e => e.Name, e => e.Located.RealCompressionMethod);
        methods.Should().Equal(new Dictionary<string, ushort>
        {
            ["a-random.bin"] = StoredMethod, ["b-random.bin"] = StoredMethod, ["c-text.txt"] = DeflateMethod,
        });
        string destDir = Path.Combine(_temp.Path, "x");
        foreach (string archive in result.CreatedFiles)
            (await _sut.ExtractAsync(new ExtractOptions { ArchivePaths = [archive], DestinationFolder = destDir, Mode = ExtractMode.SingleFolder }))
                .Success.Should().BeTrue();
        foreach (string source in sources)
        {
            File.ReadAllBytes(Path.Combine(destDir, Path.GetFileName(source))).Should().Equal(File.ReadAllBytes(source));
            File.Delete(source);
        }
        Directory.GetDirectories(outDir, ".pakko-tmp-*").Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public async Task ArchiveAsync_CompressibleFileAtFastest_StaysDeflated(int size)
    {
        string source = WriteTextFile("text.txt", size);

        string archive = await ArchiveAsync([source], CompressionLevel.Fastest);

        (string _, LocatedZipEntry entry) = ReadRawEntries(archive).Should().ContainSingle().Subject;
        entry.RealCompressionMethod.Should().Be(DeflateMethod);
        entry.CompressedSize.Should().BeLessThan(size / 4);
        await AssertRoundTripsAsync(archive, source);
    }
}
