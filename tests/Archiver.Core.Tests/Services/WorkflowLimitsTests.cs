using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F366: a hung job holds a runner for GitHub's 6-hour default, so every job sets its own limit;
// a newer push to a branch cancels the superseded run, never a tag or a workflow_dispatch build.
public sealed partial class WorkflowLimitsTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Theory]
    [InlineData("build.yml")]
    [InlineData("canary.yml")]
    public void EveryJob_HasItsOwnTimeout(string file)
    {
        List<(string Name, string Body)> jobs = WorkflowJobs.Parse(RepoRoot, file);

        jobs.Should().NotBeEmpty();
        foreach ((string name, string body) in jobs)
        {
            Match timeout = JobTimeout().Match(body);
            timeout.Success.Should().BeTrue($"{file} job {name} needs a job-level timeout-minutes");
            int.Parse(timeout.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                .Should().BeInRange(1, 120, $"{file} job {name}");
        }
    }

    [Fact]
    public void Build_CancelsOnlySupersededBranchRuns()
    {
        string concurrency = TopLevelBlock(WorkflowJobs.Text(RepoRoot, "build.yml"), "concurrency");

        concurrency.Should().Contain("cancel-in-progress: true");
        // Branch pushes and pull requests share a group per ref; a tag push or a dispatch gets a group of its own.
        concurrency.Should().Contain("startsWith(github.ref, 'refs/heads/')");
        concurrency.Should().Contain("github.event_name == 'pull_request'");
        concurrency.Should().Contain("|| github.run_id");
    }

    [Fact]
    public void Canary_IsNeverCancelledByAnotherRun()
    {
        // The release checklist dispatches the canary on purpose; a second dispatch must not cancel it.
        TopLevelBlock(WorkflowJobs.Text(RepoRoot, "canary.yml"), "concurrency").Should().BeEmpty();
    }

    [Fact]
    public void CanaryStatus_CountsATimedOutJobAsAFailedDay()
    {
        // A job past its timeout reports "cancelled" in needs, the same as a human cancel.
        string status = WorkflowJobs.Job(RepoRoot, "canary.yml", "canary-status");

        // Only the check run's annotation tells a timeout from a human cancel (probed, T-F366).
        status.Should().Contain("checks: read");
        status.Should().Contain("/annotations");
        status.Should().Contain("exceeded the maximum execution time");
    }

    private static string TopLevelBlock(string workflow, string key)
    {
        int start = workflow.IndexOf("\n" + key + ":", StringComparison.Ordinal);
        if (start < 0)
            return "";
        int end = workflow.IndexOf("\n\n", start + 1, StringComparison.Ordinal);
        return workflow[start..(end < 0 ? workflow.Length : end)];
    }

    [GeneratedRegex(@"^    timeout-minutes: (\d+)$", RegexOptions.Multiline)]
    private static partial Regex JobTimeout();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
