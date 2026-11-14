using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F513: a ZIP is opened for reading only by ZipArchiveReader.Open, whose name pass refuses an
// archive over ArchiveLimits.MaxEntries before .NET's reader holds an object for every entry. One
// other reader stays, listed with why: a new one fails here.
public sealed partial class ZipReadOwnerTests
{
    private static readonly string SrcRoot = Path.Combine(FindRepoRoot(), "src");
    private const string Owner = "Archiver.Core/Services/Zip/ZipArchiveReader.cs";

    private static readonly (string File, string Call, int Count, string Why)[] Allowed =
    [
        // The temp archive Pakko itself just wrote, asked whether it holds an entry (T-F60).
        ("Archiver.Core/Services/ZipArchiveService.cs", "ZipFile.OpenRead(", 1, "HasTempEntries: Pakko's own output, never the user's archive"),
    ];

    [Fact]
    public void ZipReading_OutsideTheOwner_IsOnlyTheListedOnes()
    {
        var found = new Dictionary<(string File, string Call), int>();
        foreach (string file in SourceFiles().Where(f => f != Owner))
            foreach (Match match in Reader().Matches(Code(file)))
                found[(file, match.Value)] = found.GetValueOrDefault((file, match.Value)) + 1;
        Dictionary<(string File, string Call), int> allowed = Allowed.ToDictionary(a => (a.File, a.Call), a => a.Count);

        string[] unlisted = [.. found.Where(f => f.Value > allowed.GetValueOrDefault(f.Key)).Select(f => $"{f.Key.File}: {f.Key.Call} x{f.Value}")];
        string[] stale = [.. allowed.Where(a => a.Value > found.GetValueOrDefault(a.Key)).Select(a => $"{a.Key.File}: {a.Key.Call} x{a.Value}")];

        string.Join(Environment.NewLine, unlisted).Should().BeEmpty("a ZIP is read through ZipArchiveReader.Open (T-F513); any other reader is listed here with why");
        string.Join(Environment.NewLine, stale).Should().BeEmpty("a listed call that is gone leaves room for a new one");
    }

    [Fact]
    public void Scan_SeesTheOwner()
    {
        // Guards the scan itself: if the owner's own call stops matching, the test above passes vacuously.
        Reader().Matches(Code(Owner)).Select(m => m.Value).Should().Contain("ZipFile.OpenRead(");
        Reader().Matches("new ZipArchive(s, ZipArchiveMode.Read); ZipFile.Open(p, ZipArchiveMode.Read); new ZipArchive(s)")
            .Select(m => m.Value).Should().HaveCount(3);
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SrcRoot, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal));

    private static string Code(string file) =>
        string.Join('\n', File.ReadAllLines(Path.Combine(SrcRoot, file.Replace('/', Path.DirectorySeparatorChar)))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    // A read: ZipFile.OpenRead, ZipFile.Open in Read mode, or a ZipArchive over a stream not in
    // Create mode (its default mode is Read).
    [GeneratedRegex(@"ZipFile\.OpenRead\(|ZipFile\.Open\([^;]*ZipArchiveMode\.Read|new ZipArchive\((?![^;]*ZipArchiveMode\.(Create|Update))[^;]*\)")]
    private static partial Regex Reader();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
