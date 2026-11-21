using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// T-F561: every tar.exe run that writes (mode "-x") goes through TarSandboxScope.RunAsync with the
// declared bytes as its write limit. A new "-x" run without one, or a "-x" run started elsewhere,
// fails here.
public sealed partial class TarWriteLimitOwnerTests
{
    private static readonly string SrcRoot = Path.Combine(FindRepoRoot(), "src");
    private const string Owner = "Archiver.Core/Services/Sandbox/TarSandboxScope.cs";

    [Fact]
    public void ExtractingRuns_AllPassAWriteLimit()
    {
        string code = Code(Owner);
        MatchCollection runs = ExtractingRun().Matches(code);

        runs.Should().HaveCount(3, "extraction, the first copies and Test");
        foreach (Match run in runs)
        {
            int end = code.IndexOf(';', run.Index);
            code[run.Index..end].Should().Contain("writeLimit:", "a tar.exe run that writes is held to what it declares");
        }
    }

    [Fact]
    public void ExtractingMode_IsStartedOnlyByTheOwner() =>
        SourceFiles().Where(f => f != Owner && ExtractingMode().IsMatch(Code(f))).Should().BeEmpty();

    [GeneratedRegex(@"RunAsync\(\s*""-x""")]
    private static partial Regex ExtractingRun();

    [GeneratedRegex(@"""-x[fOq]?""")]
    private static partial Regex ExtractingMode();

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(SrcRoot, "Archiver.Core"), "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SrcRoot, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal));

    // The file without its comment lines, so a remark about a run is not one.
    private static string Code(string file) =>
        string.Join('\n', File.ReadAllLines(Path.Combine(SrcRoot, file.Replace('/', Path.DirectorySeparatorChar)))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
