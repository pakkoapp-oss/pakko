using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F511: a folder the user chose (a destination, a repair's output) is created only by
// DestinationFolder.TryCreate, which refuses a name Windows trims (T-F509) and says why in Core's
// words (T-F406). RecoveryService made its output folder itself, so "out." wrote into the twin "out".
// Every other folder creation in src is listed with why its name is Pakko's own, and counted: one
// more of the same text is a new call to judge.
public sealed partial class DestinationFolderOwnerTests
{
    private static readonly string SrcRoot = Path.Combine(FindRepoRoot(), "src");
    private const string Owner = "Archiver.Core/Services/DestinationFolder.cs";

    private static readonly (string File, string Call, int Count, string Why)[] Allowed =
    [
        ("Archiver.App.Core/ProcessTempRoot.cs", "Directory.CreateDirectory(dir)", 1, "a scope under Pakko's %TEMP% root, Pakko's name"),
        ("Archiver.App/Services/LogService.cs", "Directory.CreateDirectory(dir)", 1, "Pakko's log folder"),
        ("Archiver.CLI/CliStagingFolder.cs", "Directory.CreateDirectory(path)", 1, "the CLI's %TEMP% staging, Pakko's name"),
        ("Archiver.Core/Services/DirectoryJunction.cs", "Directory.CreateDirectory(junctionPath)", 1, "a junction Pakko names in its own staging"),
        ("Archiver.Core/Services/ExtractionStaging.cs", "Directory.CreateDirectory(path)", 1, "the staging folder, TempOwner's name"),
        ("Archiver.Core/Services/ExtractionStaging.cs", "Directory.CreateDirectory(actualDest)", 1, "the destination after TryCreate, or its subfolder named by ArchiveNaming.GetFolderName"),
        ("Archiver.Core/Services/ExtractionStaging.cs", "Directory.CreateDirectory(finalFolder)", 1, "an entry's folder below the destination, named by EntryDiskName"),
        ("Archiver.Core/Services/ExtractionStaging.cs", "Directory.CreateDirectory(System.IO.Path.GetDirectoryName(finalFile)!)", 1, "an entry's folder below the destination, named by EntryDiskName"),
        ("Archiver.Core/Services/Sandbox/HelperReader.cs", "Directory.CreateDirectory(Path.Combine(placement.Staging.Path, relative))", 1, "the staging folder, entry names by EntryDiskName"),
        ("Archiver.Core/Services/Sandbox/QuarantineFiles.cs", "Directory.CreateDirectory(Literal(folder))", 1, "the quarantine's owner (T-F463)"),
        ("Archiver.Core/Services/Sandbox/TarExeReader.cs", "Directory.CreateDirectory(Path.Combine(actualDest, relative))", 1, "an entry's folder below the destination, named by EntryDiskName"),
        ("Archiver.Core/Services/Sandbox/TarExeReader.cs", "Directory.CreateDirectory(Path.Combine(plan.Staging.Path, relative))", 1, "the staging folder, entry names by EntryDiskName"),
        ("Archiver.Core/Services/Sandbox/TarSandboxScope.cs", "Directory.CreateDirectory(SandboxParentDirectory)", 1, "the sandbox parent folder, Pakko's name"),
        ("Archiver.Core/Services/Sandbox/TarSandboxScope.cs", "Directory.CreateDirectory(quarantineRoot)", 1, "the quarantine root, Pakko's name"),
        ("Archiver.Core/Services/Sandbox/TarSandboxScope.cs", "Directory.CreateDirectory(outDir)", 1, "a folder at the quarantine root, Pakko's name"),
        ("Archiver.Core/Services/Sandbox/TarSandboxScope.cs", "Directory.CreateDirectory(firstDir)", 1, "a folder at the quarantine root, Pakko's name"),
        ("Archiver.Core/Services/Sandbox/TarSandboxScope.cs", "Directory.CreateDirectory(folder)", 3, "a folder at the quarantine root, Pakko's name"),
        ("Archiver.Core/Services/TarCapabilitySnapshot.cs", "Directory.CreateDirectory(Path.GetDirectoryName(verdictPath)!)", 1, "Pakko's LocalAppData folder"),
        ("Archiver.Core/Services/TarSandboxedService.cs", "Directory.CreateDirectory(destDir)", 1, "the destination after TryCreate, or its subfolder named by ArchiveNaming.GetFolderName"),
        ("Archiver.Core/Services/TarSandboxedService.cs", "Directory.CreateDirectory(Path.GetDirectoryName(stagedFilePath)!)", 1, "the staging folder, entry names by EntryDiskName"),
        ("Archiver.Core/Services/TarSandboxedService.cs", "Directory.CreateDirectory(writerTempDir)", 1, "creation's temp folder, Pakko's name"),
        ("Archiver.Core/Services/TarSandboxedService.cs", "Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!)", 1, "creation's staging folder, Pakko's name"),
        ("Archiver.Core/Services/Zip/ParallelSingleArchiveWriter.cs", "Directory.CreateDirectory(path)", 1, "the chunk folder, TempOwner's name"),
        ("Archiver.Core/Services/ZipArchiveService.cs", "Directory.CreateDirectory(destDir)", 1, "the destination after TryCreate, or its subfolder named by ArchiveNaming.GetFolderName"),
        ("Archiver.Core/Services/ZipArchiveService.cs", "Directory.CreateDirectory(folder)", 1, "an entry's folder in the staging, named by EntryDiskName"),
        ("Archiver.Core/Services/ZipArchiveService.cs", "Directory.CreateDirectory(Path.GetDirectoryName(destFilePath)!)", 1, "an entry's folder in the staging, named by EntryDiskName"),
    ];

    [Fact]
    public void FolderCreation_OutsideTheOwner_IsOnlyTheListedOnes()
    {
        var found = new Dictionary<(string File, string Call), int>();
        foreach (string file in SourceFiles().Where(f => f != Owner))
            foreach (Match match in CreateCall().Matches(Code(file)))
                found[(file, match.Value)] = found.GetValueOrDefault((file, match.Value)) + 1;
        Dictionary<(string File, string Call), int> allowed = Allowed.ToDictionary(a => (a.File, a.Call), a => a.Count);

        string[] unlisted = [.. found.Where(f => f.Value > allowed.GetValueOrDefault(f.Key)).Select(f => $"{f.Key.File}: {f.Key.Call} x{f.Value}")];
        string[] stale = [.. allowed.Where(a => a.Value > found.GetValueOrDefault(a.Key)).Select(a => $"{a.Key.File}: {a.Key.Call} x{a.Value}")];

        string.Join(Environment.NewLine, unlisted).Should().BeEmpty("a folder the user chose is created by DestinationFolder.TryCreate (T-F511); any other creation is listed here with why");
        string.Join(Environment.NewLine, stale).Should().BeEmpty("a listed call that is gone leaves room for a new one with the same text");
    }

    [Fact]
    public void Scan_SeesTheOwner()
    {
        // Guards the scan itself: if the owner's own call stops matching, the test above passes vacuously.
        CreateCall().Matches(Code(Owner)).Select(m => m.Value).Should().Equal("Directory.CreateDirectory(folder)");
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SrcRoot, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal));

    // The file without its comment lines, so a remark or a <see cref> about the call is not one.
    private static string Code(string file) =>
        string.Join('\n', File.ReadAllLines(Path.Combine(SrcRoot, file.Replace('/', Path.DirectorySeparatorChar)))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    // A creation call to its closing parenthesis on the line (one nesting level: GetDirectoryName(...)!).
    [GeneratedRegex(@"\bDirectory\.CreateDirectory\((?:[^()]|\([^()]*\))*\)|\.CreateSubdirectory\((?:[^()]|\([^()]*\))*\)")]
    private static partial Regex CreateCall();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
