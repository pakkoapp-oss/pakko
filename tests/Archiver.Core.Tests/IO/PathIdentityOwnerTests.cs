using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Core.Tests.IO;

// T-F511 PR 4: where a path really leads is asked of PathIdentity. The App opened the handle and read
// the final path itself (Win32SourceDeleteOperations.ResolveFinalPath); it now asks
// PathIdentity.FinalPathOfItself. One other resolver stays, listed with why: a new call fails here.
public sealed partial class PathIdentityOwnerTests
{
    private static readonly string SrcRoot = Path.Combine(FindRepoRoot(), "src");
    private const string Owner = "Archiver.Core/IO/PathIdentity.cs";

    private static readonly (string File, string Call, int Count, string Why)[] Allowed =
    [
        // -sdel's refusal compares where a path would lead before anything exists to open: a source
        // that is not there, or the fictitious folders of its unit tests, are walked part by part
        // (T-F419). PathIdentity answers only for what can be opened; its file identity is asked too.
        ("Archiver.CLI/CliSourceDeleter.cs", "LinkTarget", 1, "RealPath: a path that need not exist, one part at a time"),
        ("Archiver.CLI/CliSourceDeleter.cs", ".ResolveLinkTarget(", 1, "RealPath: a path that need not exist, one part at a time"),
        // tar.exe fails on a file at a SUBST drive's root (T-F285); the path string is rewritten
        // before tar.exe opens anything, so there is no handle to ask.
        ("Archiver.Core/Services/SubstDrive.cs", "QueryDosDevice", 2, "a SUBST drive's folder for tar.exe's command line (T-F285)"),
    ];

    [Fact]
    public void FinalPathResolution_OutsideTheOwner_IsOnlyTheListedOnes()
    {
        var found = new Dictionary<(string File, string Call), int>();
        foreach (string file in SourceFiles().Where(f => f != Owner))
            foreach (Match match in Resolver().Matches(Code(file)))
                found[(file, match.Value)] = found.GetValueOrDefault((file, match.Value)) + 1;
        Dictionary<(string File, string Call), int> allowed = Allowed.ToDictionary(a => (a.File, a.Call), a => a.Count);

        string[] unlisted = [.. found.Where(f => f.Value > allowed.GetValueOrDefault(f.Key)).Select(f => $"{f.Key.File}: {f.Key.Call} x{f.Value}")];
        string[] stale = [.. allowed.Where(a => a.Value > found.GetValueOrDefault(a.Key)).Select(a => $"{a.Key.File}: {a.Key.Call} x{a.Value}")];

        string.Join(Environment.NewLine, unlisted).Should().BeEmpty("where a path leads is PathIdentity's (T-F511); any other resolver is listed here with why");
        string.Join(Environment.NewLine, stale).Should().BeEmpty("a listed call that is gone leaves room for a new one");
    }

    [Fact]
    public void Scan_SeesTheOwner()
    {
        // Guards the scan itself: if the owner's own import stops matching, the test above passes vacuously.
        Resolver().Matches(Code(Owner)).Select(m => m.Value).Should().Contain("GetFinalPathNameByHandle");
        Resolver().Matches("x.ResolveLinkTarget(true); y.LinkTarget is null; z is { LinkTarget: not null }").Select(m => m.Value)
            .Should().Equal(".ResolveLinkTarget(", "LinkTarget", "LinkTarget");
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SrcRoot, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal));

    // The file without its comment lines, so a remark about a call is not one.
    private static string Code(string file) =>
        string.Join('\n', File.ReadAllLines(Path.Combine(SrcRoot, file.Replace('/', Path.DirectorySeparatorChar)))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    // A resolver by the calls that ask where a path leads: the final name, a link's target, a DOS
    // device's target, the reparse data, or the name through a handle.
    [GeneratedRegex(@"\bGetFinalPathNameByHandle|\.ResolveLinkTarget\(|(?<!Resolve)\bLinkTarget\b|\bQueryDosDevice|\bNtQueryInformationFile|\bFSCTL_GET_REPARSE_POINT|\bFileNameInfo\b")]
    private static partial Regex Resolver();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
