using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F371: scripts run on pwsh 7 only, a retried CI step leaves a warning annotation, and the
// Scorecard job keeps the shape its publish_results mode accepts.
public sealed partial class DevOpsHygieneTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    // End-user remediation for T-F233 (linked from SECURITY.md): a stock Windows has only 5.1.
    private static readonly string[] EndUserScripts = ["Find-PakkoSandboxAce.ps1", "Repair-PakkoSandboxAce.ps1"];

    [Fact]
    public void EveryDevScript_RequiresPwsh7_AndEndUserScriptsStayOn51()
    {
        string[] scripts = Directory.GetFiles(Path.Combine(RepoRoot, "scripts"), "*.ps1", SearchOption.AllDirectories);

        scripts.Should().HaveCountGreaterThan(EndUserScripts.Length);
        string[] wrong = [.. scripts
            .Where(p => File.ReadLines(p).First() != (EndUserScripts.Contains(Path.GetFileName(p))
                ? "#Requires -Version 5.1"
                : "#Requires -Version 7.0"))
            .Select(Path.GetFileName)!];
        wrong.Should().BeEmpty();
    }

    [Fact]
    public void AppPostBuildDeploy_RunsPwsh()
    {
        var project = XDocument.Load(Path.Combine(RepoRoot, "src", "Archiver.App", "Archiver.App.csproj"));
        string[] deployExecs = [.. project.Descendants()
            .Where(e => e.Name.LocalName == "Exec")
            .Select(e => (string?)e.Attribute("Command") ?? "")
            .Where(c => c.Contains("Deploy.ps1", StringComparison.Ordinal))];

        deployExecs.Should().ContainSingle().Which.Should().StartWith("pwsh.exe ");
    }

    [Theory]
    [InlineData("build.yml", 2)]
    [InlineData("canary.yml", 6)]
    public void EveryRetryLoop_AnnotatesItsRetry(string file, int loops)
    {
        MatchCollection found = RetryLoop().Matches(WorkflowJobs.Text(RepoRoot, file));

        found.Should().HaveCount(loops);
        foreach (Match loop in found)
            loop.Value.Should().Contain("::warning title=Retried::");
    }

    [Fact]
    public void ScorecardJob_HasOnlyPinnedUsesSteps()
    {
        string workflow = WorkflowJobs.Text(RepoRoot, "scorecard.yml");
        string job = WorkflowJobs.Job(RepoRoot, "scorecard.yml", "analysis");

        workflow.Should().Contain("\npermissions:\n  contents: read\n");
        workflow.Should().NotContain("\nenv:").And.NotContain("\ndefaults:");
        job.Should().Contain("publish_results: true");
        job.Should().NotContain("run:");
        string[] uses = [.. UsesLine().Matches(job).Select(m => m.Groups[1].Value)];
        uses.Should().HaveCount(4);
        uses.Where(u => !u.StartsWith("actions/", StringComparison.Ordinal))
            .Should().OnlyContain(u => ShaPin().IsMatch(u));
    }

    [GeneratedRegex(@"\$attempts = \d+.*?throw ", RegexOptions.Singleline)]
    private static partial Regex RetryLoop();

    [GeneratedRegex(@"uses: (\S+)")]
    private static partial Regex UsesLine();

    [GeneratedRegex("@[0-9a-f]{40}$")]
    private static partial Regex ShaPin();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
