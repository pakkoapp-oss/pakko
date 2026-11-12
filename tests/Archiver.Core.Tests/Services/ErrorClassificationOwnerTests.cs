using System.Text.RegularExpressions;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F511 PR 6: an error is classified in one place. A Windows code is read from an exception only by
// CoreMessages (IsDiskFull, IsSharingViolation, IsAlreadyExists, and the code it shows); a decision on
// an exception's text is listed with whose text it is; a catch of everything in Core says why, or
// rethrows.
public sealed partial class ErrorClassificationOwnerTests
{
    private static readonly string SrcRoot = Path.Combine(FindRepoRoot(), "src");
    private const string Owner = "Archiver.Core/Services/CoreMessages.cs";

    private static readonly (string File, string Use, int Count, string Why)[] AllowedTextDecisions =
    [
        ("Archiver.Core/IO/VerifyingReadStream.cs", ".Message ==", 1, "the BCL's own text, pinned by VerifyingReadStreamTests against the exception ZipArchive throws"),
        ("Archiver.Core/IO/VerifyingReadStream.cs", ".Message !=", 1, "the BCL's own text, pinned by VerifyingReadStreamTests against the exception ZipArchive throws"),
        ("Archiver.Core/Services/TarSandboxedService.cs", "IsLikelyEncryptionFailure(ex.Message)", 2, "libarchive's stderr inside the exception: its fixed English phrases (T-F518)"),
    ];

    // T-F406 checks a public operation's paths at its entry (InputPaths.Problem), so an argument error
    // is no failure to catch per item. These are the ones left, each with why; a new one fails here.
    private static readonly (string File, int Count, string Why)[] AllowedArgumentCatches =
    [
        ("Archiver.Core/Services/InputPaths.cs", 1, "the gate itself: GetFullPath is how it learns a path cannot be parsed"),
        ("Archiver.Core/Services/DestinationFolder.cs", 1, "the destination's gate, as InputPaths"),
        ("Archiver.Core/Services/SourceKey.cs", 1, "a key for a path no gate has seen (the frontends' snapshot): none, not a throw"),
        ("Archiver.Core/Services/SourcePathNormalizer.cs", 1, "runs before the engine's gate, which reports the same path as not found"),
        ("Archiver.Core/Recovery/RecoveryTestStep.cs", 2, "a .par2 path given to Test, before any gate; a set found beside an archive is optional"),
        ("Archiver.Core/Services/RecoveryDataLookup.cs", 2, "a lookup the frontends make for a path no operation has gated yet"),
        ("Archiver.Core/Recovery/RecoveryDataWriter.cs", 2, "listing the archive's folder after it is written: a failure is a warning, the archive stays"),
        ("Archiver.Core/Services/RecoveryService.cs", 1, "the repaired copy's name is built from the archive's and the output folder: reported as RecoveryRepairNotWritten"),
        ("Archiver.Core/Services/FileTimes.cs", 3, "a time from an archive entry Windows cannot store (DateTime out of FILETIME range): best-effort metadata"),
        ("Archiver.Core/Services/ArchiveCreationRouter.cs", 1, "the archive's time from its sources: best-effort metadata"),
        ("Archiver.Core/Services/ExtractionRouter.cs", 1, "the total for progress only: an unknown length counts 0"),
        ("Archiver.Core/Services/BareGzip.cs", 2, "a probe of whether a .gz is a bare gzip: an unreadable one is left to the reader, which reports it"),
        ("Archiver.Core/IO/DiskSeekPenalty.cs", 1, "a hint for the writer's parallelism: unknown is null"),
        ("Archiver.Core/IO/TempOwner.cs", 1, "Process.GetProcessById's documented 'no process has this id'"),
        ("Archiver.Core/Services/DirectoryJunction.cs", 1, "DriveInfo's documented 'not a drive' for a root it cannot name"),
    ];

    [Fact]
    public void ArgumentCatch_InCore_IsOnlyTheListedOnes()
    {
        var found = new Dictionary<string, int>();
        foreach (string file in SourceFiles().Where(f => f.StartsWith("Archiver.Core/", StringComparison.Ordinal)))
        {
            int count = ArgumentCatch().Count(Code(file));
            if (count > 0)
                found[file] = count;
        }
        Dictionary<string, int> allowed = AllowedArgumentCatches.ToDictionary(a => a.File, a => a.Count);

        string[] unlisted = [.. found.Where(f => f.Value > allowed.GetValueOrDefault(f.Key)).Select(f => $"{f.Key} x{f.Value}")];
        string[] stale = [.. allowed.Where(a => a.Value > found.GetValueOrDefault(a.Key)).Select(a => $"{a.Key} x{a.Value}")];

        string.Join(Environment.NewLine, unlisted).Should().BeEmpty("a path is checked at the operation's entry, not caught per item (T-F406, T-F511)");
        string.Join(Environment.NewLine, stale).Should().BeEmpty("a listed catch that is gone leaves room for a new one");
    }

    [Fact]
    public void HResult_IsReadOnlyByTheOwner()
    {
        string[] found = [.. SourceFiles().Where(f => f != Owner)
            .SelectMany(f => HResult().Matches(Code(f)).Select(_ => f))];
        found.Should().BeEmpty("a Windows code is CoreMessages' to read (IsDiskFull, IsSharingViolation, IsAlreadyExists; T-F511)");
    }

