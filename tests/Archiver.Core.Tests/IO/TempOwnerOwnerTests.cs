using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Core.Tests.IO;

// T-F511 PR 2: a temporary name carries its owner (TempOwner.NewName) so a later sweep can remove
// what a killed run left. The archiveint probe and the tar verdict made "<guid>" names nothing swept,
// and the CLI kept its own name, sweep and delete. A fixed Pakko root under %TEMP%, or a unique name
// inside a folder that already carries the owner, is listed with why: a new one fails here.
public sealed partial class TempOwnerOwnerTests
{
    private static readonly string SrcRoot = Path.Combine(FindRepoRoot(), "src");
    private const string Owner = "Archiver.Core/IO/TempOwner.cs";

    private static readonly (string File, string Call, int Count, string Why)[] Allowed =
    [
        ("Archiver.App.Core/NestedArchiveCache.cs", "GetTempPath", 1, "a fixed root; ProcessTempRoot names a folder per process in it"),
        ("Archiver.App.Core/PreviewCache.cs", "GetTempPath", 1, "a fixed root; ProcessTempRoot names a folder per process in it"),
        ("Archiver.App.Core/ProcessTempRoot.cs", "Guid.NewGuid", 1, "a scope inside this process's own folder, which carries the owner"),
        ("Archiver.CLI/CliStreamStaging.cs", "GetTempPath", 2, "the -si and -so roots; each run's folder is TempOwner's"),
        ("Archiver.Core/Services/Sandbox/ArchiveintGate.cs", "GetTempPath", 2, "where the probe file is named and swept by TempOwner"),
        ("Archiver.Core/Services/Sandbox/QuarantineLocation.cs", "GetTempPath", 1, "only its volume is asked, no name (T-F524)"),
        ("Archiver.Core/Services/Sandbox/TarSandboxScope.cs", "GetTempPath", 1, "a fixed root for the sandbox folders"),
        ("Archiver.Core/Services/TarCollisionStaging.cs", "GetTempPath", 1, "where its folders are swept by TempOwner"),
        ("Archiver.Core/Services/TarSandboxedService.cs", "GetTempPath", 1, "where TarCollisionStaging names its folder by TempOwner"),
        ("Archiver.Core/Services/Zip/ParallelSingleArchiveWriter.cs", "Guid.NewGuid", 1, "a chunk file inside the chunk folder TempOwner named"),
    ];

    [Fact]
    public void TempNames_OutsideTheOwner_AreOnlyTheListedOnes()
    {
        var found = new Dictionary<(string File, string Call), int>();
        foreach (string file in SourceFiles().Where(f => f != Owner))
            foreach (Match match in TempName().Matches(Code(file)))
                found[(file, match.Value)] = found.GetValueOrDefault((file, match.Value)) + 1;
        Dictionary<(string File, string Call), int> allowed = Allowed.ToDictionary(a => (a.File, a.Call), a => a.Count);

        string[] unlisted = [.. found.Where(f => f.Value > allowed.GetValueOrDefault(f.Key)).Select(f => $"{f.Key.File}: {f.Key.Call} x{f.Value}")];
        string[] stale = [.. allowed.Where(a => a.Value > found.GetValueOrDefault(a.Key)).Select(a => $"{a.Key.File}: {a.Key.Call} x{a.Value}")];

        string.Join(Environment.NewLine, unlisted).Should().BeEmpty("a temporary name is TempOwner's (T-F511); any other is listed here with why");
        string.Join(Environment.NewLine, stale).Should().BeEmpty("a listed call that is gone leaves room for a new one");
    }

    [Fact]
    public void Scan_SeesTheOwner()
    {
        // Guards the scan itself: if the owner's own call stops matching, the test above passes vacuously.
        TempName().Matches(Code(Owner)).Select(m => m.Value).Should().Contain("Guid.NewGuid");
        TempName().Matches("Path.GetTempFileName(); Path.GetRandomFileName(); Directory.CreateTempSubdirectory(); Path.GetTempPath()")
            .Select(m => m.Value).Should().Equal("GetTempFileName", "GetRandomFileName", "CreateTempSubdirectory", "GetTempPath");
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SrcRoot, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal));

    // The file without its comment lines, so a remark or a <see cref> about a call is not one.
    private static string Code(string file) =>
        string.Join('\n', File.ReadAllLines(Path.Combine(SrcRoot, file.Replace('/', Path.DirectorySeparatorChar)))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    // A temporary name by the calls that make one or find where it goes.
    [GeneratedRegex(@"\bGuid\.NewGuid\b|\bGetTempPath\b|\bGetTempFileName\b|\bGetRandomFileName\b|\bCreateTempSubdirectory\b")]
    private static partial Regex TempName();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
