using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F365: each MSIX and CLI zip gets a CycloneDX SBOM, attested. The third-party generator runs in a
// job of its own with no secrets and no id-token; the jobs holding the signing key or the OIDC token
// only run the first-party actions/attest.
public sealed partial class SbomWorkflowTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void ToolManifest_PinsTheCycloneDxTool()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, ".config", "dotnet-tools.json")));
        JsonElement tool = json.RootElement.GetProperty("tools").GetProperty("cyclonedx");

        tool.GetProperty("version").GetString().Should().MatchRegex(@"^\d+\.\d+\.\d+$");
        tool.GetProperty("rollForward").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void SbomJob_ReadsOnlyAndHasNoSecrets()
    {
        string job = Job("sbom");

        job.Should().Contain("New-Sbom.ps1");
        job.Should().Contain("contents: read");
        job.Should().NotContain("secrets.");
        job.Should().NotContain("id-token");
        job.Should().NotContain("write");
    }

    [Fact]
    public void NoPrivilegedJob_RunsTheGenerator()
    {
        List<(string Name, string Body)> privileged = [.. Jobs().Where(j => j.Body.Contains("secrets.", StringComparison.Ordinal) || j.Body.Contains("id-token", StringComparison.Ordinal))];

        privileged.Select(j => j.Name).Should().Contain(["build-msix", "build-cli"]);
        foreach ((string name, string job) in privileged)
        {
            job.Should().NotContain("New-Sbom.ps1", name);
            job.Should().NotContain("dotnet tool restore", name);
            job.Should().NotContain("dotnet-CycloneDX", name);
        }
    }

    [Theory]
    [InlineData("build-msix")]
    [InlineData("build-cli")]
    public void BuildJob_AttestsItsSbom(string name)
    {
        string job = Job(name);

        NeedsLine(job).Should().Contain("sbom");
        job.Should().Contain("uses: actions/attest@");
        job.Should().Contain("sbom-path:");
        job.Should().NotContain("attest-sbom");
    }

    [Fact]
    public void Release_PublishesTheSboms()
    {
        string job = Job("release");

        job.Should().Contain("name: pakko-sbom");
        job.Should().Contain("*.cdx.json");
    }

    [Fact]
    public void Script_KeepsBuildOnlyAndUnshippedPackagesOut()
    {
        string script = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "New-Sbom.ps1"));

        foreach (string package in (string[])["Microsoft.Windows.SDK.BuildTools", "Microsoft.Windows.AI.MachineLearning", "System.Numerics.Tensors"])
            script.Should().Contain(package);
        script.Should().Contain("--locked-mode");
    }

    private static string NeedsLine(string job) =>
        job.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("needs:", StringComparison.Ordinal)) ?? "";

    private static string Job(string name) =>
        Jobs().Where(j => j.Name == name).Select(j => j.Body).SingleOrDefault() ?? "";

    private static List<(string Name, string Body)> Jobs()
    {
        // Comment lines go: the one above a job would otherwise count as the previous job's text.
        string workflow = string.Join('\n', File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "build.yml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Where(l => !l.TrimStart().StartsWith('#')));
        string jobs = workflow[(workflow.IndexOf("\njobs:\n", StringComparison.Ordinal) + 7)..];
        MatchCollection headers = JobHeader().Matches(jobs);
        return [.. headers.Select((m, i) => (m.Groups[1].Value,
            jobs[m.Index..(i + 1 < headers.Count ? headers[i + 1].Index : jobs.Length)]))];
    }

    [GeneratedRegex(@"^  ([a-z0-9-]+):\n", RegexOptions.Multiline)]
    private static partial Regex JobHeader();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