    [Fact]
    public void DecisionOnExceptionText_IsOnlyTheListedOnes()
    {
        var found = new Dictionary<(string File, string Use), int>();
        foreach (string file in SourceFiles())
            foreach (Match match in TextDecision().Matches(Code(file)))
                found[(file, match.Value)] = found.GetValueOrDefault((file, match.Value)) + 1;
        Dictionary<(string File, string Use), int> allowed = AllowedTextDecisions.ToDictionary(a => (a.File, a.Use), a => a.Count);

        string[] unlisted = [.. found.Where(f => f.Value > allowed.GetValueOrDefault(f.Key)).Select(f => $"{f.Key.File}: {f.Key.Use} x{f.Value}")];
        string[] stale = [.. allowed.Where(a => a.Value > found.GetValueOrDefault(a.Key)).Select(a => $"{a.Key.File}: {a.Key.Use} x{a.Value}")];

        string.Join(Environment.NewLine, unlisted).Should().BeEmpty("an exception's text is a language and a version, not a classification (T-F511)");
        string.Join(Environment.NewLine, stale).Should().BeEmpty("a listed use that is gone leaves room for a new one");
    }

    [Fact]
    public void CatchOfEverything_InCore_SaysWhyOrRethrows()
    {
        string[] silent = [.. SourceFiles().Where(f => f.StartsWith("Archiver.Core/", StringComparison.Ordinal))
            .SelectMany(f => SilentCatches(File.ReadAllLines(Path.Combine(SrcRoot, f.Replace('/', Path.DirectorySeparatorChar)))).Select(line => $"{f}:{line}"))];
        silent.Should().BeEmpty("a catch of every exception in Core says why on its line or the block's first, or rethrows (T-F511)");
    }

    [Fact]
    public void Scan_SeesWhatItGuards()
    {
        HResult().Matches(Code(Owner)).Should().NotBeEmpty();
        ArgumentCatch().Count("catch (ArgumentException) {} when (ex is IOException\n or ArgumentException or X) when (ex is ArgumentException or Y) throw new ArgumentException(")
            .Should().Be(3);
        TextDecision().Matches("if (ex.Message == A) x; ex.Message.Contains(\"b\"); IsFoo(ex.Message); Log(ex.Message);")
            .Select(m => m.Value).Should().Equal(".Message ==", ".Message.Contains(", "IsFoo(ex.Message)");
        SilentCatches(
        [
            "catch (Exception)", "{", "    return null;", "}",
            "catch (Exception ex) // why", "{", "}",
            "catch { /* best-effort */ }",
            "catch", "{", "    Cleanup();", "    throw;", "}",
            "catch (Exception ex) when (ex is IOException)", "{", "}",
            "catch", "{", "    // why", "    return false;", "}",
            "try { x = f(); } catch { x = 0; }",
            "try { x = f(); } catch { /* best-effort */ }",
        ]).Should().Equal(1, 22);
    }

    // The 1-based lines of each catch of everything (bare, or Exception with no filter) that has no
    // comment on its line or the block's first line, and does not rethrow.
    private static IEnumerable<int> SilentCatches(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("//", StringComparison.Ordinal))
                continue;
            // A one-line "try { ... } catch { ... }": its reason is on the line, after the catch.
            if (CatchAllInLine().Match(line) is { Success: true } inLine)
            {
                string rest = line[inLine.Index..];
                if (!rest.Contains("//", StringComparison.Ordinal) && !rest.Contains("/*", StringComparison.Ordinal))
                    yield return i + 1;
                continue;
            }
            if (!CatchAll().IsMatch(line))
                continue;
            if (line.Contains("//", StringComparison.Ordinal) || line.Contains("/*", StringComparison.Ordinal))
                continue;
            string block = Block(lines, i);
            string firstInside = i + 2 < lines.Length && lines[i + 1].Trim() == "{" ? lines[i + 2].Trim() : "";
            if (firstInside.StartsWith("//", StringComparison.Ordinal) || Rethrow().IsMatch(block))
                continue;
            yield return i + 1;
        }
    }

    // The catch's block: from its line to the brace that closes the first one opened.
    private static string Block(string[] lines, int start)
    {
        int depth = 0;
        bool opened = false;
        var block = new System.Text.StringBuilder();
        for (int i = start; i < lines.Length; i++)
        {
            block.AppendLine(lines[i]);
            foreach (char c in lines[i])
            {
                if (c == '{') { depth++; opened = true; }
                else if (c == '}') depth--;
            }
            if (opened && depth <= 0)
                break;
        }
        return block.ToString();
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SrcRoot, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal));

    private static string Code(string file) =>
        string.Join('\n', File.ReadAllLines(Path.Combine(SrcRoot, file.Replace('/', Path.DirectorySeparatorChar)))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    // A catch of ArgumentException: on its own, or in a "when (ex is ...)" filter, on one line or more.
    [GeneratedRegex(@"catch \(ArgumentException\b|\bor ArgumentException\b|\bis ArgumentException\b")]
    private static partial Regex ArgumentCatch();

    [GeneratedRegex(@"\.HResult\b")]
    private static partial Regex HResult();

    // A comparison or search of an exception's text, or the text handed to an Is... predicate.
    [GeneratedRegex(@"\.Message\s*(==|!=)|\.Message\.(Contains|StartsWith|EndsWith|Equals|IndexOf)\(|\bIs\w*\(\w+\.Message\)")]
    private static partial Regex TextDecision();

    // "catch", "catch (Exception)" or "catch (Exception ex)" with no "when" filter.
    [GeneratedRegex(@"^(\}\s*)?catch(\s*$|\s*\{|\s*\(Exception(\s+\w+)?\)(?!\s*when))")]
    private static partial Regex CatchAll();

    // The same catch after a statement or a block on the same line.
    [GeneratedRegex(@"(?<=[;}])\s*catch(\s*$|\s*\{|\s*\(Exception(\s+\w+)?\)(?!\s*when))")]
    private static partial Regex CatchAllInLine();

    [GeneratedRegex(@"\bthrow\s*;|\bthrow\s+\w")]
    private static partial Regex Rethrow();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
