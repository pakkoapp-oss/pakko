using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F441: every text boundary that depends on this machine's code pages takes them from
// Archiver.Core.Text.SystemCodePages. A second place that asks Windows for a code page, or turns
// one into an Encoding by its own rule, is how the encoding bugs of T-F204..T-F424 kept coming back.
// Codecs for a literal page (SystemCodePages.For(866)) and fixed encodings (UTF-8, UTF-16, Latin-1)
// are not code-page selection and are not matched.
public sealed partial class TextBoundaryOwnersTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly string[] Owners =
    [
        Path.Combine("src", "Archiver.Core", "Text", "SystemCodePages.cs"),
        Path.Combine("tests", "Archiver.Core.Tests", "Services", "TextBoundaryOwnersTests.cs"),
    ];

    [Theory]
    [InlineData("src")]
    [InlineData("tests")]
    public void CodePageSelection_HappensOnlyInSystemCodePages(string dir)
    {
        var offenders = new List<string>();
        foreach (string file in SourceFiles(dir))
        {
            string relative = Path.GetRelativePath(RepoRoot, file);
            if (Owners.Contains(relative, StringComparer.OrdinalIgnoreCase))
                continue;

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (CodePageSelection().IsMatch(lines[i]))
                    offenders.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
            }
        }

        offenders.Should().BeEmpty("take the code page from Archiver.Core.Text.SystemCodePages (docs/DECISIONS.md, T-F441)");
    }

    // The tar-name boundary: tar.exe's escaped listing is read back in one place, which also judges it.
    [Fact]
    public void TarListing_IsReadOnlyByTarNames()
    {
        string owner = Path.Combine("src", "Archiver.Core", "Services", "Sandbox", "TarNames.cs");
        SourceFiles("src")
            .Where(file => File.ReadAllText(file).Contains("TarListingName.Unescape(", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(RepoRoot, file))
            .Should().BeEquivalentTo([owner], "a listing's names are read through TarNames.ReadListing, which the verdict judges");
    }

    [Fact]
    public void Scan_SeesTheOwner()
    {
        // Guards the scan itself: if the owner's own P/Invokes stop matching, the test above passes vacuously.
        File.ReadAllLines(Path.Combine(RepoRoot, Owners[0])).Should().Contain(line => CodePageSelection().IsMatch(line));
    }

    private static IEnumerable<string> SourceFiles(string dir) =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot, dir), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    // GetACP/GetOEMCP, the GetLocaleInfoEx code-page constants by name or value, the framework's
    // code-page tables, and the "current page" spellings of Encoding.
    [GeneratedRegex(@"\bGetACP\b|\bGetOEMCP\b|LocaleIDefault(Ansi)?CodePage|0x0*1004\b|CodePagesEncodingProvider|Encoding\.GetEncoding\(|Encoding\.Default\b")]
    private static partial Regex CodePageSelection();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
