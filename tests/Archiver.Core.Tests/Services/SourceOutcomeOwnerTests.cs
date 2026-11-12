using System.Text.RegularExpressions;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F511 PR 5: whether a source may be deleted is SourceOutcomeRules' to decide. Only it makes a
// source Completed or Partial; RecoveryDataWriter did it on its own. NotProcessed, given before an
// operation starts, stays with the callers that refuse it.
public sealed partial class SourceOutcomeOwnerTests
{
    private static readonly string SrcRoot = Path.Combine(FindRepoRoot(), "src");
    private const string Owner = "Archiver.Core/Services/SourceOutcomeRules.cs";

    [Fact]
    public void CompletedOrPartial_IsSetOnlyByTheOwner()
    {
        string[] found = [.. SourceFiles().Where(f => f != Owner)
            .SelectMany(f => Setting().Matches(Code(f)).Select(m => $"{f}: {m.Value}"))];
        found.Should().BeEmpty("a source's Completed or Partial is SourceOutcomeRules' to give (T-F511)");
    }

    [Fact]
    public void Scan_SeesTheOwner()
    {
        Setting().Matches(Code(Owner)).Should().NotBeEmpty();
        Setting().Matches("s with { Outcome = SourceOutcome.Partial }; Outcome = SourceOutcome.NotProcessed; x => SourceOutcome.Completed,")
            .Select(m => m.Value).Should().Equal("SourceOutcome.Partial", "SourceOutcome.Completed");
    }

    [Fact]
    public void DowngradeAfterLaterError_KeepsEveryCompletedSource_WithNoCause()
    {
        SourceResult[] sources =
        [
            new() { Path = @"C:\a", Outcome = SourceOutcome.Completed },
            new() { Path = @"C:\b", Outcome = SourceOutcome.Partial, Cause = PartialCause.HoldsAnOutput },
            new() { Path = @"C:\c", Outcome = SourceOutcome.NotProcessed },
        ];

        SourceOutcomeRules.DowngradeAfterLaterError(sources).Should().Equal(
            new SourceResult { Path = @"C:\a", Outcome = SourceOutcome.Partial },
            sources[1],
            sources[2]);
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SrcRoot, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal));

    // The file without its comment lines and XML docs, so a <see cref> is not a use.
    private static string Code(string file) =>
        string.Join('\n', File.ReadAllLines(Path.Combine(SrcRoot, file.Replace('/', Path.DirectorySeparatorChar)))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    // A use of Completed or Partial as a value: after "=" or "=>", not a comparison ("== ...", "!= ...").
    [GeneratedRegex(@"(?<=(?<![=!<>])=\s*|=>\s*)SourceOutcome\.(Completed|Partial)\b")]
    private static partial Regex Setting();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
