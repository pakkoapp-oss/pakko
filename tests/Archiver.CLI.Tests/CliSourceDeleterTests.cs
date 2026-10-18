using Archiver.CLI.Tests.Subprocess;
using FluentAssertions;

namespace Archiver.CLI.Tests;

/// <summary>
/// T-F411: -sdel's deletion, as 7-Zip's UpdateArchive does it (Update.cpp 26.02): only the files
/// listed before archiving whose size and times are still the same, then the folders, deepest
/// first, only once empty. Pakko never enters or deletes a link, and refuses a volume root or the
/// current folder (or one holding it) before anything is written.
/// </summary>
public sealed class CliSourceDeleterTests : IDisposable
{
    private readonly string _dir = CliFixtureFiles.CreateScratchDir();
    private readonly List<string> _junctions = [];

    public void Dispose()
    {
        foreach (string junction in _junctions)
        {
            try { Directory.Delete(junction); } catch (IOException) { /* already gone */ }
        }
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    // src\a.txt, src\d\b.txt
    private string CreateTree(string name = "src")
    {
        string src = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.Combine(src, "d"));
        File.WriteAllText(Path.Combine(src, "a.txt"), "a");
        File.WriteAllText(Path.Combine(src, "d", "b.txt"), "b");
        return src;
    }

    private bool TryCreateJunction(string link, string target)
    {
        using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/d", "/c", "mklink", "/J", link, target])
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        mklink.WaitForExit();
        if (mklink.ExitCode != 0)
            return false;
        _junctions.Add(link);
        return true;
    }

    // --- refusals ---

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
    [InlineData(@"\\server\share")]
    public void Refusal_VolumeRoot_IsRefused(string source)
    {
        CliSourceDeleter.Refusal([source], @"C:\work").Should().Contain("volume root");
    }

