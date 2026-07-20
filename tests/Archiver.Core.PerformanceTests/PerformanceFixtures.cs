namespace Archiver.Core.PerformanceTests;

/// <summary>
/// T-F114: generates the three fixture shapes used by the compression/extraction performance
/// tests, at test-run time into a caller-supplied directory (never committed to git — distinct
/// from Archiver.Core.Tests.GenerateFixtures' small, committed correctness fixtures).
/// </summary>
public static class PerformanceFixtures
{
    // Fixed seed: fixture content must be identical across runs/machines so the Pakko-vs-7za
    // ratio being compared isn't itself polluted by different random input each time.
    private const int Seed = 20260717;

    public const int ManySmallFilesCount = 5_000;
    public const int ManySmallFilesMinBytes = 1_024;
    public const int ManySmallFilesMaxBytes = 10_240;

    public const long LargeFileBytes = 300L * 1024 * 1024;

    public const int HybridSmallFilesCount = 500;
    public const int HybridSmallFilesMinBytes = 1_024;
    public const int HybridSmallFilesMaxBytes = 51_200;
    public const int HybridMediumFilesCount = 4;
    public const long HybridMediumFileMinBytes = 5L * 1024 * 1024;
    public const long HybridMediumFileMaxBytes = 20L * 1024 * 1024;

    // T-F128 follow-up: unlike the three fixtures above (all flat, no subfolders), this one has
    // real nested subfolders — scaled down from the real-world 993-folder/14049-file case that
    // surfaced the folder-hash performance question, but still large enough to exercise real
    // subfolder recursion and relative-path computation cost, which none of the flat fixtures do.
    public const int ManyFilesAndFoldersFolderCount = 300;
    public const int ManyFilesAndFoldersFilesPerFolder = 10;
    public const int ManyFilesAndFoldersMinBytes = 1_024;
    public const int ManyFilesAndFoldersMaxBytes = 10_240;

    public static string CreateManySmallFilesFolder(string rootDir)
    {
        string dir = Path.Combine(rootDir, "many_small_files");
        Directory.CreateDirectory(dir);
        var rng = new Random(Seed);
        for (int i = 0; i < ManySmallFilesCount; i++)
        {
            long size = rng.NextInt64(ManySmallFilesMinBytes, ManySmallFilesMaxBytes + 1);
            WriteSemiCompressibleFile(Path.Combine(dir, $"f{i}.dat"), size, rng);
        }
        return dir;
    }

    public static string CreateOneLargeFileFolder(string rootDir)
    {
        string dir = Path.Combine(rootDir, "one_large_file");
        Directory.CreateDirectory(dir);
        var rng = new Random(Seed);
        WriteSemiCompressibleFile(Path.Combine(dir, "large.dat"), LargeFileBytes, rng);
        return dir;
    }

    public static string CreateHybridFolder(string rootDir)
    {
        string dir = Path.Combine(rootDir, "hybrid");
        Directory.CreateDirectory(dir);
        var rng = new Random(Seed);
        for (int i = 0; i < HybridSmallFilesCount; i++)
        {
            long size = rng.NextInt64(HybridSmallFilesMinBytes, HybridSmallFilesMaxBytes + 1);
            WriteSemiCompressibleFile(Path.Combine(dir, $"small_{i}.dat"), size, rng);
        }
        for (int i = 0; i < HybridMediumFilesCount; i++)
        {
            long size = rng.NextInt64(HybridMediumFileMinBytes, HybridMediumFileMaxBytes + 1);
            WriteSemiCompressibleFile(Path.Combine(dir, $"medium_{i}.dat"), size, rng);
        }
        return dir;
    }

    public static string CreateManyFilesAndFoldersFolder(string rootDir)
    {
        string dir = Path.Combine(rootDir, "many_files_and_folders");
        Directory.CreateDirectory(dir);
        var rng = new Random(Seed);
        for (int f = 0; f < ManyFilesAndFoldersFolderCount; f++)
        {
            string subDir = Path.Combine(dir, $"sub_{f}");
            Directory.CreateDirectory(subDir);
            for (int i = 0; i < ManyFilesAndFoldersFilesPerFolder; i++)
            {
                long size = rng.NextInt64(ManyFilesAndFoldersMinBytes, ManyFilesAndFoldersMaxBytes + 1);
                WriteSemiCompressibleFile(Path.Combine(subDir, $"f{i}.dat"), size, rng);
            }
        }
        return dir;
    }

    // A repeating pseudo-random block gives real internal redundancy (unlike pure random noise,
    // which no compressor can shrink) without being trivially compressible (unlike all-zeros) —
    // representative of realistic mixed file content for a fair Pakko-vs-7za ratio.
    private static void WriteSemiCompressibleFile(string path, long sizeBytes, Random rng)
    {
        const int blockSize = 64 * 1024;
        byte[] block = new byte[blockSize];
        rng.NextBytes(block);

        using var stream = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None, blockSize, FileOptions.SequentialScan);
        long remaining = sizeBytes;
        while (remaining > 0)
        {
            int toWrite = (int)Math.Min(blockSize, remaining);
            stream.Write(block, 0, toWrite);
            remaining -= toWrite;
        }
    }
}
