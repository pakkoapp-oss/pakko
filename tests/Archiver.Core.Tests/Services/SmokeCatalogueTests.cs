using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F378: docs/SMOKE.md is the one catalogue of on-device checks. Its "Covers" column names the
// entry points a row exercises; these tests read the entry points from the source and fail when
// one has no row, or a row names one that is gone, so the catalogue cannot fall behind the app.
public sealed partial class SmokeCatalogueTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string[] Prefixes = ["CLI", "SHL", "EXP", "APP", "OPW", "PAR", "POL", "PKG", "PRF", "REL", "STO"];
    private static readonly string[] Tiers = ["quick", "release", "store", "retired"];
    private static readonly string[] Executors = ["script", "mcp", "uac", "user", "store-machine"];
    private static readonly string[] Artifacts = ["dev", "tag", "store-bundle", "store-install"];
    private static readonly string[] EntryKinds = ["cli", "shell", "menu", "app", "policy"];

    private sealed record Row(string Id, string Tier, string By, string On, string Action, string Expect, string[] Covers);

    private static readonly List<Row> Rows = ReadRows();

    [Fact]
    public void EveryRow_HasAnIdOfItsSurface_NumberedWithoutGaps()
    {
        Rows.Should().NotBeEmpty();
        Rows.Select(r => r.Id).Should().OnlyHaveUniqueItems();
        foreach (IGrouping<string, Row> surface in Rows.GroupBy(r => r.Id[..3]))
        {
            Prefixes.Should().Contain(surface.Key);
            // A removed row would leave a gap: a row that no longer applies is marked retired instead.
            surface.Select(r => int.Parse(r.Id[4..])).Should().Equal(Enumerable.Range(1, surface.Count()), "surface {0}", surface.Key);
        }

        Rows.Select(r => r.Id[..3]).Distinct().Should().BeEquivalentTo(Prefixes);
    }

    [Fact]
    public void EveryRow_UsesTheVocabulary_AndSaysWhatToDoAndExpect()
    {
        foreach (Row row in Rows)
        {
            Tiers.Should().Contain(row.Tier, "row {0}", row.Id);
            Executors.Should().Contain(row.By, "row {0}", row.Id);
            Artifacts.Should().Contain(row.On, "row {0}", row.Id);
            row.Action.Should().NotBeNullOrWhiteSpace("row {0}", row.Id);
            row.Expect.Should().NotBeNullOrWhiteSpace("row {0}", row.Id);
        }
    }

    [Fact]
    public void QuickRows_NeedNoPersonAndNoRelease()
    {
        foreach (Row row in Rows.Where(r => r.Tier == "quick"))
        {
            row.By.Should().Be("script", "the quick tier runs unattended: {0}", row.Id);
            row.On.Should().Be("dev", "the quick tier runs on the deployed dev package: {0}", row.Id);
        }
    }

    [Fact]
    public void EveryEntryPointInTheCode_HasALiveRow()
    {
        HashSet<string> covered = [.. Rows.Where(r => r.Tier != "retired").SelectMany(r => r.Covers)];

        EntryPoints().Where(e => !covered.Contains(e)).Should().BeEmpty(
            "each command, switch, menu item and policy needs a row in docs/SMOKE.md that names it in Covers");
    }

    [Fact]
    public void EveryCoveredName_ExistsInTheCode_AndEveryTaskIsKnown()
    {
        HashSet<string> entryPoints = [.. EntryPoints()];
        string tasks = File.ReadAllText(Path.Combine(RepoRoot, "docs", "TASKS.md"))
            + File.ReadAllText(Path.Combine(RepoRoot, "docs", "TASKS_DONE.md"));

        foreach (Row row in Rows.Where(r => r.Tier != "retired"))
        {
            foreach (string name in row.Covers)
            {
                if (TaskId().IsMatch(name))
                    tasks.Contains($"### {name} ", StringComparison.Ordinal).Should().BeTrue("row {0} names task {1}, which needs an entry in TASKS.md or TASKS_DONE.md", row.Id, name);
                else
                {
                    EntryKinds.Should().Contain(name.Split(':')[0], "row {0} covers '{1}'", row.Id, name);
                    entryPoints.Should().Contain(name, "row {0} covers it", row.Id);
                }
            }
        }
    }

    [Fact]
    public void EntryPoints_AreReadFromEverySource()
    {
        // Guards the readers below: a renamed file or a reshaped switch would otherwise leave an
        // empty list, and an empty list needs no rows.
        string[] found = [.. EntryPoints()];

        found.Should().Contain(["cli:a", "cli:r", "cli:u", "cli:-rr", "cli:-si", "cli:--help"]);
        found.Should().Contain(["shell:--recovery-repair", "shell:--paths-stdin", "shell:--browse"]);
        found.Should().Contain(["menu:RecoveryRepairCommand", "menu:TarArchiveCommand"]);
        found.Should().Contain(["app:RepairBrowsedArchive", "app:Archive", "app:Cancel"]);
        found.Should().Contain(["policy:DisableRecoveryData", "policy:EnforceMOTW"]);
        found.Should().NotContain("menu:PakkoRootCommand", "the root item only holds the others");
    }

    private static IEnumerable<string> EntryPoints()
    {
        string cli = Source("src", "Archiver.CLI", "CliArgumentParser.cs");
        string commandSwitch = CliCommandSwitch().Match(cli).Value;
        foreach (Match m in QuotedWord().Matches(commandSwitch))
            yield return "cli:" + m.Groups[1].Value;
        foreach (string name in CliSwitch().Matches(cli).Select(m => m.Groups[1].Value).Distinct())
            yield return "cli:" + name;

        string shell = Source("src", "Archiver.Shell", "ShellArgumentParser.cs") + Source("src", "Archiver.Shell", "StdinPathList.cs");
        foreach (string name in ShellArgument().Matches(shell).Select(m => m.Groups[1].Value).Distinct())
            yield return "shell:" + name;

        string menu = Source("src", "Archiver.ShellExtension", "ExplorerCommands.h");
        foreach (string name in MenuCommand().Matches(menu).Select(m => m.Groups[1].Value).Where(n => n != "PakkoRootCommand"))
            yield return "menu:" + name;

        string app = Source("src", "Archiver.App", "ViewModels", "MainViewModel.cs");
        foreach (Match m in AppCommand().Matches(app))
            yield return "app:" + (m.Groups[1].Value.EndsWith("Async", StringComparison.Ordinal) ? m.Groups[1].Value[..^5] : m.Groups[1].Value);

        string policy = Source("src", "Archiver.Core", "Services", "GroupPolicyService.cs");
        foreach (string name in PolicyValue().Matches(policy).Select(m => m.Groups[1].Value).Distinct())
            yield return "policy:" + name;
    }

    private static List<Row> ReadRows()
    {
        var rows = new List<Row>();
        foreach (string line in File.ReadLines(Path.Combine(RepoRoot, "docs", "SMOKE.md")))
        {
            if (!RowStart().IsMatch(line))
                continue;
            string[] cells = [.. line.Trim().Trim('|').Split('|').Select(c => c.Trim())];
            cells.Should().HaveCount(7, "a row has seven cells and no '|' inside one: {0}", line);
            rows.Add(new Row(cells[0], cells[1], cells[2], cells[3], cells[4], cells[5],
                cells[6].Replace("`", "").Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        }

        return rows;
    }

    // Without comment lines: a comment may quote a switch the parser does not take.
    private static string Source(params string[] parts) => string.Join(Environment.NewLine,
        File.ReadLines(Path.Combine([RepoRoot, .. parts])).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [GeneratedRegex(@"^\| [A-Z]{3}-\d{3} \|")]
    private static partial Regex RowStart();

    [GeneratedRegex(@"^T-F\d+$")]
    private static partial Regex TaskId();

    [GeneratedRegex(@"commandName switch\s*\{.*?var other", RegexOptions.Singleline)]
    private static partial Regex CliCommandSwitch();

    [GeneratedRegex("\"([a-z]+)\"")]
    private static partial Regex QuotedWord();

    [GeneratedRegex("\"(--?[a-z]+)\"")]
    private static partial Regex CliSwitch();

    [GeneratedRegex("\"(--[a-z-]+)\"")]
    private static partial Regex ShellArgument();

    [GeneratedRegex(@"class (\w+Command) final")]
    private static partial Regex MenuCommand();

    [GeneratedRegex(@"\[RelayCommand[^\]]*\]\s+(?:private|public|internal)\s+(?:async\s+)?(?:Task|void)\s+(\w+)\(")]
    private static partial Regex AppCommand();

    [GeneratedRegex("PolicyKeyPath, \"(\\w+)\"")]
    private static partial Regex PolicyValue();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
