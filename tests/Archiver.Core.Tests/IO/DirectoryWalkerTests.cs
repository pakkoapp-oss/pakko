using System.Runtime.Versioning;
using Archiver.Core.IO;
using Archiver.Core.Tests.Helpers;
using Archiver.Core.Tests.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.IO;

// T-F236/T-F237/T-F251: the shared folder walk — order, empty folders, unreadable folders and
// reparse points, independent of any one consumer.
[SupportedOSPlatform("windows")]
public sealed class DirectoryWalkerTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Rel(WalkEntry e) => Path.GetRelativePath(_temp.Path, e.Info.FullName).Replace('\\', '/');

    [Fact]
    public void Walk_PreOrder_FilesBeforeSubfolders_CaseInsensitiveSort()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Path, "B", "x"));
        Directory.CreateDirectory(Path.Combine(_temp.Path, "a"));
        File.WriteAllText(Path.Combine(_temp.Path, "z.txt"), "z");
        File.WriteAllText(Path.Combine(_temp.Path, "M.txt"), "m");
        File.WriteAllText(Path.Combine(_temp.Path, "a", "a1.txt"), "a1");
        File.WriteAllText(Path.Combine(_temp.Path, "B", "x", "deep.txt"), "deep");

        List<string> order = [.. DirectoryWalker.Walk(_temp.Path).Select(e => $"{e.Kind}:{Rel(e)}")];

        order.Should().Equal(
            "Directory:.", "File:M.txt", "File:z.txt",
            "Directory:a", "File:a/a1.txt",
            "Directory:B", "Directory:B/x", "File:B/x/deep.txt");
    }

    [Fact]
    public void Walk_EmptyFolder_FlaggedOnlyWhenItHasNoEntriesAtAll()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Path, "empty"));
        Directory.CreateDirectory(Path.Combine(_temp.Path, "full"));
        File.WriteAllText(Path.Combine(_temp.Path, "full", "f.txt"), "f");

        List<WalkEntry> dirs = [.. DirectoryWalker.Walk(_temp.Path).Where(e => e.Kind == WalkEntryKind.Directory)];

        dirs.Single(e => Rel(e) == "empty").IsEmptyDirectory.Should().BeTrue();
        dirs.Single(e => Rel(e) == "full").IsEmptyDirectory.Should().BeFalse();
        dirs.Single(e => Rel(e) == ".").IsEmptyDirectory.Should().BeFalse();
    }

    [Fact]
    public void Walk_UnreadableSubfolder_OneEntryAndLaterSiblingsStillWalked()
    {
        string locked = Path.Combine(_temp.Path, "b_locked");
        Directory.CreateDirectory(locked);
        File.WriteAllText(Path.Combine(locked, "hidden.txt"), "h");
        Directory.CreateDirectory(Path.Combine(_temp.Path, "c_after"));
        File.WriteAllText(Path.Combine(_temp.Path, "c_after", "c.txt"), "c");

        List<WalkEntry> entries;
        using (new DeniedFolder(locked))
            entries = [.. DirectoryWalker.Walk(_temp.Path)];

        entries.Should().ContainSingle(e => e.Kind == WalkEntryKind.UnreadableDirectory)
            .Which.Error.Should().BeOfType<UnauthorizedAccessException>();
        entries.Should().Contain(e => e.Kind == WalkEntryKind.File && Rel(e) == "c_after/c.txt");
        entries.Should().NotContain(e => Rel(e).Contains("hidden"));
    }

    [Fact]
    public void Walk_UnreadableRoot_OneEntryNoThrow()
    {
        using (new DeniedFolder(_temp.Path))
        {
            List<WalkEntry> entries = [.. DirectoryWalker.Walk(_temp.Path)];
            entries.Should().ContainSingle().Which.Kind.Should().Be(WalkEntryKind.UnreadableDirectory);
        }
    }

    [Fact]
    public void Walk_MissingRoot_OneUnreadableEntryNoThrow()
    {
        List<WalkEntry> entries = [.. DirectoryWalker.Walk(Path.Combine(_temp.Path, "nope"))];

        entries.Should().ContainSingle().Which.Kind.Should().Be(WalkEntryKind.UnreadableDirectory);
    }

    [Fact]
    public void Walk_JunctionLoop_ReportedOnceNeverEntered()
    {
        File.WriteAllText(Path.Combine(_temp.Path, "a.txt"), "a");
        string loop = Path.Combine(_temp.Path, "loop");
        if (!ZipArchiveServiceArchiveTests.TryCreateJunction(loop, _temp.Path))
            return; // junctions not supported on this system

        try
        {
            List<WalkEntry> entries = [.. DirectoryWalker.Walk(_temp.Path)];

            entries.Should().ContainSingle(e => e.Kind == WalkEntryKind.ReparsePoint).Which.Info.FullName.Should().Be(loop);
            entries.Should().ContainSingle(e => e.Kind == WalkEntryKind.File);
        }
        finally
        {
            Directory.Delete(loop, recursive: false);
        }
    }

    // T-F509: a name Windows changes ("a.txt ", "d.") opens its twin by the plain path a consumer
    // gets. The walk reports it as TrimmedName and never enters it: listing "d." lists "d".
    [Fact]
    public void Walk_NamesWindowsTrims_AreTrimmedNameAndNeverEntered()
    {
        string root = _temp.Path;
        File.WriteAllText(@"\\?\" + Path.Combine(root, "a.txt "), "spaced");
        File.WriteAllText(Path.Combine(root, "a.txt"), "twin");
        Directory.CreateDirectory(@"\\?\" + Path.Combine(root, "d."));
        File.WriteAllText(@"\\?\" + Path.Combine(root, "d.", "in.txt"), "in dotted");
        Directory.CreateDirectory(Path.Combine(root, "d"));
        File.WriteAllText(Path.Combine(root, "d", "twin.txt"), "twin");
        Directory.CreateDirectory(@"\\?\" + Path.Combine(root, "e "));
        File.WriteAllText(@"\\?\" + Path.Combine(root, "e ", "e.txt"), "e");
        try
        {
            List<string> order = [.. DirectoryWalker.Walk(root).Select(e => $"{e.Kind}:{e.Info.FullName[Math.Min(root.Length + 1, e.Info.FullName.Length)..].Replace('\\', '/')}")];

            order.Should().Equal(
                "Directory:", "File:a.txt", "TrimmedName:a.txt ",
                "TrimmedName:d.", "TrimmedName:e ",
                "Directory:d", "File:d/twin.txt");
        }
        finally
        {
            File.Delete(@"\\?\" + Path.Combine(root, "a.txt "));
            Directory.Delete(@"\\?\" + Path.Combine(root, "d."), recursive: true);
            Directory.Delete(@"\\?\" + Path.Combine(root, "e "), recursive: true);
        }
    }

    [Fact]
    public void Walk_FolderHoldingOnlyATrimmedName_IsNotEmpty()
    {
        string folder = Path.Combine(_temp.Path, "f");
        Directory.CreateDirectory(folder);
        File.WriteAllText(@"\\?\" + Path.Combine(folder, "x."), "dotted");
        try
        {
            DirectoryWalker.Walk(folder).First().IsEmptyDirectory.Should().BeFalse();
        }
        finally
        {
            File.Delete(@"\\?\" + Path.Combine(folder, "x."));
        }
    }

    // The filter is the user's: a name it leaves out is LeftOut, never an error.
    [Fact]
    public void Walk_TrimmedNameTheFilterLeavesOut_IsLeftOut()
    {
        File.WriteAllText(@"\\?\" + Path.Combine(_temp.Path, "skip."), "dotted");
        try
        {
            List<WalkEntry> entries = [.. DirectoryWalker.Walk(_temp.Path, info => info.Name.StartsWith("skip", StringComparison.Ordinal))];

            entries.Should().ContainSingle(e => e.Info.Name == "skip.").Which.Kind.Should().Be(WalkEntryKind.LeftOut);
        }
        finally
        {
            File.Delete(@"\\?\" + Path.Combine(_temp.Path, "skip."));
        }
    }
}
