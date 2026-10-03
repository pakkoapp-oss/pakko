using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F112: diagram 6's three check tables in docs/DIAGRAMS.md are a contract — each row must equal
// what the code decides, and the tables must cover the whole decision, so neither side can drift.
public sealed class DiagramSixTests
{
    private static readonly string Section = ReadSection();

    [Fact]
    public void BrowseLocationTable_MatchesCode_AndCoversTheWholeDomain()
    {
        var rows = Table("browse-location");
        var seen = new List<(bool, bool, bool)>();
        foreach (string[] row in rows)
        {
            (bool inside, bool nested, bool isZip) = (YesNo(row[0]), YesNo(row[1]), YesNo(row[2]));
            seen.Add((inside, nested, isZip));
            BrowseLocationState.For(inside, nested, isZip).Should().Be(
                new BrowseLocationState(YesNo(row[3]), YesNo(row[4]), YesNo(row[5]), YesNo(row[6]), YesNo(row[7])),
                "row {0}", string.Join(" | ", row));
        }

        bool[] both = [false, true];
        seen.Should().BeEquivalentTo(
            from i in both from n in both from z in both select (i, n, z),
            "every input of BrowseLocationState.For appears exactly once");
    }

    [Fact]
    public void RowOpenTable_MatchesCode_AndUsesEveryAction()
    {
        var rows = Table("row-open");
        foreach (string[] row in rows)
        {
            bool isBusy = YesNo(row[1]);
            bool isFolder = row[3].EndsWith('/');
            bool probed = false;
            Func<bool> probe = row[4] == "-"
                ? () => throw new InvalidOperationException("the table says the disk probe must not run: " + string.Join(" | ", row))
                : () => { probed = true; return YesNo(row[4]); };
            RowOpenAction actual = row[0] switch
            {
                "pending" => BrowserEntryRouting.DecidePendingRow(isBusy, isFolder, probe),
                "browser" => BrowserEntryRouting.DecideBrowserRow(isBusy, YesNo(row[2]), isFolder, row[3].TrimEnd('/'), probe),
                _ => throw new InvalidOperationException("unknown list: " + row[0]),
            };
            actual.Should().Be(Enum.Parse<RowOpenAction>(row[5]), "row {0}", string.Join(" | ", row));
            probed.Should().Be(row[4] != "-", "the table's probe column says whether the disk is read: {0}", string.Join(" | ", row));
        }

        rows.Select(r => Enum.Parse<RowOpenAction>(r[5])).Should().Contain(Enum.GetValues<RowOpenAction>());
    }

    [Fact]
    public void BrowseUpTable_MatchesCode_UsesEveryStep_AndEachRowIsAnArrow()
    {
        var stateOf = new Dictionary<ArchiveBrowseScope, string>
        {
            [ArchiveBrowseScope.Archive] = "InsideArchive",
            [ArchiveBrowseScope.RealFileSystem] = "RealFolder",
            [ArchiveBrowseScope.ThisPc] = "ThisPcState",
        };
        string stateDiagram = Section[Section.IndexOf("stateDiagram-v2", StringComparison.Ordinal)..];
        stateDiagram = stateDiagram[..stateDiagram.IndexOf("```", StringComparison.Ordinal)];

        var rows = Table("browse-up");
        foreach (string[] row in rows)
        {
            var scope = Enum.Parse<ArchiveBrowseScope>(row[0]);
            var step = Enum.Parse<BrowseUpStep>(row[4]);
            string? archive = row[3] == "-" ? null : row[3];

            BrowseNavigation.DecideUp(scope, row[1], int.Parse(row[2]), archive)
                .Should().Be(step, "row {0}", string.Join(" | ", row));
            row[5].Should().Be(stateOf[scope], "From is the state of the row's scope");
            stateDiagram.Should().MatchRegex(
                $@"(?m)^\s*{row[5]} --> {row[6]}: Up \S+ {step}\b", "the diagram draws Up {0} from {1} to {2}", step, row[5], row[6]);
        }

        rows.Select(r => Enum.Parse<BrowseUpStep>(r[4])).Should().Contain(Enum.GetValues<BrowseUpStep>());
    }

    private static bool YesNo(string cell) => cell switch
    {
        "yes" => true,
        "no" => false,
        _ => throw new InvalidOperationException("expected yes or no, got: " + cell),
    };

    private static List<string[]> Table(string name)
    {
        string start = $"<!-- check:{name} -->";
        int from = Section.IndexOf(start, StringComparison.Ordinal);
        from.Should().BeGreaterThan(-1, "diagram 6 has the {0} table", name);
        int to = Section.IndexOf("<!-- /check -->", from, StringComparison.Ordinal);
        List<string[]> rows = [.. Section[(from + start.Length)..to]
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('|'))
            .Skip(2)
            .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim().Trim('`')).ToArray())];
        rows.Should().NotBeEmpty();
        return rows;
    }

    private static string ReadSection()
    {
        string text = File.ReadAllText(Path.Combine(FindRepoRoot(), "docs", "DIAGRAMS.md"));
        int from = text.IndexOf("## 6. State", StringComparison.Ordinal);
        int to = text.IndexOf("## 7. ", from, StringComparison.Ordinal);
        return text[from..to].Replace("\r\n", "\n");
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
