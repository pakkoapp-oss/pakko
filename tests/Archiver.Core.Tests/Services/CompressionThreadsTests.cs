using System.Text.RegularExpressions;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F565 (v1.12.0 batch 3): libarchive compresses xz and zstd on threads, bounded by the cores,
// -mmt, and the memory a thread costs at its level (docs/DECISIONS.md, "batch 3, step 0").
public sealed partial class CompressionThreadsTests
{
    private const long MiB = 1024L * 1024;
    private const long GiB = 1024 * MiB;

    private static CompressionThreads.Limit Machine(int cores, long budget, int maxThreads = 0) => new(maxThreads, cores, budget);

    // --- Happy path ---

    [Theory]
    [InlineData("xz", 0)]
    [InlineData("xz", 1)]
    [InlineData("xz", 4)]
    [InlineData("zstd", 1)]
    [InlineData("zstd", 9)]
    public void For_CheapLevels_TakeEveryCore(string codec, int level) =>
        CompressionThreads.For(codec, level, Machine(cores: 12, budget: 2 * GiB)).Should().Be(12);

    [Theory]
    [InlineData(1L, 1)]
    [InlineData(2L, 2)]
    [InlineData(4L, 4)]
    public void For_Xz9_ABudgetGiBEachThread(long budgetGiB, int expected) =>
        CompressionThreads.For("xz", 9, Machine(cores: 12, budget: budgetGiB * GiB)).Should().Be(expected);

    [Theory]
    [InlineData(1L, 4)]
    [InlineData(4L, 16)]
    public void For_Zstd19_AQuarterGiBEachThread(long budgetGiB, int expected) =>
        CompressionThreads.For("zstd", 19, Machine(cores: 32, budget: budgetGiB * GiB)).Should().Be(expected);

    [Fact]
    public void For_FewerCoresThanTheBudgetAllows_TheCores() =>
        CompressionThreads.For("xz", 9, Machine(cores: 2, budget: 4 * GiB)).Should().Be(2);

    [Fact]
    public void For_MaxThreadsGiven_IsTheCap() =>
        CompressionThreads.For("zstd", 1, Machine(cores: 12, budget: 4 * GiB, maxThreads: 3)).Should().Be(3);

    [Fact]
    public void For_MaxThreadsAboveTheMemoryBound_TheMemoryBoundStays() =>
        CompressionThreads.For("xz", 9, Machine(cores: 12, budget: GiB, maxThreads: 8)).Should().Be(1);

    [Fact]
    public void Option_NamesTheModule() =>
        CompressionThreads.Option("xz", 4).Should().Be("xz:threads=4");

    // --- Security & Boundary ---

    [Theory]
    [InlineData(0, 4L * 1024 * 1024 * 1024)]
    [InlineData(-3, 4L * 1024 * 1024 * 1024)]
    [InlineData(12, 0L)]
    [InlineData(12, -1L)]
    public void For_NoCoresOrNoBudget_StillOneThread(int cores, long budget) =>
        CompressionThreads.For("xz", 9, Machine(cores, budget)).Should().Be(1);

    [Fact]
    public void For_LevelsBetweenTheMeasuredOnes_CostTheNextMeasuredLevel()
    {
        // xz 5-8 is never asked by Pakko; a level between measured ones costs what the next does.
        CompressionThreads.For("xz", 6, Machine(cores: 12, budget: 2 * GiB)).Should().Be(2);
        CompressionThreads.For("zstd", 12, Machine(cores: 32, budget: GiB)).Should().Be(4);
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("bzip2")]
    [InlineData("lzma")]
    [InlineData("nosuch")]
    public void For_ACodecWithoutThreads_IsOne(string codec) =>
        CompressionThreads.For(codec, 9, Machine(cores: 12, budget: 4 * GiB)).Should().Be(1);

    // T-F565: the 7z writer's own 678 MiB at zstd 9 and 19 (step 0) is taken off the budget first.
    [Theory]
    [InlineData(9, 1L, 2)]
    [InlineData(19, 1L, 1)]
    [InlineData(19, 4L, 12)]
    [InlineData(1, 1L, 12)]
    public void For_SevenZipZstd_TheWritersFixedMemoryFirst(int level, long budgetGiB, int expected) =>
        CompressionThreads.For("7zip-zstd", level, Machine(cores: 12, budget: budgetGiB * GiB)).Should().Be(expected);

    [Fact]
    public void For_SevenZipZstd_BudgetBelowTheFixedPart_StillOneThread() =>
        CompressionThreads.For("7zip-zstd", 19, Machine(cores: 12, budget: 512 * MiB)).Should().Be(1);

    [Fact]
    public void OneThread_IsOne() =>
        CompressionThreads.For("zstd", 1, CompressionThreads.Limit.OneThread).Should().Be(1);

    // --- The memory budget, shared with the sandbox's job limit ---

    // User request 2026-09-25: limits for today's archives - a decoder allocates the whole
    // dictionary (7-Zip up to 1.5 GB, zstd --long=31 2 GB, RAR5 up to 4 GB). Half the machine's
    // memory, never below 1 GB nor above 4 GB.
    [Theory]
    [InlineData(0L, 1L)]
    [InlineData(2L, 1L)]
    [InlineData(4L, 2L)]
    [InlineData(6L, 3L)]
    [InlineData(8L, 4L)]
    [InlineData(64L, 4L)]
    public void MemoryBudget_HalfOfPhysicalMemoryBetweenOneAndFourGiB(long physicalGiB, long expectedGiB) =>
        MemoryBudget.For(physicalGiB * GiB).Should().Be(expectedGiB * GiB);

    // --- One owner ---

    private static readonly string SrcDir = Path.Combine(FindRepoRoot(), "src");
    private const string Owner = "Archiver.Core/Services/CompressionThreads.cs";

    [Fact]
    public void ThreadsOption_IsWrittenOnlyByItsOwner()
    {
        string[] offenders = [.. SourceFiles()
            .Where(file => file != Owner)
            .Where(file => ThreadsOption().IsMatch(Code(file)))];

        offenders.Should().BeEmpty("libarchive's threads option is built only by CompressionThreads (T-F565)");
    }

    [Fact]
    public void ThreadsOption_ScanSeesTheOwner() =>
        ThreadsOption().IsMatch(Code(Owner)).Should().BeTrue("the scan above must not pass vacuously");

    [GeneratedRegex(@":threads=")]
    private static partial Regex ThreadsOption();

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(SrcDir, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SrcDir, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !f.Contains("/obj/", StringComparison.Ordinal) && !f.Contains("/bin/", StringComparison.Ordinal));

    private static string Code(string file) =>
        string.Join('\n', File.ReadAllLines(Path.Combine(SrcDir, file.Replace('/', Path.DirectorySeparatorChar)))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
