using System.Diagnostics;
using System.Text.RegularExpressions;
using Archiver.Core.IO;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.IO;

/// <summary>
/// T-F263/T-F312: one owner for the names of Pakko's temporary files and folders. A name carries the
/// machine, process id and process start time of its owner; the sweep removes only what a process
/// that no longer runs left behind, and next to a destination (which a sync client or a share can put
/// in front of another machine) only by age for anything it cannot prove is its own machine's.
/// </summary>
public sealed class TempOwnerTests : IDisposable
{
    private const string Here = "m0000aaaa";
    private const string OtherMachine = "m1111bbbb";
    private const int DeadPid = 111;
    private const int LivePid = 222;
    private const long LiveTicks = 638_000_000_000_000_000;
    private const string Guid32 = "0123456789abcdef0123456789abcdef";

    private readonly TempDirectory _temp = new();
    private readonly DateTime _now = DateTime.UtcNow;

    public void Dispose() => _temp.Dispose();

    private static TempOwnerProbe Probe(DateTime utcNow) =>
        new("0000aaaa", (pid, ticks) => pid == LivePid && (ticks is null || ticks == LiveTicks), utcNow);

    private string Dir(string name)
    {
        string path = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(Path.Combine(path, "sub"));
        File.WriteAllText(Path.Combine(path, "sub", "staged.txt"), "staged");
        return path;
    }

