using System.Diagnostics;
using System.Text.RegularExpressions;
using Archiver.Core.IO;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using Archiver.Core.Tests.Services;
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

    // T-F528: one listing for several names; each entry is judged by the name it matched, suffix too.
    [Fact]
    public void Sweep_SeveralNames_EachJudgedByItsOwn()
    {
        string deadRoot = Dir($".pakko-w-{Here}-{DeadPid}-{LiveTicks}-{Guid32}");
        string liveRoot = Dir($".pakko-w-{Here}-{LivePid}-{LiveTicks}-{Guid32}");
        string deadTemp = FileIn($".pakko-a-{Here}-{DeadPid}-{LiveTicks}-{Guid32}.tmp");
        string deadStaging = Dir($".pakko-x-{Here}-{DeadPid}-{LiveTicks}-{Guid32}");
        string unlisted = Dir($".pakko-q-{Here}-{DeadPid}-{LiveTicks}-{Guid32}");

        TempOwner.SweepStale(_temp.Path, [(".pakko-w-", ""), (".pakko-x-", ""), (".pakko-a-", ".tmp")], TempScope.Destination, Probe(_now));

        Directory.Exists(deadRoot).Should().BeFalse();
        Directory.Exists(liveRoot).Should().BeTrue();
        File.Exists(deadTemp).Should().BeFalse();
        Directory.Exists(deadStaging).Should().BeFalse();
        Directory.Exists(unlisted).Should().BeTrue("no name of the sweep matches it");
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

    // T-F511: the CLI and the App remove their temp files and folders through TryRemove; a name
    // Windows trims would reach its twin, so it is refused and both stay.
    [Fact]
    public void TryRemove_NameWindowsTrims_LeavesTheTwin()
    {
        string twin = FileIn("a.txt");
        string literal = Path.Combine(_temp.Path, "a.txt.");
        File.WriteAllText(@"\\?\" + literal, "literal");

        TempOwner.TryRemove(literal).Should().BeFalse();

        File.Exists(twin).Should().BeTrue();
        File.Exists(@"\\?\" + literal).Should().BeTrue();
        File.Delete(@"\\?\" + literal);
    }

    // T-F533: a destination typed as \\?\C:\w\out. is opened as written, so its work root is Pakko's
    // own folder there, not a twin's; it was refused and stayed until the next run swept it.
    [Fact]
    public void TryRemove_LiteralPathWindowsWouldTrim_RemovedAndTheTwinKept()
    {
        string twin = Dir("out");
        string literalDestination = @"\\?\" + Path.Combine(_temp.Path, "out.");
        string root = Path.Combine(literalDestination, ".pakko-root");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "sub", "staged.txt"), "x");

        TempOwner.TryRemove(root).Should().BeTrue();

        Directory.Exists(root).Should().BeFalse();
        Directory.Exists(literalDestination).Should().BeTrue();
        Directory.Exists(twin).Should().BeTrue();
        Directory.Delete(literalDestination);
    }

    // T-F547: names Windows trims inside Pakko's own folder, read-only: the flag was cleared through a
    // normalized path and "d " was never entered, so the recursive delete stopped and the folder stayed.
    [Fact]
    public void TryRemove_ReadOnlyNamesWindowsTrimsInside_Removed()
    {
        string folder = Dir("staging");
        string literal = @"\\?\" + folder;
        Directory.CreateDirectory(Path.Combine(literal, "d "));
        foreach (string name in new[] { "a.txt.", "b.txt ", Path.Combine("d ", "x.txt") })
        {
            string file = Path.Combine(literal, name);
            File.WriteAllText(file, "x");
            File.SetAttributes(file, FileAttributes.ReadOnly);
        }

        TempOwner.TryRemove(folder).Should().BeTrue();

        Directory.Exists(literal).Should().BeFalse();
    }

    // T-F549: a literal root Windows would trim ("out." mid-path) was walked as nothing, so its
    // read-only file was never cleared and the recursive delete stopped.
    [Fact]
    public void TryRemove_LiteralRootWindowsWouldTrim_ReadOnlyFileInside_Removed()
    {
        string literalDestination = @"\\?\" + Path.Combine(_temp.Path, "out.");
        string root = Path.Combine(literalDestination, ".pakko-root");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        string file = Path.Combine(root, "sub", "staged.txt");
        File.WriteAllText(file, "x");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        TempOwner.TryRemove(root).Should().BeTrue();

        Directory.Exists(root).Should().BeFalse();
        Directory.Delete(literalDestination);
    }

    // T-F552: .NET and Windows take "\??\" as the "\\?\" prefix; Literal made "\\?\\??\C:\...", found
    // nothing there and reported the folder removed while it stayed.
    [Fact]
    public void TryRemove_NtPrefixedPath_RemovesTheFolder()
    {
        // "nt." - a name Windows trims: only a literal spelling reaches it, so the plain branch's refusal shows.
        string folder = Path.Combine(_temp.Path, "nt.");
        Directory.CreateDirectory(@"\\?\" + Path.Combine(folder, "sub"));
        File.WriteAllText(@"\\?\" + Path.Combine(folder, "sub", "a.txt"), "x");

        TempOwner.TryRemove(@"\??\" + folder).Should().BeTrue();

        Directory.Exists(@"\\?\" + folder).Should().BeFalse();
    }

    // T-F549: a NUL reached new FileInfo and threw ArgumentException out of a Core method.
    [Fact]
    public void TryRemove_LiteralPathWithNul_RefusedWithoutThrowing()
    {
        string path = @"\\?\" + Path.Combine(_temp.Path, "a\0b");

        TempOwner.TryRemove(path).Should().BeFalse();
    }

    // T-F533: the literal exception is only for \\?\; written plainly the same name still reaches the twin.
    [Fact]
    public void TryRemove_PlainPathWindowsWouldTrim_StillRefused()
    {
        string twin = Dir("out");

        TempOwner.TryRemove(Path.Combine(_temp.Path, "out.")).Should().BeFalse();

        Directory.Exists(twin).Should().BeTrue();
    }

    // T-F511: SweepStaleInTemp is public and, with no prefix, removes any old entry it cannot read
    // an owner from; outside %TEMP% such an entry may be the user's.
    [Fact]
    public void SweepStaleInTemp_FolderOutsideTemp_LeavesItsOldEntries()
    {
        string outside = Path.Combine(AppContext.BaseDirectory, "sweep-outside-" + Guid32);
        Directory.CreateDirectory(Path.Combine(outside, "photos"));
        try
        {
            Directory.SetCreationTimeUtc(Path.Combine(outside, "photos"), _now.AddDays(-30));

            TempOwner.SweepStaleInTemp(outside, "", TimeSpan.FromDays(1));

            Directory.Exists(Path.Combine(outside, "photos")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    // T-F511 review: the -si/-so staging root (%TEMP%\Archiver.CLI.Stdin) planted as a junction to a
    // user's folder. The sweep listed through it and removed every old entry it found there.
    [Fact]
    public void SweepStaleInTemp_RootIsALinkOutOfTemp_LeavesWhatIsBehindIt()
    {
        string outside = Path.Combine(AppContext.BaseDirectory, "sweep-behind-link-" + Guid32);
        Directory.CreateDirectory(Path.Combine(outside, "photos"));
        string link = Path.Combine(_temp.Path, "Archiver.CLI.Stdin");
        try
        {
            Directory.SetCreationTimeUtc(Path.Combine(outside, "photos"), _now.AddDays(-30));
            DirectoryJunction.Create(link, outside);

            TempOwner.SweepStaleInTemp(link, "", TimeSpan.FromDays(1));

            Directory.Exists(Path.Combine(outside, "photos")).Should().BeTrue("what a link reaches is not Pakko's");
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void SweepStaleInTemp_OldUnownedEntryInTemp_Removed()
    {
        string old = Dir("photos");
        Directory.SetCreationTimeUtc(old, _now.AddDays(-30));

        TempOwner.SweepStaleInTemp(_temp.Path, "", TimeSpan.FromDays(1));

        Directory.Exists(old).Should().BeFalse();
    }

    [Fact]
    public void TryRemove_FolderWithReadOnlyFileAndMissingPath_Removed()
    {
        string folder = Dir("staging");
        File.SetAttributes(Path.Combine(folder, "sub", "staged.txt"), FileAttributes.ReadOnly);

        TempOwner.TryRemove(folder).Should().BeTrue();
        TempOwner.TryRemove(folder).Should().BeTrue("a path already gone is removed");

        Directory.Exists(folder).Should().BeFalse();
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

    // T-F316: what a walk of a folder holding the run's destination must leave out.
    [Fact]
    public void IsOwnBelow_ThisProcessNamesBelowTheRoot_AreOwn()
    {
        const string root = @"C:\data\src";

        TempOwner.IsOwnBelow(root, Path.Combine(root, TempOwner.NewName(".pakko-a-", ".tmp"))).Should().BeTrue();
        TempOwner.IsOwnBelow(root, Path.Combine(root, "sub", TempOwner.NewName(".pakko-tmp-"), "chunk.bin")).Should().BeTrue();
        TempOwner.IsOwnBelow(root, Path.Combine(root, "a.txt")).Should().BeFalse();
        TempOwner.IsOwnBelow(root, Path.Combine(root, $".pakko-a-{Here}-{DeadPid}-1-{Guid32}.tmp"))
            .Should().BeFalse("another process's file is not this run's to hide");
    }

    [Fact]
    public void IsOwnBelow_RootItselfCarriesTheTag_ItsContentIsNotOwn()
    {
        string root = Path.Combine(@"C:\Temp", TempOwner.NewName(".pakko-x-"));

        TempOwner.IsOwnBelow(root, root).Should().BeFalse();
        TempOwner.IsOwnBelow(root, Path.Combine(root, "a.txt")).Should().BeFalse();
    }

    // T-F527: a folder (and what is in it) whose DACL lets the user nothing - what a compromised
    // sandboxed run can leave - is taken back by its owner and removed; a junction in it goes as a
    // link, and the folder it leads to keeps its files and its DACL.
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void TryRemove_AFolderThatLetsItsOwnerNothing_IsTakenBackAndRemoved()
    {
        string outside = Path.Combine(_temp.Path, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");
        string outsideDacl = RawDacl.Sddl(outside);
        string root = Path.Combine(_temp.Path, TempOwner.NewName(".pakko-sandbox-"));
        string locked = Path.Combine(root, "out", "x");
        Directory.CreateDirectory(Path.Combine(locked, "sub"));
        File.WriteAllText(Path.Combine(locked, "sub", "f.txt"), "f");
        ZipArchiveServiceArchiveTests.TryCreateJunction(Path.Combine(locked, "link"), outside).Should().BeTrue();
        foreach (string path in new[] { Path.Combine(locked, "sub", "f.txt"), Path.Combine(locked, "sub"), locked })
            RawDacl.Write(path, "D:P(A;OICI;FA;;;SY)");
        Action list = () => Directory.GetFiles(locked);
        list.Should().Throw<UnauthorizedAccessException>();

        TempOwner.TryRemove(root).Should().BeFalse("only a quarantine root, private to the user from its birth, is taken back");
        Directory.Exists(locked).Should().BeTrue();

        TempOwner.TryRemoveQuarantine(root).Should().BeTrue();

        Directory.Exists(root).Should().BeFalse();
        File.ReadAllText(Path.Combine(outside, "keep.txt")).Should().Be("keep");
        RawDacl.Sddl(outside).Should().Be(outsideDacl);
    }

    // A sweep of a folder another principal can write to (a destination's work roots) takes nothing back:
    // a planted folder there could steer the walk. The quarantine's sweep does.
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void SweepStale_TakesBackOnlyForTheQuarantine()
    {
        string stale = Path.Combine(_temp.Path, ".pakko-w-" + "m00000000-1-1-" + Guid.NewGuid().ToString("N"));
        string locked = Path.Combine(stale, "x");
        Directory.CreateDirectory(locked);
        File.WriteAllText(Path.Combine(locked, "f.txt"), "f");
        RawDacl.Write(locked, "D:P(A;OICI;FA;;;SY)");
        var probe = new TempOwnerProbe("00000000", (_, _) => false, DateTime.UtcNow);

        TempOwner.SweepStale(_temp.Path, ".pakko-w-", "", TempScope.Destination, probe);

        Directory.Exists(locked).Should().BeTrue();

        TempOwner.SweepStale(_temp.Path, ".pakko-w-", "", TempScope.Destination with { TakeBack = true }, probe);

        Directory.Exists(stale).Should().BeFalse();
    }
}
