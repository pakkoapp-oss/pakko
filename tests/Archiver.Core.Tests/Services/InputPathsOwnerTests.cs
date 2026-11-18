using System.Text.RegularExpressions;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F511 PR 5: trailing dots and spaces have two owners, on purpose. EntryDiskName judges an entry
// name Pakko writes part by part (7-Zip's Correct_FsPath trims every part); InputPaths judges a path
// the user gave by what Windows opens (GetFullPath, one part at a time). A third TrimEnd of dots or
// spaces in src fails here; the ones left are listed with why.
public sealed partial class InputPathsOwnerTests
{
    private static readonly string SrcRoot = Path.Combine(FindRepoRoot(), "src");

    private static readonly string[] Owners =
    [
        "Archiver.Core/Services/EntryDiskName.cs",
        "Archiver.Core/Services/InputPaths.cs",
    ];

    private static readonly (string File, string Call, int Count, string Why)[] Allowed =
    [
        ("Archiver.Core/Services/ArchiveEntrySecurity.cs", ".TrimEnd(' ', '.')", 1, "a device name ignores its trailing dots and spaces (\"CON. \" is CON)"),
        ("Archiver.Core/Services/ArchiveEntrySecurity.cs", ".TrimEnd(' ')", 1, "a device name's stem before its extension (\"CON .txt\" is CON)"),
    ];

    [Fact]
    public void TrailingDotSpaceTrim_OutsideTheOwners_IsOnlyTheListedOnes()
    {
        var found = new Dictionary<(string File, string Call), int>();
        foreach (string file in SourceFiles().Where(f => !Owners.Contains(f)))
            foreach (Match match in Trim().Matches(Code(file)))
                found[(file, match.Value)] = found.GetValueOrDefault((file, match.Value)) + 1;
        Dictionary<(string File, string Call), int> allowed = Allowed.ToDictionary(a => (a.File, a.Call), a => a.Count);

        string[] unlisted = [.. found.Where(f => f.Value > allowed.GetValueOrDefault(f.Key)).Select(f => $"{f.Key.File}: {f.Key.Call} x{f.Value}")];
        string[] stale = [.. allowed.Where(a => a.Value > found.GetValueOrDefault(a.Key)).Select(a => $"{a.Key.File}: {a.Key.Call} x{a.Value}")];

        string.Join(Environment.NewLine, unlisted).Should().BeEmpty("a trailing dot or space is EntryDiskName's (entry names) or InputPaths' (the user's paths) to judge (T-F511)");
        string.Join(Environment.NewLine, stale).Should().BeEmpty("a listed call that is gone leaves room for a new one");
    }

    [Fact]
    public void Scan_SeesTheOwner()
    {
        // Guards the scan itself: if the owner's own trim stops matching, the test above passes vacuously.
        Trim().Matches(Code(Owners[0])).Select(m => m.Value).Should().Contain(".TrimEnd(' ', '.')");
        Trim().Matches("a.TrimEnd('.', ' '); b.TrimEnd( ' ' ); c.TrimEnd(); d.TrimEnd('\\\\')")
            .Select(m => m.Value).Should().Equal(".TrimEnd('.', ' ')", ".TrimEnd( ' ' )");
    }

    // The two rules differ, and are meant to: Pakko writes an entry "d /x.txt" as "d/x.txt" (every
    // part trimmed, as 7-Zip), while Windows opens the user's "C:\w\d \x.txt" as typed - a space ends
    // a folder in the middle of a path, and only one dot is dropped there.
    [Theory]
    [InlineData("d /x.txt", @"C:\w\d \x.txt", true, false)]
    [InlineData("d../x.txt", @"C:\w\d..\x.txt", true, false)]
    [InlineData("d./x.txt", @"C:\w\d.\x.txt", true, true)]
    [InlineData("d/x.txt ", @"C:\w\d\x.txt ", true, true)]
    [InlineData("d/x.txt", @"C:\w\d\x.txt", false, false)]
    public void EntryRuleAndPathRule_DifferInTheMiddleOfAPath(string entry, string path, bool entryTrims, bool pathTrimmed)
    {
        EntryDiskName.Trims(entry).Should().Be(entryTrims);
        InputPaths.NameTrimmed(path).Should().Be(pathTrimmed);
    }

