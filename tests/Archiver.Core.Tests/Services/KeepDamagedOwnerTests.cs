using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F496: keeping a damaged file is asked for only by the user, one extraction at a time, and only
// ZIP extraction acts on it. The Shell (Explorer's menu), Test, hash, preview and scan never name it,
// so a new file that does fails here and gets a look.
public sealed class KeepDamagedOwnerTests
{
    private static readonly string SrcRoot = Path.Combine(FindRepoRoot(), "src");

    private static readonly string[] Allowed =
    [
        "Archiver.App.Core/KeepDamagedOption.cs",
        "Archiver.App/ViewModels/MainViewModel.cs",
        "Archiver.CLI/CliArgumentParser.cs",
        "Archiver.CLI/CliDownloadMark.cs",
        "Archiver.CLI/Program.cs",
        "Archiver.Core/Models/ExtractOptions.cs",
        "Archiver.Core/Models/GroupPolicyOptions.cs",
        "Archiver.Core/Services/ExtractionRouter.cs",
        "Archiver.Core/Services/GroupPolicyService.cs",
        "Archiver.Core/Services/ZipArchiveService.cs",
    ];

    [Fact]
    public void TheOption_IsNamedOnlyByTheListedFiles()
    {
        string[] naming = [.. Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SrcRoot, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(Path.Combine(SrcRoot, f)).Contains("KeepDamagedFiles", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];

        naming.Should().Equal(Allowed, "only the App's box, pakko's -kb, the policy and ZIP extraction may name it");
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
