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

    [Fact]
    public void DescribeLimitHit_None_AddsNothing()
        => TarSandboxScope.DescribeLimitHit(SandboxJobObject.LimitHit.None).Should().BeEmpty();
}
