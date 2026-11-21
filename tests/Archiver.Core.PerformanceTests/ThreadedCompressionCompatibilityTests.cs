using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.PerformanceTests;

/// <summary>A test that needs a tar.exe writing both xz and zstd; skipped where it writes neither (Windows 10).</summary>
public sealed class TarWritesXzAndZstdFactAttribute : FactAttribute
{
    public TarWritesXzAndZstdFactAttribute()
    {
        TarCapabilities caps = new TarSandboxedService(new GroupPolicyOptions()).DetectCapabilitiesAsync().GetAwaiter().GetResult();
        if (!caps.SupportsXz || !caps.SupportsZstd)
            Skip = "this system's tar.exe does not write xz and zstd";
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

    private string BuildTree()
    {
        string tree = Path.Combine(_temp.Path, "tree");
        Directory.CreateDirectory(Path.Combine(tree, "sub"));
        File.WriteAllText(Path.Combine(tree, "text.txt"), string.Concat(Enumerable.Repeat("compressible text ", 20_000)));
        byte[] random = new byte[300_000];
        new Random(7).NextBytes(random);
        File.WriteAllBytes(Path.Combine(tree, "sub", "random.bin"), random);
        File.WriteAllBytes(Path.Combine(tree, "empty.txt"), []);
        return tree;
    }

    private static Dictionary<string, byte[]> Files(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes);

    [TarWritesXzAndZstdFact]
    public async Task XzAndZstdOnFourThreads_SevenZipExtractsTheSource()
    {
        string tree = BuildTree();

        foreach (ArchiveContainerFormat format in new[] { ArchiveContainerFormat.TarXz, ArchiveContainerFormat.TarZst })
        {
            foreach (CompressionLevel level in new[] { CompressionLevel.Fastest, CompressionLevel.SmallestSize })
            {
                string name = $"{format}-{level}";
                ArchiveResult result = await _sut.CompressAsync(new ArchiveOptions
                {
                    SourcePaths = [tree],
                    DestinationFolder = Path.Combine(_temp.Path, "out"),
                    ArchiveName = name,
                    Format = format,
                    CompressionLevel = level,
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
    }
}
