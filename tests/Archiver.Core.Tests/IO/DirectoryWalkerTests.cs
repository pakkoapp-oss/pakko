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

    // T-F559: a file or folder "NUL" made through \\?\ opens, by its plain path, the null device on every
    // Windows ("CON.txt" the console on Windows 10): a reader got 0 bytes, a ZIP an empty entry "z///./NUL".
    // The walk reports it as DeviceName, never entered, in a plain walk and a spelled one alike; below a
    // \\?\ root every path is literal and it is a file as any other.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Walk_NamesWindowsOpensAsADevice_AreDeviceNameAndNeverEntered(bool spelledBelow)
    {
        string root = Path.Combine(_temp.Path, "dev");
        MakeDeviceNamedTree(root);
        try
        {
            List<string> order = [.. DirectoryWalker.Walk(root, spelledBelow: spelledBelow).Select(e => $"{e.Kind}:{e.Info.FullName[Math.Min(root.Length + 1, e.Info.FullName.Length)..].Replace('\\', '/')}")];

            order.Should().Equal(
                "Directory:", "DeviceName:nul", "File:ok.txt",
                "Directory:sub", "File:sub/in.txt", "DeviceName:sub/NUL");
            DirectoryWalker.Walk(root).First().IsEmptyDirectory.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(@"\\?\" + root, recursive: true);
        }
    }

    [Fact]
    public void Walk_LiteralRoot_TakesADeviceNameAsTheFileItIs()
    {
        string root = Path.Combine(_temp.Path, "devlit");
        MakeDeviceNamedTree(root);
        try
        {
            string literal = @"\\?\" + root;
            List<string> order = [.. DirectoryWalker.Walk(literal, spelledBelow: true).Select(e => $"{e.Kind}:{e.Info.FullName[Math.Min(literal.Length + 1, e.Info.FullName.Length)..].Replace('\\', '/')}")];

            order.Should().Equal(
                "Directory:", "File:nul", "File:ok.txt",
                "Directory:sub", "File:sub/in.txt", "Directory:sub/NUL", "File:sub/NUL/in.txt");
        }
        finally
        {
            Directory.Delete(@"\\?\" + root, recursive: true);
        }
    }

    // A file "nul", a folder "sub/NUL" holding a file, and ordinary entries.
    private static void MakeDeviceNamedTree(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "ok.txt"), "ok");
        File.WriteAllText(Path.Combine(root, "sub", "in.txt"), "in");
        File.WriteAllText(@"\\?\" + Path.Combine(root, "nul"), "device-named bytes");
        Directory.CreateDirectory(@"\\?\" + Path.Combine(root, "sub", "NUL"));
        File.WriteAllText(@"\\?\" + Path.Combine(root, "sub", "NUL", "in.txt"), "inside");
    }

    // T-F547: Pakko's own folder walked by its \\?\ path: every path stays literal, so a name Windows
    // trims names that entry and no twin, and it is entered as any other.
    [Fact]
    public void WalkAsSpelled_NamesWindowsTrims_EnteredByTheirLiteralPaths()
    {
        string root = @"\\?\" + Path.Combine(_temp.Path, "out.");
        Directory.CreateDirectory(Path.Combine(root, "e "));
        File.WriteAllText(Path.Combine(root, "a.txt."), "dotted");
        File.WriteAllText(Path.Combine(root, "e ", "e.txt"), "e");
        Directory.CreateDirectory(Path.Combine(_temp.Path, "out"));
        File.WriteAllText(Path.Combine(_temp.Path, "out", "twin.txt"), "twin");
        try
        {
            List<WalkEntry> walked = [.. DirectoryWalker.WalkAsSpelled(root)];

            walked.Select(e => $"{e.Kind}:{e.Info.FullName[root.Length..].Replace('\\', '/')}").Should().Equal(
                "Directory:", "File:/a.txt.", "Directory:/e ", "File:/e /e.txt");
            walked.Should().OnlyContain(e => e.Info.FullName.StartsWith(root, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // T-F531: ZIP's walk enters a name Windows trims by its literal path, and only that name and what is
    // below it are literal; RelativePath names each one as spelled ("a.txt ", not the twin "a.txt").
    [Fact]
    public void Walk_SpelledBelow_NamesWindowsTrims_EnteredByTheirLiteralPaths()
    {
        string root = _temp.Path;
        File.WriteAllText(@"\\?\" + Path.Combine(root, "a.txt "), "spaced");
        File.WriteAllText(Path.Combine(root, "a.txt"), "twin");
        Directory.CreateDirectory(@"\\?\" + Path.Combine(root, "d."));
        File.WriteAllText(@"\\?\" + Path.Combine(root, "d.", "in.txt"), "in dotted");
        Directory.CreateDirectory(Path.Combine(root, "d"));
        File.WriteAllText(Path.Combine(root, "d", "twin.txt"), "twin");
        try
        {
            List<WalkEntry> walked = [.. DirectoryWalker.Walk(root, spelledBelow: true)];

            walked.Select(e => $"{e.Kind}:{DirectoryWalker.RelativePath(root, e.Info.FullName).Replace('\\', '/')}").Should().Equal(
                "Directory:.", "File:a.txt", "File:a.txt ",
                "Directory:d", "File:d/twin.txt",
                "Directory:d.", "File:d./in.txt");
            walked.Where(e => e.Info.FullName.StartsWith(@"\\?\", StringComparison.Ordinal)).Select(e => e.Info.Name)
                .Should().Equal("a.txt ", "d.", "in.txt");
            File.ReadAllText(walked.Single(e => e.Info.Name == "a.txt ").Info.FullName).Should().Be("spaced");
        }
        finally
        {
            File.Delete(@"\\?\" + Path.Combine(root, "a.txt "));
            Directory.Delete(@"\\?\" + Path.Combine(root, "d."), recursive: true);
        }
    }

    // T-F531: the filter judges the name as spelled; by its plain path "a.txt " is "a.txt".
    [Fact]
    public void Walk_SpelledBelow_TheFilterSeesTheNameAsSpelled()
    {
        string root = _temp.Path;
        File.WriteAllText(@"\\?\" + Path.Combine(root, "a.txt "), "spaced");
        File.WriteAllText(Path.Combine(root, "a.txt"), "twin");
        try
        {
            var seen = new List<string>();
            List<WalkEntry> walked = [.. DirectoryWalker.Walk(root, info => { seen.Add(DirectoryWalker.RelativePath(root, info.FullName)); return info.Name == "a.txt"; }, spelledBelow: true)];

            seen.Should().BeEquivalentTo("a.txt", "a.txt ");
            walked.Single(e => e.Kind == WalkEntryKind.File).Info.Name.Should().Be("a.txt ");
        }
        finally
        {
            File.Delete(@"\\?\" + Path.Combine(root, "a.txt "));
        }
    }

    // T-F531: a root written as \\?\ with a name Windows trims is walked; its plain spelling, which
    // lists the twin, walks nothing in either mode.
    [Fact]
    public void Walk_SpelledBelow_LiteralTrimmedRootIsWalked_PlainOneIsNot()
    {
        string plain = Path.Combine(_temp.Path, "d.");
        string literal = @"\\?\" + plain;
        Directory.CreateDirectory(literal);
        File.WriteAllText(Path.Combine(literal, "in.txt"), "in dotted");
        Directory.CreateDirectory(Path.Combine(_temp.Path, "d"));
        File.WriteAllText(Path.Combine(_temp.Path, "d", "twin.txt"), "twin");
        try
        {
            DirectoryWalker.Walk(literal, spelledBelow: true).Select(e => $"{e.Kind}:{DirectoryWalker.RelativePath(literal, e.Info.FullName)}")
                .Should().Equal("Directory:.", "File:in.txt");
            DirectoryWalker.Walk(plain, spelledBelow: true).Should().BeEmpty();
            DirectoryWalker.Walk(literal).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(literal, recursive: true);
        }
    }

    [Theory]
    [InlineData(@"C:\w\tw", @"\\?\C:\w\tw\a.txt ", "a.txt ")]
    [InlineData(@"C:\w\tw\", @"\\?\C:\w\tw\d.\x.txt", @"d.\x.txt")]
    [InlineData(@"C:\w\tw", @"C:\w\tw\sub\x.txt", @"sub\x.txt")]
    [InlineData(@"\\?\C:\w\d.", @"\\?\C:\w\d.\e \x.txt", @"e \x.txt")]
    [InlineData(@"\\server\share\tw", @"\\?\UNC\server\share\tw\a.txt.", "a.txt.")]
    [InlineData(@"C:\", @"\\?\C:\a.txt ", "a.txt ")]
    public void RelativePath_LiteralEntryUnderAPlainRoot_IsAsSpelled(string root, string fullName, string expected)
    {
        DirectoryWalker.RelativePath(root, fullName).Should().Be(expected);
    }

    // T-F547: only a literal root; a plain one goes through Walk's checks.
    [Fact]
    public void WalkAsSpelled_PlainRoot_WalksNothing()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Path, "plain"));

        DirectoryWalker.WalkAsSpelled(Path.Combine(_temp.Path, "plain")).Should().BeEmpty();
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

    // T-F557: a root or name of spaces is below no root, as an empty one; GetFullPath would throw.
    [Theory]
    [InlineData("   ", @"C:\x\f.txt")]
    [InlineData(@"C:\x", "   ")]
    public void RelativePath_WhitespaceOnly_ComesBackAsGiven(string root, string fullName)
    {
        DirectoryWalker.RelativePath(root, fullName).Should().Be(fullName);
    }

    // T-F557: a child spelled again by its \\?\ path and gone before its stat reads attributes -1,
    // every bit set: it is not a link. Opened later, it is reported as gone.
    [Fact]
    public void IsReparsePoint_EntryThatIsGone_IsNot()
    {
        DirectoryWalker.IsReparsePoint(new FileInfo(@"\\?\" + Path.Combine(_temp.Path, "gone."))).Should().BeFalse();
    }
}
