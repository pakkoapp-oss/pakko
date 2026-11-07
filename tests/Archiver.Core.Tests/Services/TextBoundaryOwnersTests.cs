using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F441/T-F467: every text boundary takes its page from a handler of Archiver.Core.Text.TextBoundaries,
// the one provider; only the provider reads SystemCodePages. A second place that asks Windows for a
// code page, or turns one into an Encoding by its own rule, is how the encoding bugs of
// T-F204..T-F466 kept coming back. Codecs for a literal page (new TextBoundary(866)) and fixed
// encodings inside a file format (ZIP's UTF-8 flag, Latin-1) are not page selection and are not matched.
public sealed partial class TextBoundaryOwnersTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    // The provider and the tests of the provider itself.
    private static readonly string[] ProviderFolders =
    [
        Path.Combine("src", "Archiver.Core", "Text") + Path.DirectorySeparatorChar,
        Path.Combine("tests", "Archiver.Core.Tests", "Text") + Path.DirectorySeparatorChar,
    ];

    private static readonly string[] Owners =
    [
        Path.Combine("src", "Archiver.Core", "Text", "SystemCodePages.cs"),
        Path.Combine("tests", "Archiver.Core.Tests", "Services", "TextBoundaryOwnersTests.cs"),
    ];

    // The boundaries with a fixed encoding of their own: they take it from their handler.
    private static readonly string[] FixedBoundaryFiles =
    [
        Path.Combine("src", "Archiver.Core", "Recovery", "Par2Creator.cs"),
        Path.Combine("src", "Archiver.Core", "Recovery", "Par2SetLocator.cs"),
        Path.Combine("src", "Archiver.Core", "Services", "LaunchArguments.cs"),
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

        offenders.Should().BeEmpty("take the code page from a handler of Archiver.Core.Text.TextBoundaries (docs/DECISIONS.md, T-F441, T-F467)");
    }

    // T-F467: the machine's pages reach a consumer only through a handler; a test takes a literal
    // page through TextBoundary and the machine's through MachinePages, as the product does.
    [Theory]
    [InlineData("src")]
    [InlineData("tests")]
    public void SystemCodePages_IsReadOnlyByTheProvider(string dir)
    {
        SourceFiles(dir)
            .Select(file => Path.GetRelativePath(RepoRoot, file))
            .Where(relative => !ProviderFolders.Any(folder => relative.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))
            .Where(relative => !Owners.Contains(relative, StringComparer.OrdinalIgnoreCase))
            .Where(relative => File.ReadAllText(Path.Combine(RepoRoot, relative)).Contains("SystemCodePages.", StringComparison.Ordinal))
            .Should().BeEmpty("a page reaches a boundary through a handler of TextBoundaries (docs/DECISIONS.md, T-F467)");
    }

    [Fact]
    public void FixedBoundaries_TakeTheirHandler()
    {
        FixedBoundaryFiles
            .Where(relative => FixedEncoding().IsMatch(File.ReadAllText(Path.Combine(RepoRoot, relative))))
            .Should().BeEmpty("PAR2 names and the launch payload take TextBoundaries.Par2FileNames/LaunchArgumentPayload (T-F467)");
        FixedBoundaryFiles.Should().AllSatisfy(relative =>
            File.ReadAllText(Path.Combine(RepoRoot, relative)).Should().Contain("TextBoundaries."));
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
    [GeneratedRegex(@"\bGetACP\b|\bGetOEMCP\b|LocaleIDefault(Ansi)?CodePage|0x0*1004\b|LocaleSEnglish(Language|Country)Name|CodePagesEncodingProvider|Encoding\.GetEncoding\(|Encoding\.Default\b")]
    private static partial Regex CodePageSelection();

    [GeneratedRegex(@"Encoding\.(UTF8|Unicode|ASCII)\b|new UTF8Encoding")]
    private static partial Regex FixedEncoding();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
