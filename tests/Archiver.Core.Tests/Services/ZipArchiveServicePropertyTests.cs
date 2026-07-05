using System.Security.Cryptography;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F24: property-based archive/extract round-trip integrity testing. Generates a directory
/// tree, archives it, extracts it, and compares the SHA-256 hash of every original file against
/// its extracted counterpart. Exists specifically to catch the class of bug T-F75 found by hand
/// (entry names silently losing their path prefix below the first nesting level) — a bug no
/// existing test caught because none compared full round-trip content across a nested tree.
/// </summary>
public sealed class ZipArchiveServicePropertyTests : IDisposable
{
    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    public static IEnumerable<object[]> Seeds() =>
        Enumerable.Range(1, 12).Select(seed => new object[] { seed });

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task RoundTrip_RandomDirectoryTree_EveryFileMatchesByHash(int seed)
    {
        await AssertRoundTripIntegrity(seed, new RandomTreeShape(MaxDepth: 4, MinFilesPerDir: 1, MaxFilesPerDir: 4, MinFileSize: 0, MaxFileSize: 20_000));
    }

    [Fact]
    public async Task RoundTrip_AllSmallFiles_EveryFileMatchesByHash()
    {
        await AssertRoundTripIntegrity(seed: 100, new RandomTreeShape(MaxDepth: 2, MinFilesPerDir: 3, MaxFilesPerDir: 6, MinFileSize: 0, MaxFileSize: 200));
    }

    [Fact]
    public async Task RoundTrip_AllLargerFiles_EveryFileMatchesByHash()
    {
        // "Larger" relative to a fast unit test — big enough to exercise multi-buffer
        // CopyToAsync transfers (CopyBufferSize is 80 KB), not multi-GB (that's T-F20's job).
        await AssertRoundTripIntegrity(seed: 200, new RandomTreeShape(MaxDepth: 1, MinFilesPerDir: 2, MaxFilesPerDir: 3, MinFileSize: 300_000, MaxFileSize: 900_000));
    }

    [Fact]
    public async Task RoundTrip_MixedFileSizes_EveryFileMatchesByHash()
    {
        await AssertRoundTripIntegrity(seed: 300, new RandomTreeShape(MaxDepth: 3, MinFilesPerDir: 1, MaxFilesPerDir: 4, MinFileSize: 0, MaxFileSize: 400_000));
    }

    [Fact]
    public async Task RoundTrip_DeepNesting_EveryFileMatchesByHash()
    {
        // Forces the maximum depth at every level (no random early stop) — this is exactly the
        // shape that exposed T-F75 (entries below the first nesting level losing their prefix).
        await AssertRoundTripIntegrity(seed: 400, new RandomTreeShape(MaxDepth: 6, MinFilesPerDir: 1, MaxFilesPerDir: 2, MinFileSize: 0, MaxFileSize: 5_000, ForceMaxDepth: true));
    }

    private async Task AssertRoundTripIntegrity(int seed, RandomTreeShape shape)
    {
        var rng = new Random(seed);
        string sourceRoot = Path.Combine(_temp.Path, $"source_{seed}");
        Directory.CreateDirectory(sourceRoot);

        var relativeFilePaths = new List<string>();
        GenerateLevel(sourceRoot, "", rng, shape.MaxDepth, shape, relativeFilePaths);
        relativeFilePaths.Should().NotBeEmpty("the generator must produce at least one file to make this test meaningful");

        var archiveResult = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [sourceRoot],
            DestinationFolder = _temp.Path,
            ArchiveName = $"roundtrip_{seed}"
        });

        archiveResult.Success.Should().BeTrue($"seed {seed}: archiving should succeed");
        archiveResult.Errors.Should().BeEmpty($"seed {seed}");

        string extractDest = Path.Combine(_temp.Path, $"extracted_{seed}");
        var extractResult = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archiveResult.CreatedFiles[0]],
            DestinationFolder = extractDest,
            Mode = ExtractMode.SingleFolder
        });

        extractResult.Success.Should().BeTrue($"seed {seed}: extraction should succeed");
        extractResult.Errors.Should().BeEmpty($"seed {seed}");

        // A single directory archived alone is always a single-root-folder case, so smart
        // foldering strips sourceRoot's own name — extracted files keep the same relative paths.
        foreach (var relativePath in relativeFilePaths)
        {
            string originalPath = Path.Combine(sourceRoot, relativePath);
            string extractedPath = Path.Combine(extractDest, relativePath);

            File.Exists(extractedPath).Should().BeTrue(
                $"seed {seed}: '{relativePath}' should exist after round-trip at '{extractedPath}'");
            ComputeSha256(originalPath).Should().Be(ComputeSha256(extractedPath),
                $"seed {seed}: '{relativePath}' content must be byte-identical after round-trip");
        }

        var extractedFileCount = Directory.GetFiles(extractDest, "*", SearchOption.AllDirectories).Length;
        extractedFileCount.Should().Be(relativeFilePaths.Count,
            $"seed {seed}: no extra or missing files after round-trip");
    }

    private sealed record RandomTreeShape(
        int MaxDepth,
        int MinFilesPerDir,
        int MaxFilesPerDir,
        int MinFileSize,
        int MaxFileSize,
        bool ForceMaxDepth = false);

    private static void GenerateLevel(
        string currentDir, string relativePrefix, Random rng, int depthRemaining,
        RandomTreeShape shape, List<string> relativeFilePaths)
    {
        int fileCount = rng.Next(shape.MinFilesPerDir, shape.MaxFilesPerDir + 1);
        for (int i = 0; i < fileCount; i++)
        {
            string fileName = $"file_{i}.bin";
            string relativePath = string.IsNullOrEmpty(relativePrefix)
                ? fileName
                : Path.Combine(relativePrefix, fileName);
            string fullPath = Path.Combine(currentDir, fileName);

            int size = rng.Next(shape.MinFileSize, shape.MaxFileSize + 1);
            var bytes = new byte[size];
            rng.NextBytes(bytes);
            File.WriteAllBytes(fullPath, bytes);
            relativeFilePaths.Add(relativePath);
        }

        if (depthRemaining > 0 && (shape.ForceMaxDepth || rng.Next(0, 2) == 0))
        {
            int subDirCount = rng.Next(1, 3);
            for (int d = 0; d < subDirCount; d++)
            {
                string subDirName = $"dir_{d}";
                string subDirPath = Path.Combine(currentDir, subDirName);
                Directory.CreateDirectory(subDirPath);
                string subPrefix = string.IsNullOrEmpty(relativePrefix)
                    ? subDirName
                    : Path.Combine(relativePrefix, subDirName);
                GenerateLevel(subDirPath, subPrefix, rng, depthRemaining - 1, shape, relativeFilePaths);
            }
        }
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs));
    }
}
