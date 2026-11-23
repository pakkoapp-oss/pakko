using System.Text.RegularExpressions;
using Archiver.Core.IO;
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
        ("Archiver.Core/Services/Sandbox/QuarantineFiles.cs", ".EnumerateFileSystemEntries(", 1, "whether a quarantine folder holds anything, its first entry only (T-F526)"),
        ("Archiver.Core/IO/TempOwner.cs", ".EnumerateFileSystemInfos(", 2, "one level of a Pakko temp folder, for its sweep; and (T-F527) one level of Pakko's own folder after its DACL is given back: DirectoryWalker lists a folder before it yields it, so one its owner was locked out of would come back unreadable"),
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
        ("Archiver.Core/Services/Sandbox/TarExeReader.cs", ".EnumerateFiles(", 5, "QuarantineFiles' walk of the quarantine (T-F463; T-F453's roots)"),
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

    // T-F531: Path.GetRelativePath drops a trailing space or dot of a plain path, so it named "a.txt "
    // in a walk as its twin "a.txt"; a walk's entries are named by DirectoryWalker.RelativePath. Every
    // other call is listed here with why it never meets such a name.
    private static readonly (string File, int Count, string Why)[] RelativePathAllowed =
    [
        ("Archiver.App.Core/HashReport.cs", 1, "a hashed source's name for the report; hashing refuses such a name (TrimmedName)"),
        ("Archiver.CLI/Program.cs", 2, "names printed under the destination and the folder of a failed source"),
        ("Archiver.Core/Services/ExtractionStaging.cs", 3, "Pakko's own staging tree: EntryDiskName's names"),
        ("Archiver.Core/Services/FileHashService.cs", 1, "hashing's walk, which refuses such a name (TrimmedName)"),
        ("Archiver.Core/Services/Sandbox/TarExeReader.cs", 1, "an entry's place under the destination, EntryDiskName's names"),
        ("Archiver.Core/Services/TarSandboxedService.cs", 2, "a landed file under the destination, EntryDiskName's names"),
        ("Archiver.Core/Services/ZipArchiveService.cs", 1, "an extracted file under the destination, EntryDiskName's names"),
        ("Archiver.Shell/OperationMessages.cs", 1, "a failed source's name in a message"),
    ];

    [Fact]
    public void RelativePath_OutsideTheOwner_IsOnlyTheListedOnes()
    {
        var found = SourceFiles().Where(f => f != Owner)
            .Select(f => (File: f, Count: RelativePathCall().Count(Code(f))))
            .Where(f => f.Count > 0)
            .ToDictionary(f => f.File, f => f.Count);

        found.Should().BeEquivalentTo(RelativePathAllowed.ToDictionary(a => a.File, a => a.Count),
            "a walk's entry is named by DirectoryWalker.RelativePath (T-F531); any other call is listed with why");
    }

    // T-F547: WalkAsSpelled enters names Windows trims, which a walk over the user's folders must
    // never do; only the removal of Pakko's own folders takes it.
    [Fact]
    public void WalkAsSpelled_OnlyTempOwnerCallsIt()
    {
        SourceFiles().Where(f => f != Owner && Code(f).Contains("WalkAsSpelled(", StringComparison.Ordinal))
            .Should().Equal("Archiver.Core/IO/TempOwner.cs");
    }

    // T-F547: the \\?\ form of a path is InputPaths.Literal's; QuarantineFiles and WorkRoot each had
    // their own, which differed for \\.\. A check of a prefix (StartsWith) is not one.
    [Fact]
    public void LiteralPathPrefix_IsBuiltOnlyByInputPaths()
    {
        SourceFiles().Where(f => f != "Archiver.Core/Services/InputPaths.cs" && LiteralPrefixBuilt().IsMatch(Code(f)))
            .Should().BeEmpty();
        LiteralPrefixBuilt().IsMatch(Code("Archiver.Core/Services/InputPaths.cs")).Should().BeTrue("the scan must see the owner's own");
    }

    // T-F552: any spelling of the prefix in a literal (verbatim, interpolated, escaped, inside
    // string.Concat or a format) counts; only a prefix test (StartsWith) and a volume GUID path's
    // constant are not a build.
    [Fact]
    public void LiteralPrefixBuilt_SeesEverySpelling()
    {
        string[] built =
        [
            @"return @""\\?\"" + path;",
            @"return $@""\\?\{path}"";",
            @"return @$""\\?\UNC\{path}"";",
            @"return string.Concat(@""\\?\"", path);",
            @"return ""\\\\?\\"" + path;",
            @"return $""\\\\?\\{path}"";",
            @"return Path.Join(@""\\?"", path);",
            @"return ""\\\\?"" + ""\\\\"" + path;",
        ];
        string[] notBuilt =
        [
            @"if (path.StartsWith(@""\\?\"", StringComparison.Ordinal))",
            @"if (path.StartsWith(@""\\?\UNC\"", StringComparison.OrdinalIgnoreCase))",
            @"if (path.StartsWith(""\\\\?\\"", StringComparison.Ordinal))",
            @"const string Start = @""\\?\Volume{"";",
        ];

        built.Where(line => !LiteralPrefixBuilt().IsMatch(line)).Should().BeEmpty();
        notBuilt.Where(line => LiteralPrefixBuilt().IsMatch(line)).Should().BeEmpty();
    }

    // A literal holding the prefix ("\\?\" verbatim or "\\\\?\\" escaped, or the same without its last
    // backslash) not right after StartsWith(,
    // and not a volume GUID path.
    [GeneratedRegex(@"(?<!StartsWith\([$@]{0,2})""(\\\\\?(\\|"")|\\\\\\\\\?(\\\\|""))(?!Volume\{)")]
    private static partial Regex LiteralPrefixBuilt();

    [Fact]
    public void Scan_SeesTheOwner()
    {
        // Guards the scan itself: if the owner's own listing stops matching, the test above passes vacuously.
        Listing().Matches(Code(Owner)).Select(m => m.Value).Should().Contain(".EnumerateFileSystemInfos(");
        Listing().Matches("d.GetDirectories(); Directory.EnumerateFileSystemEntries(p); new FileSystemEnumerable<string>(r, t, o)")
            .Select(m => m.Value).Should().Equal(".GetDirectories(", ".EnumerateFileSystemEntries(", "FileSystemEnumerable");
    }

    // T-F557: the ZIP writers name every kind a walk yields. A kind left out of the switch was dropped
    // in silence and the source reported complete; TrimmedName, which a spelled walk never yields, among them.
    [Theory]
    [InlineData("Archiver.Core/Services/ZipArchiveService.cs")]
    [InlineData("Archiver.Core/Services/Zip/WorkItemEnumerator.cs")]
    public void ZipWritersWalk_NamesEveryKind(string file)
    {
        string code = Code(file);
        Enum.GetNames<WalkEntryKind>().Where(kind => !code.Contains("case WalkEntryKind." + kind, StringComparison.Ordinal))
            .Should().BeEmpty("a kind the walk yields is written, skipped or reported, never dropped (T-F557)");
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SrcRoot, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal));

    // The file without its comment lines, so a remark or a <see cref> about a call is not one.
    private static string Code(string file) =>
        string.Join('\n', File.ReadAllLines(Path.Combine(SrcRoot, file.Replace('/', Path.DirectorySeparatorChar)))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [GeneratedRegex(@"\bGetRelativePath\(")]
    private static partial Regex RelativePathCall();

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
