using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F463: every disk call that reaches a name tar.exe wrote in the quarantine goes through
// Archiver.Core.Services.Sandbox.QuarantineFiles, which asks for it by its literal name (\\?\).
// Asked for without the prefix, or normalized by Path.GetFullPath/GetRelativePath, "d " is "d" and
// "a.txt." is "a.txt": T-F460 fixed eight such calls and #169's review found two more, one a false
// Clean. Every other disk call in the files that handle the quarantine is listed below with why it
// never sees a tar.exe-written name, and counted: one more of the same text is a new call to judge.
public sealed partial class QuarantineFilesOwnerTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string ServicesDir = Path.Combine(RepoRoot, "src", "Archiver.Core", "Services");
    private const string Owner = "Sandbox/QuarantineFiles.cs";
    private static readonly string[] TopLevelFiles = ["TarSandboxedService.cs", "AntivirusScanService.cs"];

    private static readonly (string File, string Call, int Count, string Why)[] Allowed =
    [
        ("TarSandboxedService.cs", "Directory.Exists(options.DestinationFolder)", 1, "the destination, a path Pakko chose"),
        ("TarSandboxedService.cs", "Directory.CreateDirectory(destDir)", 1, "the destination, a path Pakko chose"),
        ("TarSandboxedService.cs", "new FileInfo(archivePath)", 1, "the archive itself"),
        ("TarSandboxedService.cs", "Directory.Exists(actualDest)", 1, "the destination, a path Pakko chose"),
        ("TarSandboxedService.cs", "Path.GetRelativePath(actualDest, landedPath)", 1, "the destination, a path Pakko chose"),
        ("TarSandboxedService.cs", "Path.GetFullPath(Path.Combine(actualDest, relative))", 1, "the destination, a path Pakko chose"),
        ("TarSandboxedService.cs", "Directory.CreateDirectory(Path.Combine(actualDest, relative))", 1, "the destination, a path Pakko chose"),
        ("TarSandboxedService.cs", "Path.GetRelativePath(plan.ActualDest, Path.GetFullPath(Path.Combine(plan.ActualDest, relative)))", 1, "the destination, a path Pakko chose"),
        ("TarSandboxedService.cs", "Path.GetFullPath(Path.Combine(plan.ActualDest, relative))", 1, "the destination, a path Pakko chose"),
        ("TarSandboxedService.cs", "Directory.CreateDirectory(Path.Combine(plan.Staging.Path, relative))", 1, "the staging folder, Pakko's names"),
        ("TarSandboxedService.cs", "File.Exists(finalFilePath)", 1, "the destination, a path Pakko chose"),
        ("TarSandboxedService.cs", "Directory.Exists(finalFilePath)", 1, "the destination, a path Pakko chose"),
        ("TarSandboxedService.cs", "Path.GetFullPath(Path.Combine(actualDest, relativePath))", 1, "the destination, a path Pakko chose; the trim it makes is detected right after (trimmedHere)"),
        ("TarSandboxedService.cs", "Path.GetRelativePath(actualDest, finalFilePath)", 1, "the destination, a path Pakko chose"),
        ("TarSandboxedService.cs", "Directory.CreateDirectory(Path.GetDirectoryName(stagedFilePath)!)", 1, "the staging folder, Pakko's names"),
        ("TarSandboxedService.cs", "File.Exists(archivePath)", 1, "the archive itself"),
        ("TarSandboxedService.cs", "File.Exists(destPath)", 2, "the archive being created"),
        ("TarSandboxedService.cs", "Directory.Exists(path)", 2, "a source the user chose (creation)"),
        ("TarSandboxedService.cs", "File.Exists(sourcePath)", 2, "a source the user chose (creation)"),
        ("TarSandboxedService.cs", "Directory.Exists(sourcePath)", 3, "a source the user chose (creation)"),
        ("TarSandboxedService.cs", "File.Exists(tempPath)", 1, "the archive being created, in its temp name"),
        ("TarSandboxedService.cs", "Directory.Exists(writerTempDir)", 1, "creation's temp folder, Pakko's name"),
        ("TarSandboxedService.cs", "Directory.Delete(writerTempDir, recursive: true)", 1, "creation's temp folder, Pakko's name"),
        ("TarSandboxedService.cs", "Directory.CreateDirectory(writerTempDir)", 1, "creation's temp folder, Pakko's name"),
        ("TarSandboxedService.cs", "Directory.Exists(collisionStagingDir)", 1, "creation's staging folder, Pakko's name"),
        ("TarSandboxedService.cs", "File.WriteAllBytes(excludeListPath, TarCommandLineEncoding.EncodeLines(excludePatterns))", 1, "creation's exclude list, Pakko's name"),
        ("TarSandboxedService.cs", "File.Exists(path)", 2, "a source the user chose (creation); and a best-effort delete of creation's own temp file"),
        ("TarSandboxedService.cs", "Directory.Delete(junction, recursive: false)", 1, "creation's staging junction, Pakko's name"),
        ("TarSandboxedService.cs", "Directory.Delete(stagingDir, recursive: true)", 1, "creation's staging folder, Pakko's name"),
        ("TarSandboxedService.cs", "File.Delete(path)", 1, "creation's own temp file"),
        ("TarSandboxedService.cs", "Path.GetFullPath(sourcePath)", 1, "a source the user chose (creation)"),
        ("TarSandboxedService.cs", "Directory.Exists", 1, "a method group over the sources the user chose (creation)"),
        ("TarSandboxedService.cs", "Directory.Exists(fullSource)", 2, "a source the user chose (creation)"),
        ("TarSandboxedService.cs", "new DirectoryInfo(root)", 1, "a source the user chose (creation) (a drive root)"),
        ("TarSandboxedService.cs", "Path.GetFullPath(path)", 1, "a source the user chose (creation)"),
        ("TarSandboxedService.cs", "Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!)", 1, "creation's staging folder, Pakko's name"),
        ("TarSandboxedService.cs", "File.Copy(fullSource, stagedPath)", 1, "a source the user chose (creation); whether a source named \"a.txt \" survives this is open (docs/TASKS.md, T-F463 note)"),
        ("TarSandboxedService.cs", "File.GetAttributes(path)", 1, "a source the user chose (creation) (size estimate)"),
        ("TarSandboxedService.cs", "new FileInfo(path)", 1, "a source the user chose (creation) (size estimate)"),
        ("AntivirusScanService.cs", "File.OpenRead(archivePath)", 1, "the archive itself"),
        ("Sandbox/SandboxedProcessLauncher.cs", "new FileStream(stdOutRead, FileAccess.Read)", 1, "a handle, not a path"),
        ("Sandbox/SandboxedProcessLauncher.cs", "new FileStream(stdErrRead, FileAccess.Read)", 1, "a handle, not a path"),
        ("Sandbox/SandboxedProcessLauncher.cs", "new FileStream(stdInWrite, FileAccess.Write, bufferSize: 0)", 1, "a handle, not a path"),
        ("Sandbox/TarSandboxScope.cs", "Directory.CreateDirectory(SandboxParentDirectory)", 1, "the sandbox parent folder, Pakko's name"),
        ("Sandbox/TarSandboxScope.cs", "Directory.CreateDirectory(quarantineRoot)", 1, "a Pakko-named folder or file at the quarantine root"),
        ("Sandbox/TarSandboxScope.cs", "Directory.CreateDirectory(outDir)", 1, "a Pakko-named folder or file at the quarantine root"),
        ("Sandbox/TarSandboxScope.cs", "Directory.Delete(quarantineRoot, recursive: true)", 1, "the quarantine root; .NET deletes names such as \"d \" and \"a.txt.\" below it (measured)"),
        ("Sandbox/TarSandboxScope.cs", "new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read)", 1, "the archive itself"),
        ("Sandbox/TarSandboxScope.cs", "Directory.CreateDirectory(firstDir)", 1, "a Pakko-named folder or file at the quarantine root"),
        ("Sandbox/TarSandboxScope.cs", "Directory.CreateDirectory(folder)", 1, "a Pakko-named folder or file at the quarantine root"),
        ("Sandbox/TarSandboxScope.cs", "File.WriteAllBytes(Path.Combine(QuarantineRoot, relativePath), TarCommandLineEncoding.EncodeLines(lines))", 1, "a Pakko-named folder or file at the quarantine root"),
        ("Sandbox/TarSandboxScope.cs", "new FileStream(ReopenArchive(), FileAccess.Read)", 2, "a handle, not a path (the archive)"),
        ("Sandbox/TarSandboxScope.cs", "Directory.Exists(QuarantineRoot)", 1, "a Pakko-named folder or file at the quarantine root"),
        ("Sandbox/TarSandboxScope.cs", "Directory.Delete(QuarantineRoot, recursive: true)", 1, "the quarantine root; .NET deletes names such as \"d \" and \"a.txt.\" below it (measured)"),
        ("Sandbox/TarSignatureVerifier.cs", "File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete)", 1, "tar.exe and its DLLs"),
    ];

    [Fact]
    public void DiskCalls_OutsideTheOwner_AreOnlyTheListedOnes()
    {
        var found = new Dictionary<(string File, string Call), int>();
        foreach (string file in ScannedFiles())
            foreach (string call in Calls(Code(file)))
                found[(file, call)] = found.GetValueOrDefault((file, call)) + 1;
        Dictionary<(string File, string Call), int> allowed = Allowed.ToDictionary(a => (a.File, a.Call), a => a.Count);

        string[] unlisted = [.. found.Where(f => f.Value > allowed.GetValueOrDefault(f.Key))
            .Select(f => $"{f.Key.File}: {f.Key.Call} x{f.Value}")];
        string[] stale = [.. allowed.Where(a => a.Value > found.GetValueOrDefault(a.Key))
            .Select(a => $"{a.Key.File}: {a.Key.Call} x{a.Value}")];

        string.Join(Environment.NewLine, unlisted).Should().BeEmpty("a quarantine path goes through Sandbox/QuarantineFiles (docs/DECISIONS.md, T-F463); any other disk call is listed here with why");
        string.Join(Environment.NewLine, stale).Should().BeEmpty("a listed call that is gone leaves room for a new one with the same text");
    }

    [Fact]
    public void LongPathPrefix_IsSpelledOnlyByTheOwner()
    {
        string[] offenders = [.. ScannedFiles().SelectMany(file => Code(file).Split('\n')
            .Where(line => LiteralCall().IsMatch(line) || line.Contains(@"@""\\?\", StringComparison.Ordinal) || line.Contains(@"""\\\\?\\", StringComparison.Ordinal))
            .Select(line => $"{file}: {line.Trim()}"))];

        string.Join(Environment.NewLine, offenders).Should().BeEmpty("only Sandbox/QuarantineFiles adds the long-path prefix (T-F463)");
    }

    [Fact]
    public void Scan_SeesTheOwner()
    {
        // Guards the scan itself: if the owner's own calls stop matching, the tests above pass vacuously.
        Calls(Code(Owner)).Should().Contain(call => call.StartsWith("Directory.", StringComparison.Ordinal));
        Calls(Code(Owner)).Should().Contain(call => call.StartsWith("File.Move(", StringComparison.Ordinal));
        Code(Owner).Should().Contain(@"@""\\?\");
    }

    private static IEnumerable<string> ScannedFiles() =>
        TopLevelFiles
            .Concat(Directory.EnumerateFiles(Path.Combine(ServicesDir, "Sandbox"), "*.cs").Select(f => "Sandbox/" + Path.GetFileName(f)))
            .Where(f => f != Owner);

    // The file without its comment lines, so a remark about Directory.EnumerateFiles is not a call.
    private static string Code(string file) =>
        string.Join('\n', File.ReadAllLines(Path.Combine(ServicesDir, file.Replace('/', Path.DirectorySeparatorChar)))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    // Each call from its name to its closing parenthesis, joined across lines, spaces collapsed; a
    // method group as it is.
    private static IEnumerable<string> Calls(string code)
    {
        foreach (Match match in DiskCall().Matches(code))
        {
            if (!match.Value.EndsWith('('))
            {
                yield return match.Value;
                continue;
            }
            int depth = 1;
            int end = match.Index + match.Length;
            while (end < code.Length && depth > 0)
            {
                depth += code[end] switch { '(' => 1, ')' => -1, _ => 0 };
                end++;
            }
            string call = Spaces().Replace(code[match.Index..end], " ");
            yield return call.Replace("( ", "(", StringComparison.Ordinal).Replace(" )", ")", StringComparison.Ordinal);
        }
    }

    // A call; a method group (.Any(Directory.Exists)), which has no parenthesis of its own; an explicit
    // or target-typed construction of a FileInfo, DirectoryInfo or FileStream, as a variable or as an
    // expression-bodied member; Path's two normalizing calls.
    [GeneratedRegex(@"\b(?:Directory|File)\.\w+\(|\b(?:Directory|File)\.(?>\w+)(?!\s*\()|\bnew\s+(?:FileInfo|DirectoryInfo|FileStream)\(|\b(?:FileInfo|DirectoryInfo|FileStream)\??\s+\w+\s*=\s*new\s*\(|\b(?:FileInfo|DirectoryInfo|FileStream)\??\s+\w+\s*\([^()]*\)\s*=>\s*new\s*\(|\bPath\.(?:GetRelativePath|GetFullPath)\(")]
    private static partial Regex DiskCall();

    [GeneratedRegex(@"\bLiteral\(")]
    private static partial Regex LiteralCall();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
