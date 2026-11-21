namespace Archiver.Core.Services;

/// <summary>
/// T-F565: how many threads libarchive compresses xz and zstd with, and the option that says so. The
/// least of the cores, <see cref="Models.ArchiveOptions.MaxThreads"/> where given, and the memory
/// budget divided by what one thread costs at the level (measured, docs/DECISIONS.md "v1.12.0 batch 3,
/// step 0"); never fewer than one. Codecs without threads (gzip, bzip2, lzma) always get one.
/// </summary>
internal static class CompressionThreads
{
    private const long MiB = 1024L * 1024;

    /// <summary>What bounds the count on a machine.</summary>
    internal readonly record struct Limit(int MaxThreads, int ProcessorCount, long MemoryBudgetBytes)
    {
        /// <summary>Exactly one thread: the arguments tar.exe was given before T-F565.</summary>
        internal static Limit OneThread { get; } = new(1, 1, 0);

        /// <summary>This machine, with <paramref name="maxThreads"/> from -mmt (0 or less: no cap).</summary>
        internal static Limit ForThisMachine(int maxThreads) => new(maxThreads, Environment.ProcessorCount, MemoryBudget.ThisMachine);
    }

    /// <summary>The thread count for <paramref name="codec"/> ("xz", "zstd") at <paramref name="level"/>.</summary>
    internal static int For(string codec, int level, Limit limit)
    {
        long cost = CostPerThread(codec, level);
        if (cost == 0)
            return 1;

        long count = Math.Min(Math.Max(1, limit.ProcessorCount), Math.Max(1, limit.MemoryBudgetBytes / cost));
        if (limit.MaxThreads > 0)
            count = Math.Min(count, limit.MaxThreads);
        return (int)count;
    }

    /// <summary>libarchive's option naming <paramref name="threads"/> for <paramref name="module"/>.</summary>
    internal static string Option(string module, int threads) => $"{module}:threads={threads}";

    // The working set one more thread took at the levels Pakko uses, rounded up; a level between two
    // measured ones costs what the higher one does. 0: the codec has no threads.
    private static long CostPerThread(string codec, int level) => codec switch
    {
        "xz" => level switch
        {
            <= 0 => 16 * MiB,
            1 => 32 * MiB,
            <= 4 => 96 * MiB,
            _ => 1024 * MiB,
        },
        "zstd" => level switch
        {
            <= 1 => 16 * MiB,
            <= 9 => 96 * MiB,
            _ => 256 * MiB,
        },
        _ => 0,
    };
}
