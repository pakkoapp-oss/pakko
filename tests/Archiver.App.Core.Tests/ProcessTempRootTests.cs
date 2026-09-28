using System.Diagnostics;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F252: closing one Pakko window deleted every other window's preview and nested-archive files.
public sealed class ProcessTempRootTests : IDisposable
{
    private readonly string _shared = Path.Combine(Path.GetTempPath(), "PakkoTempRootTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_shared, recursive: true); } catch (DirectoryNotFoundException) { /* already gone */ }
    }

    private ProcessTempRoot Root(string owner, params string[] aliveOwners) =>
        new(_shared, owner, name => aliveOwners.Contains(name));

    [Fact]
    public void CreateScope_LivesUnderOwnSubfolder()
    {
        ProcessTempRoot root = Root("100-1");

        string scope = root.CreateScope();

        Directory.Exists(scope).Should().BeTrue();
        Path.GetDirectoryName(scope).Should().Be(Path.Combine(_shared, "100-1"));
    }

    [Fact]
    public void DeleteOwn_LeavesAnotherProcessesScopes()
    {
        ProcessTempRoot mine = Root("100-1");
        ProcessTempRoot other = Root("200-2");
        string myScope = mine.CreateScope();
        string otherScope = other.CreateScope();
        File.WriteAllText(Path.Combine(otherScope, "open.txt"), "still previewed");

        mine.DeleteOwn();

        Directory.Exists(myScope).Should().BeFalse();
        File.Exists(Path.Combine(otherScope, "open.txt")).Should().BeTrue();
    }

    [Fact]
    public void SweepStale_DeletesDeadOwnersKeepsLiveOnesAndOwn()
    {
        ProcessTempRoot mine = Root("100-1", "200-2");
        string myScope = mine.CreateScope();
        string liveScope = Root("200-2").CreateScope();
        string deadScope = Root("300-3").CreateScope();
        string legacyGuidFolder = Directory.CreateDirectory(Path.Combine(_shared, Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllText(Path.Combine(_shared, "stray.txt"), "x");

        mine.SweepStale();

        Directory.Exists(myScope).Should().BeTrue();
        Directory.Exists(liveScope).Should().BeTrue();
        Directory.Exists(deadScope).Should().BeFalse();
        Directory.Exists(legacyGuidFolder).Should().BeFalse();
        File.Exists(Path.Combine(_shared, "stray.txt")).Should().BeFalse();
    }

    [Fact]
    public void SweepStale_MissingRoot_DoesNotThrow()
    {
        Action act = () => Root("100-1").SweepStale();

        act.Should().NotThrow();
    }

    [Fact]
    public void DeleteOwn_Twice_DoesNotThrow()
    {
        ProcessTempRoot root = Root("100-1");
        root.CreateScope();
        root.DeleteOwn();

        Action act = root.DeleteOwn;

        act.Should().NotThrow();
    }

    [Fact]
    public void IsOwnerAlive_CurrentProcess_True()
    {
        ProcessTempRoot.IsOwnerAlive(ProcessTempRoot.CurrentOwnerName).Should().BeTrue();
    }

    [Fact]
    public void IsOwnerAlive_CurrentPidWithOtherStartTime_FalseAsAReusedPid()
    {
        using var current = Process.GetCurrentProcess();
        long otherTicks = current.StartTime.ToUniversalTime().Ticks - TimeSpan.TicksPerHour;

        ProcessTempRoot.IsOwnerAlive(ProcessTempRoot.OwnerName(Environment.ProcessId, otherTicks)).Should().BeFalse();
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("12-")]
    [InlineData("-5")]
    [InlineData("1-2-3")]
    [InlineData("..")]
    public void IsOwnerAlive_UnparsableName_FalseAsALeftover(string name)
    {
        ProcessTempRoot.IsOwnerAlive(name).Should().BeFalse();
    }
}
