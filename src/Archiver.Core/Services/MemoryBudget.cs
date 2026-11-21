namespace Archiver.Core.Services;

/// <summary>
/// The memory one tar.exe run may take: the sandbox's job limit for reading, and what the threads of
/// a creation may cost together (T-F565). Each operation has its own.
/// </summary>
internal static class MemoryBudget
{
    private const long GiB = 1024L * 1024 * 1024;

    /// <summary>This machine's budget.</summary>
    internal static long ThisMachine { get; } = For(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);

    /// <summary>Half of <paramref name="physicalMemoryBytes"/>, never below 1 GiB nor above 4 GiB.</summary>
    internal static long For(long physicalMemoryBytes) => Math.Clamp(physicalMemoryBytes / 2, GiB, 4 * GiB);
}