    [Theory]
    [InlineData(@"C:\work")]
    [InlineData(@"C:\WORK\")]
    public void Refusal_CurrentFolderOrAFolderHoldingIt_IsRefused(string source)
    {
        CliSourceDeleter.Refusal([source], @"C:\work").Should().Contain("current folder");
        CliSourceDeleter.Refusal([source], @"C:\work\sub").Should().Contain("current folder");
    }

    [Theory]
    [InlineData(@"C:\work\sub2")]
    [InlineData(@"C:\work\sub\inner")]
    [InlineData(@"C:\work\sub.txt")]
    public void Refusal_FolderBesideOrBelowTheCurrentOne_IsAllowed(string source)
    {
        CliSourceDeleter.Refusal([source], @"C:\work\sub").Should().BeNull();
    }

    [Fact]
    public void Refusal_PrefixThatIsNotAParent_IsAllowed()
    {
        // "C:\wo" is not a parent of "C:\work\sub" - only a whole name is.
        CliSourceDeleter.Refusal([@"C:\wo"], @"C:\work\sub").Should().BeNull();
    }

    // --- deletion ---

    [Fact]
    public void Delete_UnchangedFolder_IsGoneEntirely()
    {
        string src = CreateTree();
        CliSourceSnapshot snapshot = CliSourceSnapshot.Take([src]);

        CliDeleteOutcome outcome = snapshot.DeleteUnchanged([src]);

        outcome.Kept.Should().BeEmpty();
        outcome.Failed.Should().BeEmpty();
        Directory.Exists(src).Should().BeFalse();
    }

    [Fact]
    public void Delete_SingleFileSource_IsDeleted()
    {
        string src = CreateTree();
        string file = Path.Combine(src, "a.txt");
        CliSourceSnapshot snapshot = CliSourceSnapshot.Take([file]);

        snapshot.DeleteUnchanged([file]);

        File.Exists(file).Should().BeFalse();
        File.Exists(Path.Combine(src, "d", "b.txt")).Should().BeTrue();
    }

    // 7-Zip compares size, modification and creation time; any other change is not archived.
    [Fact]
    public void Delete_FileChangedAfterTheList_IsKeptWithItsFolders()
    {
        string src = CreateTree();
        CliSourceSnapshot snapshot = CliSourceSnapshot.Take([src]);
        string changed = Path.Combine(src, "d", "b.txt");
        File.WriteAllText(changed, "b changed");

        CliDeleteOutcome outcome = snapshot.DeleteUnchanged([src]);

        outcome.Kept.Should().Equal(changed);
        outcome.Failed.Should().BeEmpty();
        File.ReadAllText(changed).Should().Be("b changed");
        File.Exists(Path.Combine(src, "a.txt")).Should().BeFalse();
    }

    [Fact]
    public void Delete_FileTouchedAfterTheList_IsKept()
    {
        string src = CreateTree();
        CliSourceSnapshot snapshot = CliSourceSnapshot.Take([src]);
        string touched = Path.Combine(src, "a.txt");
        File.SetLastWriteTimeUtc(touched, File.GetLastWriteTimeUtc(touched).AddSeconds(1));

        CliDeleteOutcome outcome = snapshot.DeleteUnchanged([src]);

        outcome.Kept.Should().Equal(touched);
        File.Exists(touched).Should().BeTrue();
    }

    [Fact]
    public void Delete_FileCreatedAfterTheList_IsLeftAndTheFolderStays()
    {
        string src = CreateTree();
        CliSourceSnapshot snapshot = CliSourceSnapshot.Take([src]);
        string late = Path.Combine(src, "d", "late.txt");
        File.WriteAllText(late, "late");

        CliDeleteOutcome outcome = snapshot.DeleteUnchanged([src]);

        outcome.Failed.Should().BeEmpty("a folder still holding something stays, as in 7-Zip, without an error");
        File.Exists(late).Should().BeTrue();
        File.Exists(Path.Combine(src, "d", "b.txt")).Should().BeFalse();
    }

    [Fact]
    public void Delete_ReadOnlyFile_IsDeleted()
    {
        string src = CreateTree();
        File.SetAttributes(Path.Combine(src, "a.txt"), FileAttributes.ReadOnly);
        CliSourceSnapshot snapshot = CliSourceSnapshot.Take([src]);

        CliDeleteOutcome outcome = snapshot.DeleteUnchanged([src]);

        outcome.Failed.Should().BeEmpty();
        Directory.Exists(src).Should().BeFalse();
    }

    // Only the sources Core reports fully archived are given; the rest are not touched.
    [Fact]
    public void Delete_SourceNotFullyArchived_IsNotTouched()
    {
        string src = CreateTree();
        string other = CreateTree("other");
        CliSourceSnapshot snapshot = CliSourceSnapshot.Take([src, other]);

        snapshot.DeleteUnchanged([other]);

        File.Exists(Path.Combine(src, "a.txt")).Should().BeTrue();
        Directory.Exists(other).Should().BeFalse();
    }

    [Fact]
    public void Delete_CompletedPathSpelledDifferently_StillMatches()
    {
        string src = CreateTree();
        CliSourceSnapshot snapshot = CliSourceSnapshot.Take([src + Path.DirectorySeparatorChar]);

        snapshot.DeleteUnchanged([src.ToUpperInvariant()]);

        Directory.Exists(src).Should().BeFalse();
    }

    [Fact]
    public void Delete_JunctionInside_NeitherItNorItsTargetIsTouched()
    {
        string src = CreateTree();
        string target = CreateTree("target");
        if (!TryCreateJunction(Path.Combine(src, "j"), target))
            return; // no junctions on this volume
        CliSourceSnapshot snapshot = CliSourceSnapshot.Take([src]);

        snapshot.DeleteUnchanged([src]);

        Directory.Exists(Path.Combine(src, "j")).Should().BeTrue();
        File.Exists(Path.Combine(target, "a.txt")).Should().BeTrue();
        File.Exists(Path.Combine(target, "d", "b.txt")).Should().BeTrue();
        File.Exists(Path.Combine(src, "a.txt")).Should().BeFalse();
    }

    [Fact]
    public void Delete_JunctionAsTheSource_IsNotTouched()
    {
        string target = CreateTree("target");
        string link = Path.Combine(_dir, "link");
        if (!TryCreateJunction(link, target))
            return; // no junctions on this volume
        CliSourceSnapshot snapshot = CliSourceSnapshot.Take([link]);

        CliDeleteOutcome outcome = snapshot.DeleteUnchanged([link]);

        outcome.Links.Should().Equal(link);
        Directory.Exists(link).Should().BeTrue();
        File.Exists(Path.Combine(target, "a.txt")).Should().BeTrue();
    }

    [Fact]
    public void Delete_FileHeldOpen_IsAFailureAndTheRestIsDeleted()
    {
        string src = CreateTree();
        string held = Path.Combine(src, "a.txt");
        CliSourceSnapshot snapshot = CliSourceSnapshot.Take([src]);

        CliDeleteOutcome outcome;
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read))
            outcome = snapshot.DeleteUnchanged([src]);

        outcome.Failed.Should().ContainSingle(f => f.Path == held);
        File.Exists(held).Should().BeTrue();
        File.Exists(Path.Combine(src, "d", "b.txt")).Should().BeFalse();
    }

    // A folder the list could not read is never deleted from: what it held is unknown.
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Delete_FolderThatCouldNotBeListed_SourceIsNotTouched()
    {
        string src = CreateTree();
        string denied = Path.Combine(src, "d");
        var security = new DirectoryInfo(denied).GetAccessControl();
        var rule = new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.ListDirectory, System.Security.AccessControl.AccessControlType.Deny);
        security.AddAccessRule(rule);
        new DirectoryInfo(denied).SetAccessControl(security);
        try
        {
            CliSourceSnapshot snapshot = CliSourceSnapshot.Take([src]);

            CliDeleteOutcome outcome = snapshot.DeleteUnchanged([src]);

            outcome.Unlisted.Should().Equal(src);
            File.Exists(Path.Combine(src, "a.txt")).Should().BeTrue();
        }
        finally
        {
            security.RemoveAccessRule(rule);
            new DirectoryInfo(denied).SetAccessControl(security);
        }
    }
}
