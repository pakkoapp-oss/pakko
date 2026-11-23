using System.Text.RegularExpressions;
using Archiver.Core.IO;
using FluentAssertions;

namespace Archiver.Core.Tests.IO;

// T-F572: DirectoryWalker says what an entry is; each consumer decides what that means for its job,
// for every kind. A switch over WalkEntryKind names every kind and has no default, so a new kind
// fails here until each consumer has decided it (T-F557 and T-F559 each touched seven to nine files,
// and a consumer that did not was the next defect).
public sealed partial class WalkKindOwnerTests
{
    private static readonly string SrcRoot = Path.Combine(FindRepoRoot(), "src");
    private const string Owner = "Archiver.Core/IO/DirectoryWalker.cs";

    // A kind named outside a full switch, each with why; a new one fails here.
    private static readonly (string File, int Count, string Why)[] AllowedOutsideASwitch =
    [
        ("Archiver.Core/Services/TarSandboxedService.cs", 1, "the message for an entry the walk just put in the unopened list, which holds only the two kinds it names"),
    ];

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SrcRoot, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => f != Owner && !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal));

    private static string Code(string file) =>
        string.Join('\n', File.ReadAllLines(Path.Combine(SrcRoot, file.Replace('/', Path.DirectorySeparatorChar)))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    // Each "switch (...) { ... }" block whose cases name a WalkEntryKind, as (start, end) in the code.
    private static List<(int Start, int End)> WalkSwitches(string code)
    {
        List<(int, int)> blocks = [];
        foreach (Match match in SwitchStatement().Matches(code))
        {
            int open = code.IndexOf('{', match.Index + match.Length - 1);
            int depth = 0;
            int end = open;
            for (; end < code.Length; end++)
            {
                if (code[end] == '{')
                    depth++;
                else if (code[end] == '}' && --depth == 0)
                    break;
            }
            if (code[open..end].Contains("case WalkEntryKind.", StringComparison.Ordinal))
                blocks.Add((open, end));
        }
        return blocks;
    }

    [Fact]
    public void EverySwitchOverWalkKinds_NamesEveryKind_WithNoDefault()
    {
        string[] kinds = Enum.GetNames<WalkEntryKind>();
        List<string> problems = [];
        foreach (string file in SourceFiles())
        {
            string code = Code(file);
            foreach ((int start, int end) in WalkSwitches(code))
            {
                string block = code[start..end];
                string[] missing = [.. kinds.Where(k => !block.Contains($"WalkEntryKind.{k}", StringComparison.Ordinal))];
                if (missing.Length > 0)
                    problems.Add($"{file}: missing {string.Join(", ", missing)}");
                if (DefaultArm().IsMatch(block))
                    problems.Add($"{file}: a default arm");
            }
        }
        string.Join(Environment.NewLine, problems).Should().BeEmpty("each consumer decides every kind the walker can give (T-F572)");
    }

    [Fact]
    public void WalkKind_OutsideAFullSwitch_IsOnlyTheListedOnes()
    {
        var found = new Dictionary<string, int>();
        foreach (string file in SourceFiles())
        {
            string code = Code(file);
            List<(int Start, int End)> switches = WalkSwitches(code);
            int count = KindUse().Matches(code).Count(m => !switches.Any(s => m.Index > s.Start && m.Index < s.End));
            if (count > 0)
                found[file] = count;
        }
        Dictionary<string, int> allowed = AllowedOutsideASwitch.ToDictionary(a => a.File, a => a.Count);

        string[] unlisted = [.. found.Where(f => f.Value > allowed.GetValueOrDefault(f.Key)).Select(f => $"{f.Key} x{f.Value}")];
        string[] stale = [.. allowed.Where(a => a.Value > found.GetValueOrDefault(a.Key)).Select(a => $"{a.Key} x{a.Value}")];

        string.Join(Environment.NewLine, unlisted).Should().BeEmpty("a kind is decided in a switch that names every kind (T-F572)");
        string.Join(Environment.NewLine, stale).Should().BeEmpty("a listed use that is gone leaves room for a new one");
    }

    [GeneratedRegex(@"\bswitch\s*\([^)]*\)\s*\{")]
    private static partial Regex SwitchStatement();

    [GeneratedRegex(@"\bdefault\s*:")]
    private static partial Regex DefaultArm();

    [GeneratedRegex(@"WalkEntryKind\.\w+")]
    private static partial Regex KindUse();

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "windows-archiver-wrapper.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Repository root not found.");
    }
}