    // T-F557: a path's \\?\ prefix is taken off by InputPaths.SamePlainPath, which keeps it where the
    // plain path names a twin ("\\?\C:\x\d." is not "C:\x\d"). The CLI had two copies that took it off always.
    private static readonly (string File, int Count, string Why)[] LongPathPrefixAllowed =
    [
        ("Archiver.Core/Services/DirectoryJunction.cs", 1, "whether a path is on a share: a test, nothing is opened by the plain form"),
    ];

    [Fact]
    public void LongPathPrefixDropped_OutsideTheOwner_IsOnlyTheListedOnes()
    {
        var found = SourceFiles()
            .Where(f => f != "Archiver.Core/Services/InputPaths.cs")
            .Select(f => (File: f, Count: UncPrefix().Count(Code(f))))
            .Where(f => f.Count > 0)
            .ToDictionary(f => f.File, f => f.Count);

        found.Should().BeEquivalentTo(LongPathPrefixAllowed.ToDictionary(a => a.File, a => a.Count),
            "the \\\\?\\ prefix comes off through InputPaths.SamePlainPath (T-F557)");
        UncPrefix().Count(Code("Archiver.Core/Services/InputPaths.cs")).Should().BePositive("the scan must see the owner's own");
    }

    [Theory]
    [InlineData(@"C:\x\a.txt ", @"C:\x\a.txt ")]
    [InlineData(@"C:\x\d.\f.txt", @"C:\x\d.")]
    [InlineData(@"\\?\C:\x\d.\e\f.txt", @"\\?\C:\x\d.")]
    public void Problem_NameTrimmed_NamesThePartThatIsTrimmed(string path, string named)
    {
        InputPaths.Problem(path)!.Arguments.Should().Equal(named);
    }

    // T-F558: a path Windows opens as a device is no file to read: "CON.zip" in any folder is the console
    // on Windows 10 and Server 2022, and Detect blocked reading it. "NUL" is the null device on every
    // Windows (CON would block this test's own read without the fix). Spelled as \\?\ it is a file.
    [Theory]
    [InlineData(@"C:\x\NUL", true)]
    [InlineData(@"C:\x\nul", true)]
    [InlineData(@"\\.\NUL", true)]
    [InlineData(@"\\.\NUL\o.zip", true)]
    [InlineData(@"\\?\C:\x\NUL", false)]
    [InlineData(@"\\.\C:\x\a.zip", false)]
    [InlineData(@"C:\x\a.zip", false)]
    [InlineData(@"\\.\PhysicalDrive0", true)]
    // A root has no folder above it either, and is no device (T-F476's probe: DriveRootSourceTests).
    [InlineData(@"C:\", false)]
    [InlineData(@"\\?\C:\", false)]
    [InlineData(@"\\.\C:\", false)]
    [InlineData(@"\\server\share", false)]
    [InlineData(@"\\?\UNC\server\share\", false)]
    [InlineData(@"\\?\Volume{00000000-0000-0000-0000-000000000000}\", false)]
    public void Problem_ADevice_IsNotFound(string path, bool device)
    {
        CoreText? problem = InputPaths.Problem(path);

        if (device)
            problem.Should().BeEquivalentTo(new { Code = MessageCode.SourceNotFound, Arguments = new[] { path } });
        else
            problem.Should().BeNull();
        DestinationFolder.IsDevice(path).Should().Be(device, "T-F475's creation check keeps answering for a device");
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SrcRoot, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal));

    // The file without its comment lines, so a remark about a call is not one.
    private static string Code(string file) =>
        string.Join('\n', File.ReadAllLines(Path.Combine(SrcRoot, file.Replace('/', Path.DirectorySeparatorChar)))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    // A TrimEnd of dots, spaces or both, in either order.
    [GeneratedRegex(@"\.TrimEnd\(\s*'[ .]'\s*(,\s*'[ .]'\s*)?\)")]
    private static partial Regex Trim();

    [GeneratedRegex(@"@""\\\\\?\\UNC\\""")]
    private static partial Regex UncPrefix();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
