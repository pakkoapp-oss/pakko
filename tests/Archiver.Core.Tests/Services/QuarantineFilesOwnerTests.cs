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
    private static readonly string[] QuarantineClasses = ["TarSandboxedService", "AntivirusScanService"];

    private static readonly (string File, string Call, int Count, string Why)[] Allowed =
    [
        ("TarSandboxedService.cs", "Directory.Exists(options.DestinationFolder)", 1, "the destination, a path Pakko chose"),
        ("TarSandboxedService.cs", "Directory.CreateDirectory(destDir)", 1, "the destination, a path Pakko chose"),
        ("TarSandboxedService.cs", "new FileInfo(archivePath)", 1, "the archive itself"),
        ("TarSandboxedService.cs", "Directory.Exists(actualDest)", 1, "the destination, a path Pakko chose"),
        ("TarSandboxedService.cs", "Path.GetRelativePath(actualDest, landedPath)", 1, "the destination, a path Pakko chose"),
        ("TarSandboxedService.cs", "Path.GetFullPath(Path.Combine(actualDest, relative))", 1, "the destination, a path Pakko chose"),
        ("Sandbox/TarExeReader.cs", "Directory.CreateDirectory(Path.Combine(actualDest, relative))", 1, "the destination, a path Pakko chose"),
        ("Sandbox/TarExeReader.cs", "Path.GetRelativePath(plan.ActualDest, Path.GetFullPath(Path.Combine(plan.ActualDest, relative)))", 1, "the destination, a path Pakko chose"),
        ("Sandbox/TarExeReader.cs", "Path.GetFullPath(Path.Combine(plan.ActualDest, relative))", 1, "the destination, a path Pakko chose"),
        ("Sandbox/TarExeReader.cs", "Directory.CreateDirectory(Path.Combine(plan.Staging.Path, relative))", 1, "the staging folder, Pakko's names"),
        ("Sandbox/HelperReader.cs", "Directory.CreateDirectory(Path.Combine(placement.Staging.Path, relative))", 1, "the staging folder, Pakko's names"),
        ("TarSandboxedService.cs", "File.Exists(finalFilePath)", 1, "the destination, a path Pakko chose"),
        ("TarSandboxedService.cs", "Directory.Exists(finalFilePath)", 1, "the destination, a path Pakko chose"),
        ("TarSandboxedService.cs", "Path.GetFullPath(Path.Combine(actualDest, relativePath))", 1, "the destination, a path Pakko chose; the trim it makes is detected right after (trimmedHere)"),
        ("TarSandboxedService.cs", "Path.GetRelativePath(actualDest, finalFilePath)", 1, "the destination, a path Pakko chose"),
        ("TarSandboxedService.cs", "Directory.CreateDirectory(Path.GetDirectoryName(stagedFilePath)!)", 1, "the staging folder, Pakko's names"),
        ("TarSandboxedService.cs", "File.Exists(archivePath)", 1, "the archive itself"),
        ("TarSandboxedService.cs", "ArchiveEntrySecurity.TryPropagateMotw(archivePath, stagedFilePath, context.MotwMode)", 1, "the staging folder, under the destination name already checked for a trim (trimmedHere)"),
        ("TarSandboxedService.cs", "QuarantineAcl.CarriesSandboxSecurity(stagedFilePath)", 1, "the staging folder, under the destination name already checked for a trim (trimmedHere)"),
        ("TarSandboxedService.cs", "QuarantineAcl.InheritFromParent(stagedFilePath)", 1, "the staging folder, under the destination name already checked for a trim (trimmedHere)"),
        ("TarSandboxedService.cs", "ArchiveEntrySecurity.IsReparsePoint(sourcePath)", 1, "a source the user chose (creation)"),
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
        ("Sandbox/TarSandboxScope.cs", "QuarantineAcl.EnsureSharedParentTraverse(SandboxParentDirectory, sid)", 1, "the sandbox parent folder, Pakko's name"),
        ("Sandbox/TarSandboxScope.cs", "QuarantineAcl.GrantTraverseListReadAttributes(quarantineRoot, sid)", 1, "a Pakko-named folder or file at the quarantine root"),
        ("Sandbox/TarSandboxScope.cs", "QuarantineAcl.GrantModify(outDir, sid)", 1, "a Pakko-named folder or file at the quarantine root"),
        ("Sandbox/TarSandboxScope.cs", "QuarantineAcl.GrantModify(firstDir, _sid)", 1, "a Pakko-named folder or file at the quarantine root"),
        ("Sandbox/TarSandboxScope.cs", "QuarantineAcl.GrantReadExecute(folder, _sid)", 3, "a Pakko-named folder or file at the quarantine root"),
        ("Sandbox/TarSandboxScope.cs", "Directory.CreateDirectory(outDir)", 1, "a Pakko-named folder or file at the quarantine root"),
        ("Sandbox/TarSandboxScope.cs", "Directory.Delete(quarantineRoot, recursive: true)", 1, "the quarantine root; .NET deletes names such as \"d \" and \"a.txt.\" below it (measured)"),
        ("Sandbox/TarSandboxScope.cs", "new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read)", 1, "the archive itself"),
        ("Sandbox/TarSandboxScope.cs", "Directory.CreateDirectory(firstDir)", 1, "a Pakko-named folder or file at the quarantine root"),
        ("Sandbox/TarSandboxScope.cs", "Directory.CreateDirectory(folder)", 3, "a Pakko-named folder or file at the quarantine root"),
        ("Sandbox/TarSandboxScope.cs", "File.WriteAllBytes(Path.Combine(QuarantineRoot, relativePath), TarCommandLineEncoding.EncodeLines(lines, CommandLinePage))", 1, "a Pakko-named folder or file at the quarantine root"),
        ("Sandbox/TarSandboxScope.cs", "new FileStream(ReopenArchive(), FileAccess.Read)", 2, "a handle, not a path (the archive)"),
        ("Sandbox/TarSandboxScope.cs", "File.WriteAllBytesAsync(Path.Combine(QuarantineRoot, relative), SelectionBytes(selection), cancellationToken)", 1, "a Pakko-named folder or file at the quarantine root"),
        ("Sandbox/TarSandboxScope.cs", "File.ReadAllBytesAsync(helper, cancellationToken)", 1, "the archive helper's copy in bin, a Pakko-named file"),
        ("Sandbox/TarSandboxScope.cs", "File.WriteAllBytes(copy, bytes)", 1, "the archive helper's copy in bin, a Pakko-named file"),
        ("Sandbox/TarSandboxScope.cs", "File.ReadAllBytes(helperSource)", 1, "the archive helper beside Pakko's own binaries, not the quarantine"),
        ("Sandbox/ArchiveHelperOutput.cs", "new(StringComparer.Ordinal)", 1, "a Dictionary of support names, not the disk"),
        ("Sandbox/ArchiveHelperOutput.cs", "new(what)", 1, "an exception, not the disk"),
        ("Sandbox/TarSandboxScope.cs", "Directory.Exists(QuarantineRoot)", 1, "a Pakko-named folder or file at the quarantine root"),
        ("Sandbox/TarSandboxScope.cs", "Directory.Delete(QuarantineRoot, recursive: true)", 1, "the quarantine root; .NET deletes names such as \"d \" and \"a.txt.\" below it (measured)"),
        ("Sandbox/TarSignatureVerifier.cs", "File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete)", 1, "tar.exe and its DLLs"),
        ("Sandbox/TarExeReader.cs", "new(allNames.Where(n => n.EndsWith('/')).Select(n => EntryDiskName.AsPakkoWrites(n.TrimEnd('/'))), StringComparer.OrdinalIgnoreCase)", 1, "a HashSet of entry names, not the disk"),
        ("Sandbox/TarExeReader.cs", "new(fileEntries.Where(e => which(e.Name)).Select(e => EntryDiskName.AsTarExeWrites(EntryDiskName.StripLeadingDotSlash(e.Name))), StringComparer.OrdinalIgnoreCase)", 1, "a HashSet of entry names, not the disk"),
        ("TarSandboxedService.cs", "new(ownPaths.Select(path => SubstDrive.Resolve(Path.GetFullPath(path))), StringComparer.OrdinalIgnoreCase)", 1, "a HashSet of sources the user chose (creation), not the disk"),
        ("Sandbox/ProcessEnvironmentBlock.cs", "new((string)variable.Key, (string?)variable.Value ?? \"\")", 1, "an environment variable pair, not the disk"),
        ("Sandbox/SandboxedProcessLauncher.cs", "new(-1)", 1, "a pseudo-handle, not a path"),
        ("Sandbox/TarNames.cs", "new(TarNameTrust.Faithful, null)", 1, "a verdict value, not the disk"),
        ("Sandbox/TarSignatureVerifier.cs", "new(\"00AAC56B-CD44-11D0-8CC2-00C04FC295EE\")", 1, "a Guid, not the disk"),
        ("Sandbox/TarSignatureVerifier.cs", "new(\"F750E6C3-38EE-11D1-85E5-00C04FC295EE\")", 1, "a Guid, not the disk"),
        ("Sandbox/TarSignatureVerifier.cs", "new(-1)", 1, "a handle value, not a path"),
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
            .Where(line => LiteralCall().IsMatch(line) || LongPathPrefix().IsMatch(line) || line.Contains(@"""\\\\?\\", StringComparison.Ordinal))
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
        TopLevelFiles().Should().BeEquivalentTo(["TarSandboxedService.cs", "AntivirusScanService.cs"]);
    }

    [Fact]
    public void Scan_CoversEveryFileThatReachesTheQuarantine()
    {
        string[] scanned = [.. ScannedFiles().Append(Owner)];
        string[] reaching = [.. Directory.EnumerateFiles(ServicesDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => QuarantineUser().IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(ServicesDir, f).Replace(Path.DirectorySeparatorChar, '/'))];

        reaching.Should().BeSubsetOf(scanned, "a file that uses QuarantineFiles or TarSandboxScope can reach a quarantine path");
    }

    // Every Services file that declares a part of a class handling the quarantine, so a split into
    // partial files or a move does not take code out of the scan.
    private static IEnumerable<string> TopLevelFiles() =>
        Directory.EnumerateFiles(ServicesDir, "*.cs")
            .Where(f => QuarantineClasses.Any(c => Regex.IsMatch(File.ReadAllText(f), $@"\bclass\s+{c}\b")))
            .Select(Path.GetFileName)
            .OfType<string>();

    private static IEnumerable<string> ScannedFiles() =>
        TopLevelFiles()
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
    // construction of a FileInfo, DirectoryInfo or FileStream; every target-typed construction with
    // arguments, since its type is not in the text (as a ternary branch, a lambda body, an argument);
    // Path's normalizing calls; the Core helpers that touch a path on disk themselves (T-F463 review: a quarantine path handed to one is as unguarded as a direct call).
    [GeneratedRegex(@"\b(?:Directory|File)\.\w+\(|\b(?:Directory|File)\.(?>\w+)(?!\s*\()|\bnew\s+(?:FileInfo|DirectoryInfo|FileStream)\(|\bnew\s*\((?!\s*\))|\bPath\.(?:GetRelativePath|GetFullPath|Exists)\(|\bArchiveEntrySecurity\.(?:IsReparsePoint|TryPropagateMotw)\(|\bQuarantineAcl\.\w+\(")]
    private static partial Regex DiskCall();

    // The prefix as a verbatim string, interpolated or not: @"\\?\", @$"\\?\", $@"\\?\".
    [GeneratedRegex(@"(?:@\$?|\$@)""\\\\\?\\")]
    private static partial Regex LongPathPrefix();

    [GeneratedRegex(@"\b(?:QuarantineFiles\.|TarSandboxScope\b)")]
    private static partial Regex QuarantineUser();

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
