using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F470: every list, extract, test and antivirus scan of a tar-family archive reads it through
// Sandbox/ISandboxedArchiveReader. What knows how tar.exe lists, names and writes entries - a sandbox
// opened on the archive, its "-t" listing, the names it writes on disk, best-fit judging, the
// first-copy pass - lives only in the tar.exe reader and the owners it builds on, so a call made
// beside the reader cannot bypass the backend the process chose.
public sealed partial class SandboxedReaderOwnerTests
{
    private static readonly string SrcDir = Path.Combine(FindRepoRoot(), "src");
    private const string Reader = "Archiver.Core/Services/Sandbox/TarExeReader.cs";
    private const string HelperReader = "Archiver.Core/Services/Sandbox/HelperReader.cs";

    private static readonly string[] Owners =
    [
        Reader,
        HelperReader,
        "Archiver.Core/Services/Sandbox/TarSandboxScope.cs",
        "Archiver.Core/Services/Sandbox/TarNames.cs",
        "Archiver.Core/Services/EntryDiskName.cs",
    ];

    [Fact]
    public void TarExeKnowledge_StaysInTheReader()
    {
        string[] offenders = [.. SourceFiles()
            .Where(file => !Owners.Contains(file))
            .SelectMany(file => TarExeCall().Matches(Code(file)).Select(m => $"{file}: {m.Value}"))];

        string.Join(Environment.NewLine, offenders).Should().BeEmpty(
            "a tar-family archive is read only through ISandboxedArchiveReader (T-F470); tar.exe's names stay in its reader");
    }

    [Fact]
    public void Scan_SeesTheReader()
    {
        // Guards the scan itself: if the reader's own calls stop matching, the test above passes vacuously.
        string[] seen = [.. TarExeCall().Matches(Code(Reader)).Select(m => m.Value)];
        seen.Should().Contain("TarSandboxScope.CreateAsync(");
        seen.Should().Contain("AsTarExeWrites");
        seen.Should().Contain("TarNames.Judge(");
        seen.Should().Contain("ExtractFirstOccurrencesAsync(");
        SourceFiles().Should().Contain(Owners);
    }

    // The archive helper's output is read only by its reader, and the helper runs only through the
    // scope: a second reader of its lines would be a second parser to keep in step.
    private static readonly string[] HelperOwners =
    [
        HelperReader,
        "Archiver.Core/Services/Sandbox/TarSandboxScope.cs",
        "Archiver.Core/Services/Sandbox/ArchiveHelperOutput.cs",
    ];

    [Fact]
    public void TheHelpersOutput_IsReadOnlyByItsReader()
    {
        string[] offenders = [.. SourceFiles()
            .Where(file => !HelperOwners.Contains(file))
            .SelectMany(file => HelperCall().Matches(Code(file)).Select(m => $"{file}: {m.Value}"))];

        string.Join(Environment.NewLine, offenders).Should().BeEmpty(
            "the archive helper's lines are parsed by ArchiveHelperOutput for HelperReader alone (T-F470)");
        HelperCall().Matches(Code(HelperReader)).Select(m => m.Value).Should().Contain(["ArchiveHelperOutput", "RunHelperAsync("]);
    }

    [GeneratedRegex(@"\bArchiveHelperOutput\b|\bRunHelperAsync\(")]
    private static partial Regex HelperCall();

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(SrcDir, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SrcDir, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !f.Contains("/obj/", StringComparison.Ordinal) && !f.Contains("/bin/", StringComparison.Ordinal));

    // The file without its comment lines, so a remark or a <see cref> is not a call.
    private static string Code(string file) =>
        string.Join('\n', File.ReadAllLines(Path.Combine(SrcDir, file.Replace('/', Path.DirectorySeparatorChar)))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    // Opening a sandbox on an archive (every tar.exe read runs through its scope), tar.exe's names
    // on disk (a call or a method group), its listing, best-fit judging, and the first-copy pass.
    [GeneratedRegex(@"\bTarSandboxScope\.CreateAsync\(|\bAsTarExeWrites\b|\bTarNames\.(?:Judge|JudgeExtracted|EnsureNamesCanDecide|ReadListing)\(|\bReadHeaderNamesAsync\(|\bExtractFirstOccurrencesAsync\(")]
    private static partial Regex TarExeCall();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
