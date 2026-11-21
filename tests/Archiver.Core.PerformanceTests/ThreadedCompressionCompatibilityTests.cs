using System.IO.Compression;
using System.Security.Cryptography;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.PerformanceTests;

/// <summary>
/// A test that needs a tar.exe writing both xz and zstd, on four cores or more; skipped elsewhere
/// (Windows 10's tar.exe writes neither; fewer cores would give fewer threads than the test names).
/// </summary>
public sealed class TarWritesXzAndZstdFactAttribute : FactAttribute
{
    public TarWritesXzAndZstdFactAttribute()
    {
        TarCapabilities caps = new TarSandboxedService(new GroupPolicyOptions()).DetectCapabilitiesAsync().GetAwaiter().GetResult();
        if (!caps.SupportsXz || !caps.SupportsZstd)
            Skip = "this system's tar.exe does not write xz and zstd";
        else if (Environment.ProcessorCount < 4)
            Skip = "fewer than four cores";
    }
}

/// <summary>
/// T-F565: tar.xz and tar.zst written on several threads (libarchive's xz block encoder and zstd
/// workers), read by 7-Zip itself - Pakko's own reader is the same libarchive and proves nothing
/// about the format.
/// </summary>
public sealed class ThreadedCompressionCompatibilityTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // Large enough for several blocks at the fastest level (xz 1's block is 3 MiB, zstd 1's job
    // 2 MiB), so the threads have more than one piece of work each and the bytes could differ.
    private string BuildTree()
    {
        string tree = Path.Combine(_temp.Path, "tree");
        Directory.CreateDirectory(Path.Combine(tree, "sub"));
        File.WriteAllText(Path.Combine(tree, "text.txt"), string.Concat(Enumerable.Repeat("compressible text ", 20_000)));
        Random random = new(7);
        byte[] mixed = new byte[16 * 1024 * 1024];
        for (int i = 0; i < mixed.Length; i++)
            mixed[i] = (byte)(random.Next(32) + 'a');
        File.WriteAllBytes(Path.Combine(tree, "sub", "mixed.bin"), mixed);
        File.WriteAllBytes(Path.Combine(tree, "empty.txt"), []);
        return tree;
    }

    // Hashes, not the bytes: an element-wise comparison of 16 MiB costs most of a minute.
    private static Dictionary<string, string> Files(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

    // The fastest level: several blocks (xz) and jobs (zstd) in one stream, which is what threads add.
    [TarWritesXzAndZstdFact]
    public async Task XzAndZstdOnFourThreads_SevenZipExtractsTheSource()
    {
        string tree = BuildTree();

        foreach (ArchiveContainerFormat format in new[] { ArchiveContainerFormat.TarXz, ArchiveContainerFormat.TarZst })
        {
            string name = format.ToString();
            ArchiveResult result = await _sut.CompressAsync(new ArchiveOptions
            {
                SourcePaths = [tree],
                DestinationFolder = Path.Combine(_temp.Path, "out"),
                ArchiveName = name,
                Format = format,
                CompressionLevel = CompressionLevel.Fastest,
                MaxThreads = 4,
            });
            result.Success.Should().BeTrue(because: $"{name}: " + string.Join("; ", result.Errors.Select(e => e.Message)));
            string archive = result.CreatedFiles.Should().ContainSingle().Which;

            // 7-Zip opens the codec to a .tar, then the .tar to the files.
            SevenZipRunner.Test(archive);
            string tarFolder = Path.Combine(_temp.Path, name, "tar");
            SevenZipRunner.Extract(archive, tarFolder);
            string tarFile = Directory.GetFiles(tarFolder).Should().ContainSingle().Which;
            string files = Path.Combine(_temp.Path, name, "files");
            SevenZipRunner.Extract(tarFile, files);

            Files(Path.Combine(files, "tree")).Should().BeEquivalentTo(Files(tree), because: name);
        }
    }

    // docs/CLI.md's -mmt: xz and zstd write the same bytes for any count above one.
    [TarWritesXzAndZstdFact]
    public async Task XzAndZstd_TwoAndFourThreads_WriteTheSameBytes()
    {
        string tree = BuildTree();

        foreach (ArchiveContainerFormat format in new[] { ArchiveContainerFormat.TarXz, ArchiveContainerFormat.TarZst })
        {
            byte[][] archives = new byte[2][];
            int[] counts = [2, 4];
            for (int i = 0; i < counts.Length; i++)
            {
                ArchiveResult result = await _sut.CompressAsync(new ArchiveOptions
                {
                    SourcePaths = [tree],
                    DestinationFolder = Path.Combine(_temp.Path, $"same-{format}-{counts[i]}"),
                    ArchiveName = "same",
                    Format = format,
                    CompressionLevel = CompressionLevel.Fastest,
                    MaxThreads = counts[i],
                });
                result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
                archives[i] = await File.ReadAllBytesAsync(result.CreatedFiles.Should().ContainSingle().Which);
            }

            archives[0].Should().Equal(archives[1], because: $"{format} on 2 and 4 threads");
        }
    }
}
