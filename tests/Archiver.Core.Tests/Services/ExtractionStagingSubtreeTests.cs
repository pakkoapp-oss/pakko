using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F565: merging into a folder that exists, a staged folder whose destination is not there yet is
/// moved whole by one rename, as the fast path moves all of staging (T-F161), when its folder in the
/// destination passes down what its folder in staging did. Anything else is merged file by file.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ExtractionStagingSubtreeTests : IDisposable
{
    private static readonly SecurityIdentifier Everyone = new(WellKnownSidType.WorldSid, null);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string ExistingDestination()
    {
        string dest = Path.Combine(_temp.Path, "dest");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "kept.txt"), "kept");
        return dest;
    }

    private static Action<string, string> CountingMove(List<(string From, string To)> moves) => (from, to) =>
    {
        moves.Add((from, to));
        Directory.Move(from, to);
    };

    [Fact]
    public void Merge_ANewFolder_IsMovedWholeOnce()
    {
        string dest = ExistingDestination();
        var moves = new List<(string From, string To)>();
        IReadOnlyList<string> locked;

        using (var staging = ExtractionStaging.Create(dest))
        {
            Stage(staging, "a.txt");
            Stage(staging, Path.Combine("new", "b.txt"));
            Stage(staging, Path.Combine("new", "deep", "c.txt"));
            locked = staging.CommitInto(dest, CountingMove(moves));
        }

        locked.Should().BeEmpty();
        moves.Should().ContainSingle().Which.To.Should().Be(Path.Combine(dest, "new"));
        File.ReadAllText(Path.Combine(dest, "a.txt")).Should().Be("a.txt");
        File.ReadAllText(Path.Combine(dest, "new", "b.txt")).Should().Be(Path.Combine("new", "b.txt"));
        File.ReadAllText(Path.Combine(dest, "new", "deep", "c.txt")).Should().Be(Path.Combine("new", "deep", "c.txt"));
        File.ReadAllText(Path.Combine(dest, "kept.txt")).Should().Be("kept");
        string c = Path.Combine(dest, "new", "deep", "c.txt");
        Rules(c).Should().Equal(RulesOfAFileMadeBeside(c));
        string folder = Path.Combine(dest, "new");
        FolderRules(folder).Should().Equal(RulesOfAFolderMadeBeside(folder));
    }

    // T-F408: every folder and file that arrived is named once, and nothing is reported locked
    // for a file that left staging inside a folder moved whole.
    [Fact]
    public void Merge_ANewFolderWithNaming_NamesEachItemOnceAndLocksNothing()
    {
        string dest = ExistingDestination();
        var named = new List<string>();
        var moves = new List<(string From, string To)>();
        IReadOnlyList<string> locked;

        using (var staging = ExtractionStaging.Create(dest))
        {
            Stage(staging, "a.txt");
            Stage(staging, Path.Combine("new", "b.txt"));
            Stage(staging, Path.Combine("new", "deep", "c.txt"));
            locked = staging.CommitInto(dest, CountingMove(moves), named.Add);
        }

        locked.Should().BeEmpty();
        moves.Should().ContainSingle();
        named.Should().OnlyHaveUniqueItems();
        named.Should().Contain([
            Path.Combine(dest, "a.txt"),
            Path.Combine(dest, "new") + Path.DirectorySeparatorChar,
            Path.Combine(dest, "new", "deep") + Path.DirectorySeparatorChar,
            Path.Combine(dest, "new", "b.txt"),
            Path.Combine(dest, "new", "deep", "c.txt"),
        ]);
    }

    [Fact]
    public void Merge_AFolderThatExists_IsMergedFileByFile()
    {
        string dest = ExistingDestination();
        Directory.CreateDirectory(Path.Combine(dest, "s"));
        File.WriteAllText(Path.Combine(dest, "s", "old.txt"), "old");
        var moves = new List<(string From, string To)>();

        using (var staging = ExtractionStaging.Create(dest))
        {
            Stage(staging, Path.Combine("s", "b.txt"));
            staging.CommitInto(dest, CountingMove(moves)).Should().BeEmpty();
        }

        moves.Should().BeEmpty();
        File.ReadAllText(Path.Combine(dest, "s", "b.txt")).Should().Be(Path.Combine("s", "b.txt"));
        File.ReadAllText(Path.Combine(dest, "s", "old.txt")).Should().Be("old");
    }

    // T-F419: a junction already in the destination does not carry the commit outside it, moved
    // whole or not.
    [Fact]
    public void Merge_ANewFolderBelowAJunction_IsLeftOut()
    {
        string dest = ExistingDestination();
        string outside = Path.Combine(_temp.Path, "outside");
        Directory.CreateDirectory(outside);
        string link = Path.Combine(dest, "link");
        if (!ZipArchiveServiceArchiveTests.TryCreateJunction(link, outside))
            return;
        try
        {
            using (var staging = ExtractionStaging.Create(dest))
            {
                Stage(staging, Path.Combine("link", "new", "b.txt"));
                staging.CommitInto(dest).Should().BeEmpty();
                staging.LeftOutThroughLink.Should().NotBeEmpty();
            }

            Directory.EnumerateFileSystemEntries(outside).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(link, recursive: false);
        }
    }

    // A junction whose target is gone stands where the new folder would go: nothing is written
    // through it, and it is reported as a link.
    [Fact]
    public void Merge_ADanglingJunctionWhereTheNewFolderGoes_IsLeftOut()
    {
        string dest = ExistingDestination();
        string gone = Path.Combine(_temp.Path, "gone");
        Directory.CreateDirectory(gone);
        string link = Path.Combine(dest, "new");
        if (!ZipArchiveServiceArchiveTests.TryCreateJunction(link, gone))
            return;
        Directory.Delete(gone);
        try
        {
            using (var staging = ExtractionStaging.Create(dest))
            {
                Stage(staging, Path.Combine("new", "b.txt"));
                staging.CommitInto(dest).Should().BeEmpty();
                staging.LeftOutThroughLink.Should().NotBeEmpty();
            }

            Directory.Exists(gone).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(link, recursive: false);
        }
    }

    // T-F532: the folder above the new one passes down an entry staging's did not; a folder moved
    // whole would keep staging's entries, so its files take their own folder's, as merged ones do.
    [Fact]
    public void Merge_ANewFolderWhoseParentGivesOtherEntries_ItsFilesTakeThem()
    {
        string dest = ExistingDestination();
        Directory.CreateDirectory(Path.Combine(dest, "s"));
        Give(Path.Combine(dest, "s"), PropagationFlags.None);
        var moves = new List<(string From, string To)>();

        using (var staging = ExtractionStaging.Create(dest))
        {
            Stage(staging, Path.Combine("s", "new", "b.txt"));
            staging.CommitInto(dest, CountingMove(moves)).Should().BeEmpty();
            staging.PermissionsNotApplied.Should().BeEmpty();
        }

        moves.Should().BeEmpty();

        string b = Path.Combine(dest, "s", "new", "b.txt");
        Rules(b).Should().Equal(RulesOfAFileMadeBeside(b));
        Rules(b).Should().Contain(r => r.StartsWith(Everyone.Value, StringComparison.Ordinal));
    }

    // An entry the destination gives its children only reaches the new folder and stops there.
    [Fact]
    public void Merge_ANewFolderBelowAChildrenOnlyEntry_TheEntryReachesTheFolderNotBelow()
    {
        string dest = ExistingDestination();
        Give(dest, PropagationFlags.NoPropagateInherit);
        var moves = new List<(string From, string To)>();

        using (var staging = ExtractionStaging.Create(dest))
        {
            Stage(staging, Path.Combine("new", "b.txt"));
            staging.CommitInto(dest, CountingMove(moves)).Should().BeEmpty();
        }

        moves.Should().BeEmpty();
        string folder = Path.Combine(dest, "new");
        FolderRules(folder).Should().Equal(RulesOfAFolderMadeBeside(folder));
        FolderRules(folder).Should().Contain(r => r.StartsWith(Everyone.Value, StringComparison.Ordinal));
        string b = Path.Combine(folder, "b.txt");
        Rules(b).Should().Equal(RulesOfAFileMadeBeside(b));
        Rules(b).Should().NotContain(r => r.StartsWith(Everyone.Value, StringComparison.Ordinal));
    }

    // T-F161: a folder that cannot be moved whole (a file in it held open) is merged file by file;
    // its neighbours still move whole.
    [Fact]
    public void Merge_OneNewFolderCannotMove_ItIsMergedAndTheOthersMoveWhole()
    {
        string dest = ExistingDestination();
        var moves = new List<(string From, string To)>();
        IReadOnlyList<string> locked;

        using (var staging = ExtractionStaging.Create(dest))
        {
            Stage(staging, Path.Combine("n1", "a.txt"));
            Stage(staging, Path.Combine("n2", "b.txt"));
            void Move(string from, string to)
            {
                moves.Add((from, to));
                if (Path.GetFileName(from) == "n1")
                    throw new IOException($"Access to the path '{from}' is denied.");
                Directory.Move(from, to);
            }
            locked = staging.CommitInto(dest, Move);
        }

        locked.Should().BeEmpty();
        moves.Select(m => Path.GetFileName(m.To)).Should().BeEquivalentTo(["n1", "n2"]);
        File.ReadAllText(Path.Combine(dest, "n1", "a.txt")).Should().Be(Path.Combine("n1", "a.txt"));
        File.ReadAllText(Path.Combine(dest, "n2", "b.txt")).Should().Be(Path.Combine("n2", "b.txt"));
    }

    // The entry stops at the new folder, which is created; one level down the folders pass down the
    // same again, so the deeper one moves whole.
    [Fact]
    public void Merge_BelowAFolderCreatedForAChildrenOnlyEntry_TheNextFolderMovesWhole()
    {
        string dest = ExistingDestination();
        Give(dest, PropagationFlags.NoPropagateInherit);
        var moves = new List<(string From, string To)>();

        using (var staging = ExtractionStaging.Create(dest))
        {
            Stage(staging, Path.Combine("new", "deep", "c.txt"));
            staging.CommitInto(dest, CountingMove(moves)).Should().BeEmpty();
        }

        moves.Should().ContainSingle().Which.To.Should().Be(Path.Combine(dest, "new", "deep"));
        string deep = Path.Combine(dest, "new", "deep");
        FolderRules(deep).Should().Equal(RulesOfAFolderMadeBeside(deep));
        string c = Path.Combine(deep, "c.txt");
        Rules(c).Should().Equal(RulesOfAFileMadeBeside(c));
    }

    // T-F298: the folders inside one moved whole get their recorded times too.
    [Fact]
    public void Merge_ANewFolderMovedWhole_ItsFoldersGetTheirRecordedTimes()
    {
        string dest = ExistingDestination();
        var time = new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc);

        using (var staging = ExtractionStaging.Create(dest))
        {
            Stage(staging, Path.Combine("new", "deep", "c.txt"));
            staging.RecordFolderTime("new", time);
            staging.RecordFolderTime(Path.Combine("new", "deep"), time);
            staging.CommitInto(dest).Should().BeEmpty();
        }

        Directory.GetLastWriteTimeUtc(Path.Combine(dest, "new")).Should().Be(time);
        Directory.GetLastWriteTimeUtc(Path.Combine(dest, "new", "deep")).Should().Be(time);
    }

    // A folder that cannot be created (a file holds its name) ends the commit as it did before; a
    // folder moved whole ahead of it has arrived, so it is named (T-F408) and keeps its time (T-F298).
    [Fact]
    public void Merge_AFolderThatCannotBeCreatedAfterOneMovedWhole_TheMovedOneIsNamedAndTimed()
    {
        string dest = ExistingDestination();
        File.WriteAllText(Path.Combine(dest, "z"), "a file in the folder's place");
        var named = new List<string>();
        var time = new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc);

        using (var staging = ExtractionStaging.Create(dest))
        {
            Stage(staging, Path.Combine("a", "deep", "x.txt"));
            Stage(staging, Path.Combine("z", "y.txt"));
            staging.RecordFolderTime(Path.Combine("a", "deep"), time);
            Action commit = () => staging.CommitInto(dest, committed: named.Add);
            commit.Should().Throw<IOException>();
        }

        File.Exists(Path.Combine(dest, "a", "deep", "x.txt")).Should().BeTrue();
        named.Should().Contain([
            Path.Combine(dest, "a") + Path.DirectorySeparatorChar,
            Path.Combine(dest, "a", "deep") + Path.DirectorySeparatorChar,
            Path.Combine(dest, "a", "deep", "x.txt"),
        ]);
        named.Should().OnlyHaveUniqueItems();
        Directory.GetLastWriteTimeUtc(Path.Combine(dest, "a", "deep")).Should().Be(time);
        File.ReadAllText(Path.Combine(dest, "z")).Should().Be("a file in the folder's place");
    }

    // A compressed folder gives a folder made in it its compression; one moved whole would keep
    // staging's, so the new folder is created there instead.
    [Fact]
    public void Merge_ANewFolderInACompressedFolder_IsCreatedCompressed()
    {
        string dest = ExistingDestination();
        string s = Path.Combine(dest, "s");
        Directory.CreateDirectory(s);
        if (!TryCompress(s))
            return;
        var moves = new List<(string From, string To)>();

        using (var staging = ExtractionStaging.Create(dest))
        {
            Stage(staging, Path.Combine("s", "new", "b.txt"));
            staging.CommitInto(dest, CountingMove(moves)).Should().BeEmpty();
        }

        moves.Should().BeEmpty();
        File.GetAttributes(Path.Combine(s, "new")).Should().HaveFlag(FileAttributes.Compressed);
    }

    private static void Stage(ExtractionStaging staging, string relative)
    {
        string file = Path.Combine(staging.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, relative);
    }

    private static void Give(string folder, PropagationFlags propagation)
    {
        var info = new DirectoryInfo(folder);
        DirectorySecurity security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(Everyone, FileSystemRights.ReadData,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, propagation, AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    // A volume without compression (ReFS, FAT) skips the test.
    private static bool TryCompress(string folder)
    {
        using var compact = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            Path.Combine(Environment.SystemDirectory, "compact.exe"), ["/c", "/q", folder])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        if (compact is null)
            return false;
        compact.StandardOutput.ReadToEnd();
        compact.StandardError.ReadToEnd();
        compact.WaitForExit();
        return compact.ExitCode == 0 && File.GetAttributes(folder).HasFlag(FileAttributes.Compressed);
    }

    private static List<string> RulesOfAFileMadeBeside(string file)
    {
        string probe = Path.Combine(Path.GetDirectoryName(file)!, "probe-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(probe, "p");
        try
        {
            return Rules(probe);
        }
        finally
        {
            File.Delete(probe);
        }
    }

    private static List<string> RulesOfAFolderMadeBeside(string folder)
    {
        string probe = Path.Combine(Path.GetDirectoryName(folder)!, "probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(probe);
        try
        {
            return FolderRules(probe);
        }
        finally
        {
            Directory.Delete(probe);
        }
    }

    private static List<string> Rules(string file) =>
        [.. new FileInfo(file).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(r => $"{r.IdentityReference.Value} {r.FileSystemRights} {r.AccessControlType} {r.IsInherited}")
            .Order(StringComparer.Ordinal)];

    private static List<string> FolderRules(string folder) =>
        [.. new DirectoryInfo(folder).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(r => $"{r.IdentityReference.Value} {r.FileSystemRights} {r.InheritanceFlags} {r.PropagationFlags} {r.AccessControlType} {r.IsInherited}")
            .Order(StringComparer.Ordinal)];
}
