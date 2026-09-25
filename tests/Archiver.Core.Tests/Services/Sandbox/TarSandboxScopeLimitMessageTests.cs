using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// T-F239: a tar.exe run stopped by the Job's limits gets a message naming the sandbox limit, put
// in front of tar.exe's own ("Cannot allocate memory", or nothing for a CPU-time kill).
public sealed class TarSandboxScopeLimitMessageTests
{
    [Fact]
    public void DescribeLimitHit_Memory_NamesTheSandboxMemoryLimit()
        => TarSandboxScope.DescribeLimitHit(SandboxJobObject.LimitHit.Memory).Should().Contain("memory").And.Contain("sandbox");

    [Fact]
    public void DescribeLimitHit_CpuTime_NamesTheSandboxTimeLimit()
        => TarSandboxScope.DescribeLimitHit(SandboxJobObject.LimitHit.CpuTime).Should().Contain("processor time").And.Contain("minutes");

    // User request 2026-09-25: limits for today's archives — a decoder allocates the whole
    // dictionary (7-Zip up to 1.5 GB, zstd --long=31 2 GB, RAR5 up to 4 GB). Half the machine's
    // memory, never below 1 GB nor above 4 GB.
    [Theory]
    [InlineData(0L, 1L)]
    [InlineData(2L, 1L)]
    [InlineData(4L, 2L)]
    [InlineData(6L, 3L)]
    [InlineData(8L, 4L)]
    [InlineData(64L, 4L)]
    public void MemoryLimitFor_HalfOfPhysicalMemoryBetweenOneAndFourGiB(long physicalGiB, long expectedGiB)
    {
        const long GiB = 1024L * 1024 * 1024;
        TarSandboxScope.MemoryLimitFor(physicalGiB * GiB).Should().Be(expectedGiB * GiB);
    }

    [Fact]
    public void DescribeLimitHit_None_AddsNothing()
        => TarSandboxScope.DescribeLimitHit(SandboxJobObject.LimitHit.None).Should().BeEmpty();
}