    private string FileIn(string name)
    {
        string path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, "partial archive");
        return path;
    }

    private void Sweep(string prefix, TempScope scope, DateTime? utcNow = null, string suffix = "") =>
        TempOwner.SweepStale(_temp.Path, prefix, suffix, scope, Probe(utcNow ?? _now));

    [Fact]
    public void CurrentTag_NamesMachineProcessAndStartTime()
    {
        using var current = Process.GetCurrentProcess();

        TempOwner.CurrentTag.Should().MatchRegex("^m[0-9a-f]{8}-[0-9]+-[0-9]+$");
        TempOwner.CurrentTag.Should().EndWith($"-{Environment.ProcessId}-{current.StartTime.ToUniversalTime().Ticks}");
    }

    [Fact]
    public void NewName_IsPrefixTagUniquePartAndSuffix()
    {
        string a = TempOwner.NewName(".pakko-a-", ".tmp");
        string b = TempOwner.NewName(".pakko-a-", ".tmp");

        a.Should().MatchRegex("^" + Regex.Escape(".pakko-a-" + TempOwner.CurrentTag) + "-[0-9a-f]{32}" + Regex.Escape(".tmp") + "$");
        a.Should().NotBe(b);
    }

    [Fact]
    public void IsRunningHere_CurrentTag_True() =>
        TempOwner.IsRunningHere(TempOwner.CurrentTag).Should().BeTrue();

    [Fact]
    public void IsRunningHere_CurrentPidWithOtherStartTime_FalseAsAReusedPid()
    {
        using var current = Process.GetCurrentProcess();
        long otherTicks = current.StartTime.ToUniversalTime().Ticks - TimeSpan.TicksPerHour;

        TempOwner.IsRunningHere($"{Here}-{Environment.ProcessId}-{otherTicks}").Should().BeFalse();
    }

    // v1.6.0's preview and nested-archive folders are named "<pid>-<start ticks>": a v1.6.0 window
    // still open next to a newer one must keep its files.
    [Fact]
    public void IsRunningHere_OlderPidAndTicksName_CurrentProcess_True()
    {
        using var current = Process.GetCurrentProcess();

        TempOwner.IsRunningHere($"{Environment.ProcessId}-{current.StartTime.ToUniversalTime().Ticks}").Should().BeTrue();
    }

    [Theory]
    [InlineData(Guid32)]
    [InlineData("12-")]
    [InlineData("-5")]
    [InlineData("1-2-3")]
    [InlineData("..")]
    [InlineData("mzzzzzzzz-1-2")]
    public void IsRunningHere_UnparsableName_False(string name) =>
        TempOwner.IsRunningHere(name).Should().BeFalse();

    [Fact]
    public void Sweep_ThisMachine_DeadOwnerRemoved_LiveOwnerKept()
    {
        string dead = Dir($".pakko-x-{Here}-{DeadPid}-{LiveTicks}-{Guid32}");
        string reused = Dir($".pakko-x-{Here}-{LivePid}-{LiveTicks + 1}-{Guid32}");
        string live = Dir($".pakko-x-{Here}-{LivePid}-{LiveTicks}-{Guid32}");

        Sweep(".pakko-x-", TempScope.Destination);

        Directory.Exists(dead).Should().BeFalse();
        Directory.Exists(reused).Should().BeFalse();
        Directory.Exists(live).Should().BeTrue();
    }

    // A destination in a synced folder or on a share: this machine cannot tell whether another
    // machine's process still runs.
    [Fact]
    public void Sweep_Destination_OtherMachine_RemovedOnlyOnceOld()
    {
        string foreign = Dir($".pakko-x-{OtherMachine}-{DeadPid}-{LiveTicks}-{Guid32}");

        Sweep(".pakko-x-", TempScope.Destination);
        Directory.Exists(foreign).Should().BeTrue();

        Sweep(".pakko-x-", TempScope.Destination, _now.AddDays(2));
        Directory.Exists(foreign).Should().BeFalse();
    }

    // v1.6.0 named extraction staging ".pakko-x-<pid>-<guid>": no machine, so only age next to a
    // destination.
    [Fact]
    public void Sweep_Destination_OlderPidOnlyName_RemovedOnlyOnceOld()
    {
        string older = Dir($".pakko-x-{DeadPid}-{Guid32}");

        Sweep(".pakko-x-", TempScope.Destination);
        Directory.Exists(older).Should().BeTrue();

        Sweep(".pakko-x-", TempScope.Destination, _now.AddDays(2));
        Directory.Exists(older).Should().BeFalse();
    }

    [Fact]
    public void Sweep_UnownedName_RemovedOnlyOnceOld()
    {
        string unowned = Dir($".pakko-tmp-{Guid32}");

        Sweep(".pakko-tmp-", TempScope.Destination);
        Directory.Exists(unowned).Should().BeTrue();

        Sweep(".pakko-tmp-", TempScope.Destination, _now.AddDays(2));
        Directory.Exists(unowned).Should().BeFalse();
    }

    // %TEMP% is this machine's alone: an older pid-only name is decided by its process.
    [Fact]
    public void Sweep_LocalTemp_OlderPidOnlyName_DecidedByProcess()
    {
        string dead = Dir($"PakkoTarStage_{DeadPid}_{Guid32}");
        string live = Dir($"PakkoTarStage_{LivePid}_{Guid32}");

        Sweep("PakkoTarStage_", TempScope.LocalTemp(TimeSpan.FromDays(1)));

        Directory.Exists(dead).Should().BeFalse();
        Directory.Exists(live).Should().BeTrue();
    }

    [Fact]
    public void Sweep_File_DeadOwnerRemoved_FixedOlderNameUntouched()
    {
        string dead = FileIn($".pakko-a-{Here}-{DeadPid}-{LiveTicks}-{Guid32}.tmp");
        string olderFixedName = FileIn("big3.zip.tmp");

        Sweep(".pakko-a-", TempScope.Destination, suffix: ".tmp");

        File.Exists(dead).Should().BeFalse();
        File.Exists(olderFixedName).Should().BeTrue();
    }

    [Fact]
    public void Sweep_FolderWithJunctionDeepInside_RemovesLinkNotItsTarget()
    {
        string userFolder = Path.Combine(_temp.Path, "user");
        Directory.CreateDirectory(userFolder);
        File.WriteAllText(Path.Combine(userFolder, "keep.txt"), "user data");
        string dead = Dir($".pakko-x-{Here}-{DeadPid}-{LiveTicks}-{Guid32}");
        DirectoryJunction.Create(Path.Combine(dead, "sub", "link"), userFolder);

        Sweep(".pakko-x-", TempScope.Destination);

        Directory.Exists(dead).Should().BeFalse();
        File.ReadAllText(Path.Combine(userFolder, "keep.txt")).Should().Be("user data");
    }

    [Fact]
    public void Sweep_FolderWithReadOnlyFile_Removed()
    {
        string dead = Dir($".pakko-x-{Here}-{DeadPid}-{LiveTicks}-{Guid32}");
        File.SetAttributes(Path.Combine(dead, "sub", "staged.txt"), FileAttributes.ReadOnly);

        Sweep(".pakko-x-", TempScope.Destination);

        Directory.Exists(dead).Should().BeFalse();
    }

    [Fact]
    public void Sweep_OtherNamesAndMissingFolder_Untouched()
    {
        string other = Dir("photos");
        string otherPrefix = Dir($".pakko-tmp-{Here}-{DeadPid}-{LiveTicks}-{Guid32}");

        Sweep(".pakko-x-", TempScope.Destination);
        Action missing = () => TempOwner.SweepStale(Path.Combine(_temp.Path, "missing"), ".pakko-x-", "", TempScope.Destination, Probe(_now));

        Directory.Exists(other).Should().BeTrue();
        Directory.Exists(otherPrefix).Should().BeTrue();
        missing.Should().NotThrow();
    }

    [Fact]
    public void Sweep_HeldFile_StaysAndDoesNotThrow()
    {
        string dead = FileIn($".pakko-a-{Here}-{DeadPid}-{LiveTicks}-{Guid32}.tmp");

        using (new FileStream(dead, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Action act = () => Sweep(".pakko-a-", TempScope.Destination, suffix: ".tmp");
            act.Should().NotThrow();
        }

        File.Exists(dead).Should().BeTrue();
    }
}
