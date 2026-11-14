using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Core.Tests.IO;

// T-F528: an operation's work folders next to its destination are WorkRoot's: one hidden root, a new
// folder per role. Eight places once chose where theirs went and how they were made. Every other name
// TempOwner makes is listed with why it is not a work folder next to a destination: a new one fails
// here until it is a role of the root, or is listed.
public sealed partial class WorkRootOwnerTests
{
    private static readonly string SrcRoot = Path.Combine(FindRepoRoot(), "src");
    private const string Owner = "Archiver.Core/IO/WorkRoot.cs";

    private static readonly (string File, int Count, string Why)[] Allowed =
    [
        ("Archiver.CLI/CliStagingFolder.cs", 1, "-so's folder, and -si's when no root can be made near -o: %TEMP%"),
        ("Archiver.Core/Services/Sandbox/ArchiveintGate.cs", 1, "the probe's empty archive, one file in %TEMP%"),
        ("Archiver.Core/Services/Sandbox/QuarantineLocation.cs", 1, "only the length of a quarantine root's name"),
        ("Archiver.Core/Services/Sandbox/TarSandboxScope.cs", 3, "the quarantine: its own protected root, not a role of a root that inherits the destination's DACL; the helper's bin in %TEMP%"),
        ("Archiver.Core/Services/TarCapabilitySnapshot.cs", 1, "the verdict's temporary file in Pakko's LocalAppData folder"),
        ("Archiver.Core/Services/TarCollisionStaging.cs", 1, "junctions, in %TEMP%: FAT, exFAT and a share make none next to a destination"),
        ("Archiver.Core/Services/Zip/ParallelSingleArchiveWriter.cs", 1, "the chunk folder, beside the temporary archive, which is in the work root"),
    ];

    [Fact]
    public void TempOwnerNames_OutsideTheOwner_AreOnlyTheListedOnes()
    {
        var found = new Dictionary<string, int>();
        foreach (string file in SourceFiles().Where(f => f != Owner))
        {
            int count = NewName().Count(Code(file));
            if (count > 0)
                found[file] = count;
        }
        Dictionary<string, int> allowed = Allowed.ToDictionary(a => a.File, a => a.Count);

        string[] unlisted = [.. found.Where(f => f.Value > allowed.GetValueOrDefault(f.Key)).Select(f => $"{f.Key} x{f.Value}")];
        string[] stale = [.. allowed.Where(a => a.Value > found.GetValueOrDefault(a.Key)).Select(a => $"{a.Key} x{a.Value}")];

        string.Join(Environment.NewLine, unlisted).Should().BeEmpty("a work folder next to a destination is a role of WorkRoot (T-F528); any other name is listed here with why");
        string.Join(Environment.NewLine, stale).Should().BeEmpty("a listed call that is gone leaves room for a new one");
    }

    [Fact]
    public void Scan_SeesTheOwner() =>
        NewName().Count(Code(Owner)).Should().Be(1, "the root's own name is TempOwner's");

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SrcRoot, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal));

    // The file without its comment lines, so a remark about a call is not one.
    private static string Code(string file) =>
        string.Join('\n', File.ReadAllLines(Path.Combine(SrcRoot, file.Replace('/', Path.DirectorySeparatorChar)))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [GeneratedRegex(@"\bTempOwner\.NewName\(")]
    private static partial Regex NewName();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
