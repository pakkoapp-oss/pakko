using System.Text.RegularExpressions;
using System.Xml.Linq;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F264: the recognized-extension lists are hand-kept in C# (ArchiveFormatDetector, ArchiveNaming),
// C++ (ShellExtUtils.cpp - extension-only by design, T-F86/T-F131) and Package.appxmanifest. These
// tests read the C++ source and the manifest from the repo and compare them with the C# lists, so a
// new extension added to one list and forgotten in another fails here.
public sealed partial class FormatListConsistencyTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string ShellExtUtilsSource =>
        File.ReadAllText(Path.Combine(RepoRoot, "src", "Archiver.ShellExtension", "ShellExtUtils.cpp"));

    [Fact]
    public void ShellExtensionArchiveExtensions_MatchTheDetectorsList()
    {
        string source = ShellExtUtilsSource;
        IEnumerable<string> native = CppArray(source, "kZipContainerExtensions")
            .Concat(CppArray(source, "kSupportedNonZipArchiveExtensions"));

        native.Should().BeEquivalentTo(ArchiveFormatDetector.RecognizedExtensions);
    }

    [Fact]
    public void ShellExtensionZipContainers_AreExactlyTheDetectorsZipGroup()
    {
        CppArray(ShellExtUtilsSource, "kZipContainerExtensions")
            .Should().BeEquivalentTo(ArchiveFormatDetector.RecognizedZipExtensions);
    }

    [Fact]
    public void ShellExtensionCompoundExtensions_MatchArchiveNaming()
    {
        CppArray(ShellExtUtilsSource, "kCompoundArchiveExtensions")
            .Should().BeEquivalentTo(ArchiveNaming.CompoundExtensionList);
    }

    [Fact]
    public void ShellExtensionPolicyFormatMap_CoversEveryNonZipExtension()
    {
        // T-F262's GetFormatRegistryName table: { L".tar", L"tar" }, ...
        string source = ShellExtUtilsSource;
        string table = NamesTable().Match(source).Groups["body"].Value;
        IEnumerable<string> mapped = NamesTableEntry().Matches(table).Select(m => m.Groups["ext"].Value);

        mapped.Should().BeEquivalentTo(CppArray(source, "kSupportedNonZipArchiveExtensions"));
    }

    [Fact]
    public void ManifestFileTypeAssociations_MatchTheDetectorsList()
    {
        var manifest = XDocument.Load(Path.Combine(RepoRoot, "src", "Archiver.App", "Package.appxmanifest"));
        IEnumerable<string> fileTypes = manifest.Descendants()
            .Where(e => e.Name.LocalName == "FileType")
            .Select(e => e.Value.Trim());

        fileTypes.Should().BeEquivalentTo(ArchiveFormatDetector.RecognizedExtensions);
    }

    [Fact]
    public void CppArrayReader_FindsAKnownArray()
    {
        // Guards the tests above against passing vacuously on an empty parse.
        CppArray(ShellExtUtilsSource, "kZipContainerExtensions").Should().Contain(".zip");
    }

    private static List<string> CppArray(string source, string name)
    {
        Match array = Regex.Match(source, Regex.Escape(name) + @"\[\]\s*=\s*\{(?<body>[^}]*)\}");
        array.Success.Should().BeTrue($"ShellExtUtils.cpp should still define {name}");
        return [.. WideStringLiteral().Matches(array.Groups["body"].Value).Select(m => m.Groups["ext"].Value)];
    }

    [GeneratedRegex(@"kNames\[\]\s*=\s*\{(?<body>.*?)\};", RegexOptions.Singleline)]
    private static partial Regex NamesTable();

    [GeneratedRegex(@"\{\s*L""(?<ext>\.[^""]+)""")]
    private static partial Regex NamesTableEntry();

    [GeneratedRegex(@"L""(?<ext>[^""]+)""")]
    private static partial Regex WideStringLiteral();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "windows-archiver-wrapper.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
