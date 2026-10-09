using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F364: CI restores exactly the committed package graph. PublishAot puts the SDK's own
// ILCompiler/ILLink version into the lock files, so the SDK is pinned exactly too.
public sealed class BuildReproducibilityTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void PackageReferences_HaveNoFloatingVersions()
    {
        string[] floating = [.. SdkProjects()
            .SelectMany(p => XDocument.Load(p).Descendants()
                .Where(e => e.Name.LocalName == "PackageReference")
                .Where(e => ((string?)e.Attribute("Version") ?? "").Contains('*', StringComparison.Ordinal))
                .Select(e => Path.GetFileName(p) + ": " + (string?)e.Attribute("Include")))];

        floating.Should().BeEmpty();
    }

    [Fact]
    public void EverySdkProject_HasALockFile()
    {
        string[] missing = [.. SdkProjects()
            .Where(p => !File.Exists(Path.Combine(Path.GetDirectoryName(p)!, "packages.lock.json")))
            .Select(Path.GetFileName)!];

        missing.Should().BeEmpty();
    }

    [Fact]
    public void BuildProps_LockRestoreAndEnforceItInCiOnly()
    {
        var props = XDocument.Load(Path.Combine(RepoRoot, "Directory.Build.props"));

        props.Descendants().Where(e => e.Name.LocalName == "RestorePackagesWithLockFile")
            .Select(e => e.Value.Trim()).Should().Equal("true");
        XElement locked = props.Descendants().Single(e => e.Name.LocalName == "RestoreLockedMode");
        locked.Value.Trim().Should().Be("true");
        ((string?)locked.Attribute("Condition")).Should().Contain("$(CI)").And.Contain("$(PAKKO_FLOATING_TOOLCHAIN)")
            // A Linux restore adds linux-x64 (the AOT host package) to the exes' RID set; GitHub's
            // dependency submission runs there and failed NU1004 in locked mode.
            .And.Contain("'$(OS)' == 'Windows_NT'");
    }

    [Fact]
    public void GlobalJson_PinsTheExactSdk()
    {
        // The canary rewrites global.json to float the SDK on purpose (canary.yml).
        if (Environment.GetEnvironmentVariable("PAKKO_FLOATING_TOOLCHAIN") == "1")
            return;

        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "global.json")));
        JsonElement sdk = json.RootElement.GetProperty("sdk");

        sdk.GetProperty("rollForward").GetString().Should().Be("disable");
        sdk.GetProperty("version").GetString().Should().MatchRegex(@"^\d+\.\d+\.\d{3}$");
    }

    [Fact]
    public void BuildWorkflow_TakesTheSdkFromGlobalJson()
    {
        string workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "build.yml"));

        workflow.Should().NotContain("dotnet-version:");
        workflow.Should().Contain("global-json-file: global.json");
    }

    private static readonly string[] ProjectRoots = ["src", "tests", "tools"];

    private static IEnumerable<string> SdkProjects() =>
        ProjectRoots
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(RepoRoot, d), "*.csproj", SearchOption.AllDirectories))
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj"));

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
