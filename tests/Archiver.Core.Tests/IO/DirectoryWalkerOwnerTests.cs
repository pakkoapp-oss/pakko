using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Core.Tests.IO;

// T-F511 PR 3: a walk over the user's folders is DirectoryWalker's: it enters no link, never a name
// Windows trims, and reports a folder it cannot list. The CLI's two searches had their own (one
// dropped an unreadable folder in silence). Every other listing in src is one folder Pakko knows, or
// Pakko's own tree, listed with why: a new one fails here.
public sealed partial class DirectoryWalkerOwnerTests
{
    private static readonly string SrcRoot = Path.Combine(FindRepoRoot(), "src");
    private const string Owner = "Archiver.Core/IO/DirectoryWalker.cs";

    private static readonly (string File, string Call, int Count, string Why)[] Allowed =
    [
        ("Archiver.App.Core/FileSystemBrowser.cs", ".EnumerateDirectories(", 1, "the App's browser lists one folder, never a tree"),
        ("Archiver.App.Core/FileSystemBrowser.cs", ".EnumerateFiles(", 1, "the App's browser lists one folder, never a tree"),
        ("Archiver.App.Core/ProcessTempRoot.cs", ".GetFileSystemEntries(", 1, "one level of Pakko's own %TEMP% root"),
        ("Archiver.CLI/CliSourceDeleter.cs", ".EnumerateFileSystemInfos(", 1, "whether a folder is empty before -sdel removes it"),
        ("Archiver.CLI/CliStreamStaging.cs", ".GetFiles(", 1, "-so's own staging folder"),
        ("Archiver.Core/IO/TempOwner.cs", ".EnumerateFileSystemInfos(", 1, "one level of a Pakko temp folder, for its sweep"),
        ("Archiver.Core/Recovery/Par2SetLocator.cs", ".EnumerateFiles(", 3, "the files of the one folder beside an archive"),
        ("Archiver.Core/Recovery/RecoveryDataWriter.cs", ".GetFiles(", 1, "the files of the one folder beside an archive"),
        ("Archiver.Core/Recovery/RecoveryDataWriter.cs", ".EnumerateFiles(", 1, "the files of the one folder beside an archive"),
        ("Archiver.Core/Services/ExtractionStaging.cs", ".EnumerateDirectories(", 1, "Pakko's own staging tree: EntryDiskName's names, no links"),
        ("Archiver.Core/Services/ExtractionStaging.cs", ".EnumerateFiles(", 1, "Pakko's own staging tree: EntryDiskName's names, no links"),
        ("Archiver.Core/Services/FileHashService.cs", ".EnumerateDirectories(", 1, "a folder's own spelling on disk, from its parent"),
        ("Archiver.Core/Services/TarSandboxedService.cs", ".EnumerateFiles(", 1, "QuarantineFiles' walk of the quarantine (T-F463)"),
        ("Archiver.Core/Services/TarSandboxedService.cs", ".GetFileSystemInfos(", 1, "a drive root's names for tar.exe, one level (T-F285)"),
        ("Archiver.Core/Services/ZipArchiveService.cs", ".EnumerateFileSystemEntries(", 1, "whether a folder is empty"),
        ("Archiver.Core/Services/Sandbox/QuarantineFiles.cs", ".EnumerateFileSystemInfos(", 1, "the quarantine's own walk, by literal paths, each item's attributes from the listing (T-F463)"),
        ("Archiver.Core/Services/Sandbox/TarExeReader.cs", ".EnumerateFiles(", 4, "QuarantineFiles' walk of the quarantine (T-F463)"),
    ];

    [Fact]
    public void FolderListing_OutsideTheOwner_IsOnlyTheListedOnes()
    {
        var found = new Dictionary<(string File, string Call), int>();
        foreach (string file in SourceFiles().Where(f => f != Owner))
            foreach (Match match in Listing().Matches(Code(file)))
                found[(file, match.Value)] = found.GetValueOrDefault((file, match.Value)) + 1;
        Dictionary<(string File, string Call), int> allowed = Allowed.ToDictionary(a => (a.File, a.Call), a => a.Count);

        string[] unlisted = [.. found.Where(f => f.Value > allowed.GetValueOrDefault(f.Key)).Select(f => $"{f.Key.File}: {f.Key.Call} x{f.Value}")];
        string[] stale = [.. allowed.Where(a => a.Value > found.GetValueOrDefault(a.Key)).Select(a => $"{a.Key.File}: {a.Key.Call} x{a.Value}")];

        string.Join(Environment.NewLine, unlisted).Should().BeEmpty("a walk over the user's folders is DirectoryWalker's (T-F511); any other listing is listed here with why");
        string.Join(Environment.NewLine, stale).Should().BeEmpty("a listed call that is gone leaves room for a new one");
    }

    [Fact]
    public void Scan_SeesTheOwner()
    {
        // Guards the scan itself: if the owner's own listing stops matching, the test above passes vacuously.
        Listing().Matches(Code(Owner)).Select(m => m.Value).Should().Contain(".EnumerateFileSystemInfos(");
        Listing().Matches("d.GetDirectories(); Directory.EnumerateFileSystemEntries(p); new FileSystemEnumerable<string>(r, t, o)")
            .Select(m => m.Value).Should().Equal(".GetDirectories(", ".EnumerateFileSystemEntries(", "FileSystemEnumerable");
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SrcRoot, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal));

    // The file without its comment lines, so a remark or a <see cref> about a call is not one.
    private static string Code(string file) =>
        string.Join('\n', File.ReadAllLines(Path.Combine(SrcRoot, file.Replace('/', Path.DirectorySeparatorChar)))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    // A listing by the calls that list a folder.
    [GeneratedRegex(@"\.Enumerate(Files|Directories|FileSystemEntries|FileSystemInfos)\(|\.Get(Files|Directories|FileSystemEntries|FileSystemInfos)\(|\bFileSystemEnumerable\b")]
    private static partial Regex Listing();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
