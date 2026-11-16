using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Archiver.Core.IO;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using Archiver.Core.Tests.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.IO;

/// <summary>
/// T-F528: one operation's work goes into one hidden root next to its destination, in the nearest
/// folder of it that exists; each role is a new folder of an unguessable name in it, and the root goes
/// with the operation. A root or an older version's folder a killed run left is swept when the next
/// root is made there.
/// </summary>
public sealed class WorkRootTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Folder_IsANewFolderInOneHiddenRoot_InTheNearestFolderThatExists()
    {
        string destination = Path.Combine(_temp.Path, "new", "deeper");
        using var work = WorkRoot.Near(destination);

        string first = work.Folder("x");
        string second = work.Folder("x");

        string root = Path.GetDirectoryName(first)!;
        Path.GetDirectoryName(root).Should().Be(_temp.Path);
        Path.GetFileName(root).Should().StartWith(WorkRoot.Prefix + TempOwner.CurrentTag + "-");
        new DirectoryInfo(root).Attributes.Should().HaveFlag(FileAttributes.Hidden);
        Path.GetDirectoryName(second).Should().Be(root);
        second.Should().NotBe(first);
        Path.GetFileName(first).Should().MatchRegex("^x-[0-9a-f]{32}$");
        Directory.Exists(Path.Combine(_temp.Path, "new")).Should().BeFalse("the destination is created only by the commit");
        Directory.GetFileSystemEntries(_temp.Path).Should().ContainSingle();
    }

    [Fact]
    public void NoRole_LeavesNothing()
    {
        using (WorkRoot.Near(_temp.Path))
        {
            Directory.GetFileSystemEntries(_temp.Path).Should().BeEmpty();
        }
        Directory.GetFileSystemEntries(_temp.Path).Should().BeEmpty();
    }

    [Fact]
    public void Dispose_RemovesTheRoot_AndALinkInIt_NeverItsTarget()
    {
        string outside = Path.Combine(_temp.Path, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "the user's");
        string destination = Path.Combine(_temp.Path, "dest");
        Directory.CreateDirectory(destination);

        string root;
        using (var work = WorkRoot.Near(destination))
        {
            string staged = work.Folder("x");
            root = Path.GetDirectoryName(staged)!;
            File.WriteAllText(Path.Combine(staged, "a.txt"), "staged");
            ZipArchiveServiceArchiveTests.TryCreateJunction(Path.Combine(staged, "link"), outside).Should().BeTrue();
        }

        Directory.Exists(root).Should().BeFalse();
        File.ReadAllText(Path.Combine(outside, "keep.txt")).Should().Be("the user's");
    }

    // A destination spelled through "\\.\" or "\\?\" (T-F512's matrix) gets its root there, and loses it.
    [Theory]
    [InlineData(@"\\.\")]
    [InlineData(@"\\?\")]
    public void DevicePrefixedDestination_RootIsMadeAndRemoved(string prefix)
    {
        using (var work = WorkRoot.Near(prefix + _temp.Path))
        {
            string staged = work.Folder("x");
            File.WriteAllText(Path.Combine(staged, "a.txt"), "staged");
            Directory.GetFileSystemEntries(_temp.Path).Should().ContainSingle();
        }
        Directory.GetFileSystemEntries(_temp.Path).Should().BeEmpty();
    }

    // T-F533: DestinationFolder.TryCreate takes "\\?\C:\w\out." as typed; its root stayed behind, refused
    // by TryRemove as a name Windows trims, until the next run there swept it.
    [Fact]
    public void LiteralDestinationWindowsWouldTrim_RootIsRemoved()
    {
        string destination = @"\\?\" + Path.Combine(_temp.Path, "out.");
        Directory.CreateDirectory(destination);
        using (var work = WorkRoot.Near(destination))
        {
            string staged = work.Folder("x");
            File.WriteAllText(Path.Combine(staged, "a.txt"), "staged");
        }
        Directory.GetFileSystemEntries(destination).Should().BeEmpty();
        Directory.Delete(destination);
    }

    // A killed run's root, and the staging folder an older version left, are swept; a live run's stay.
    [Fact]
    public void FirstRole_SweepsDeadRootsAndOlderStaging_KeepsLiveOnes()
    {
        string dead = Path.Combine(_temp.Path, DeadOwner.Name(WorkRoot.Prefix));
        string deadStaging = Path.Combine(_temp.Path, DeadOwner.Name(".pakko-x-"));
        string live = Path.Combine(_temp.Path, TempOwner.NewName(WorkRoot.Prefix));
        foreach (string folder in new[] { dead, deadStaging, live })
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "plain.txt"), "plaintext");
        }

        using var work = WorkRoot.Near(_temp.Path);
        _ = work.Folder("x");

        Directory.Exists(dead).Should().BeFalse();
        Directory.Exists(deadStaging).Should().BeFalse();
        Directory.Exists(live).Should().BeTrue();
    }

    // A name taken already, by a folder or a link planted there, is never written through.
    [Fact]
    public void CreateNewFolder_ATakenName_Throws()
    {
        string taken = Path.Combine(_temp.Path, "taken");
        Directory.CreateDirectory(taken);
        string link = Path.Combine(_temp.Path, "link");
        ZipArchiveServiceArchiveTests.TryCreateJunction(link, taken).Should().BeTrue();

        FluentActions.Invoking(() => WorkRoot.CreateNewFolder(taken)).Should().Throw<IOException>();
        FluentActions.Invoking(() => WorkRoot.CreateNewFolder(link)).Should().Throw<IOException>();
        Directory.GetFileSystemEntries(taken).Should().BeEmpty();
    }

    [Fact]
    public void CreateNewFolder_ALongPath_IsCreated()
    {
        string path = _temp.Path;
        while (path.Length < 300)
            path = Path.Combine(path, new string('d', 40));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        WorkRoot.CreateNewFolder(path);

        Directory.Exists(path).Should().BeTrue();
    }

    // An entry's flags are two letters each; NP inside a SID or a right is not the flag.
    [Theory]
    [InlineData("D:(A;OICINP;FR;;;WD)", true)]
    [InlineData("D:PAI(A;OICIIDNP;0x1200a9;;;S-1-5-21-1)(A;OICI;FA;;;SY)", true)]
    [InlineData("D:(A;OICI;FA;;;SY)(A;;FR;;;S-1-5-21-1NP)", false)]
    [InlineData("D:(A;CIONP;FA;;;SY)", false)]
    [InlineData("D:", false)]
    public void HasNoPropagateEntry_ReadsTheFlagsField(string sddl, bool expected) =>
        Archiver.Core.Services.Sandbox.QuarantineAcl.HasNoPropagateEntry(sddl).Should().Be(expected);

    // Step 0 of T-F528 (measured): an entry the destination's folder gives its children only
    // (NO_PROPAGATE) does not reach a folder made one level deeper, and a rename keeps what a folder
    // has. A folder the commit renamed into place gets it as a folder made there would.
    [Fact]
    [SupportedOSPlatform("windows")]
    public void FastPathCommit_FolderGetsWhatAFolderMadeThereGets()
    {
        string parent = Path.Combine(_temp.Path, "parent");
        Directory.CreateDirectory(parent);
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var info = new DirectoryInfo(parent);
        DirectorySecurity security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.ReadData,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.NoPropagateInherit, AccessControlType.Allow));
        info.SetAccessControl(security);
        string actualDest = Path.Combine(parent, "out");

        using (var staging = ExtractionStaging.Create(parent))
        {
            File.WriteAllText(Path.Combine(staging.Path, "a.txt"), "a");
            staging.CommitInto(actualDest).Should().BeEmpty();
        }

        string direct = Path.Combine(parent, "direct");
        Directory.CreateDirectory(direct);
        Rules(actualDest).Should().Equal(Rules(direct));
        Rules(actualDest).Should().Contain(r => r.Contains(everyone.Value, StringComparison.Ordinal));
    }

    // T-F528 opus review: another account can put a junction where the commit renamed the folder,
    // between the rename and the ACL call. The call opens the name itself and leaves a link alone, so
    // the folder it points to keeps its own entries.
    [Fact]
    [SupportedOSPlatform("windows")]
    public void TakeParentEntries_AJunctionInTheMovedFoldersPlace_LeavesItsTargetAlone()
    {
        string parent = Path.Combine(_temp.Path, "parent");
        Directory.CreateDirectory(parent);
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var parentInfo = new DirectoryInfo(parent);
        DirectorySecurity parentSecurity = parentInfo.GetAccessControl();
        parentSecurity.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.ReadData,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.NoPropagateInherit, AccessControlType.Allow));
        parentSecurity.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinGuestsSid, null), FileSystemRights.ReadData,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        parentInfo.SetAccessControl(parentSecurity);
        string target = Path.Combine(_temp.Path, "private");
        Directory.CreateDirectory(target);
        var targetInfo = new DirectoryInfo(target);
        DirectorySecurity targetSecurity = targetInfo.GetAccessControl();
        targetSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
        targetInfo.SetAccessControl(targetSecurity);
        List<string> before = Rules(target);
        string inner = Path.Combine(target, "inner");
        Directory.CreateDirectory(inner);
        var innerInfo = new DirectoryInfo(inner);
        DirectorySecurity innerSecurity = innerInfo.GetAccessControl();
        innerSecurity.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.ReadData, AccessControlType.Allow));
        innerInfo.SetAccessControl(innerSecurity);
        List<string> innerBefore = Rules(inner);
        string moved = Path.Combine(parent, "out");
        ZipArchiveServiceArchiveTests.TryCreateJunction(moved, target).Should().BeTrue();

        WorkRoot.TakeParentEntries(moved);

        Rules(target).Should().Equal(before);
        Rules(inner).Should().Equal(innerBefore);
        new DirectoryInfo(target).GetAccessControl().AreAccessRulesProtected.Should().BeTrue();
    }

    [SupportedOSPlatform("windows")]
    private static List<string> Rules(string folder) =>
        [.. new DirectoryInfo(folder).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(r => $"{r.IdentityReference.Value} {r.FileSystemRights} {r.AccessControlType} {r.InheritanceFlags} {r.PropagationFlags} {r.IsInherited}")
            .Order(StringComparer.Ordinal)];
}
