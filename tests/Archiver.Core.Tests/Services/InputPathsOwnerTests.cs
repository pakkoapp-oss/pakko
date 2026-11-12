using System.Text.RegularExpressions;
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

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
