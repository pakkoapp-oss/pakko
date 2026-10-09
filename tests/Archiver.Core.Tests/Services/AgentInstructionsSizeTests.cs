using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F369: CLAUDE.md loads into every agent session, so it has a size gate; area rules live in
// .claude/rules/*.md and load only when the agent touches a file matching their paths: globs.
// An unquoted glob starting with '*' is a YAML alias, the frontmatter fails to parse and Claude
// Code then loads the rule unconditionally, so every glob must be quoted.
public sealed partial class AgentInstructionsSizeTests
{
    private const int CoreLimitBytes = 26_000;
    private const int RuleLimitBytes = 12_000;

    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string RulesDir = Path.Combine(RepoRoot, ".claude", "rules");

    [Fact]
    public void RootClaudeMd_StaysUnderTheGate()
    {
        long size = Encoding.UTF8.GetByteCount(File.ReadAllText(Path.Combine(RepoRoot, "CLAUDE.md")));

        size.Should().BeLessThanOrEqualTo(CoreLimitBytes,
            "CLAUDE.md loads into every session: pair each addition with a deletion, move area rules to .claude/rules/ and history to docs/DECISIONS.md");
    }

    [Fact]
    public void RulesDirectory_HasRules()
    {
        Directory.GetFiles(RulesDir, "*.md").Should().NotBeEmpty();
    }

    [Fact]
    public void EveryRule_IsPathScopedWithQuotedGlobs_AndUnderTheGate()
    {
        foreach (string file in Directory.GetFiles(RulesDir, "*.md", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file).ReplaceLineEndings("\n");
            string name = Path.GetFileName(file);

            Encoding.UTF8.GetByteCount(text).Should().BeLessThanOrEqualTo(RuleLimitBytes, name);

            Match front = Frontmatter().Match(text);
            front.Success.Should().BeTrue(name + " needs a paths: frontmatter");
            string[] entries = front.Groups[1].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            entries.Should().NotBeEmpty(name);
            entries.Should().AllSatisfy(e => QuotedGlob().IsMatch(e).Should().BeTrue(name + ": " + e));
        }
    }

    [Theory]
    [InlineData("src")]
    [InlineData("tests")]
    [InlineData("scripts")]
    [InlineData("docs")]
    public void NoNestedClaudeMd_RulesHaveOneHome(string dir)
    {
        Directory.GetFiles(Path.Combine(RepoRoot, dir), "CLAUDE.md", SearchOption.AllDirectories)
            .Should().BeEmpty("area rules go in .claude/rules/");
    }

    [GeneratedRegex(@"\A---\npaths:\n((?:[^\n]*\n)*?)---\n")]
    private static partial Regex Frontmatter();

    [GeneratedRegex(@"^  - ""[^""]+""$")]
    private static partial Regex QuotedGlob();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
