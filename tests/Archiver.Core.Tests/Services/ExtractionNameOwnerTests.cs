using System.Text.RegularExpressions;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F570: where an extracted file, folder or kept damaged file goes is ExtractionNameRegistry's to
// decide (PlaceAsync, PlaceKept, ClaimFolder); no engine picks a free name or claims a path itself.
// T-F443, T-F453, T-F496 and T-F569 were each one engine or one kind of item deciding alone.
public sealed partial class ExtractionNameOwnerTests
{
    private static readonly string SrcRoot = Path.Combine(FindRepoRoot(), "src");

    private const string Owner = "Archiver.Core/Services/ExtractionNameRegistry.cs";
    private const string Naming = "Archiver.Core/Services/ArchiveNaming.cs";

    // Each with why; a new one fails here.
    private static readonly (string File, int Count, string Why)[] Allowed =
    [
        ("Archiver.Core/Services/ArchiveTempFile.cs", 1, "an archive Pakko creates, not an extracted name"),
        ("Archiver.Core/Services/RecoveryService.cs", 2, "a repaired archive's output, not an extracted name"),
        ("Archiver.Core/Services/TarSandboxedService.cs", 2, "the archive tar creates, not an extracted name"),
        ("Archiver.Core/Services/ZipArchiveService.cs", 2, "the archive ZIP creates, not an extracted name"),
        ("Archiver.Core/Services/ExtractionStaging.cs", 1,
            "-aot's name for the file moved aside at the commit, past every path the registry holds (IsTaken)"),
    ];

    [Fact]
    public void ExtractedNames_ArePlacedOnlyByTheRegistry()
    {
        var found = new Dictionary<string, int>();
        foreach (string file in Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SrcRoot, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => f != Owner && f != Naming && !f.Contains("/obj/", StringComparison.Ordinal) && !f.Contains("/bin/", StringComparison.Ordinal)))
        {
            string code = string.Join('\n', File.ReadAllLines(Path.Combine(SrcRoot, file))
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            int count = NamingCall().Count(code);
            if (count > 0)
                found[file] = count;
        }
        Dictionary<string, int> allowed = Allowed.ToDictionary(a => a.File, a => a.Count);

        found.Where(f => f.Value > allowed.GetValueOrDefault(f.Key)).Select(f => $"{f.Key} x{f.Value}")
            .Should().BeEmpty("an extracted name is placed by ExtractionNameRegistry (T-F570)");
        allowed.Where(a => a.Value > found.GetValueOrDefault(a.Key)).Select(a => $"{a.Key} x{a.Value}")
            .Should().BeEmpty("a listed use that is gone leaves room for a new one");
    }

    // T-F575: a reader that made a folder entry itself would make one the registry never claimed.
    [Theory]
    [InlineData("Archiver.Core/Services/Sandbox/HelperReader.cs")]
    [InlineData("Archiver.Core/Services/Sandbox/TarExeReader.cs")]
    public void SandboxReaders_CreateNoFolder_TheEngineClaimsAndCreatesThem(string file)
    {
        string code = File.ReadAllText(Path.Combine(SrcRoot, file));

        code.Should().NotContain("Directory.CreateDirectory(", "folder entries are claimed and created by TarSandboxedService (T-F575)");
        code.Should().Contain("FolderEntryPaths(");
    }

    // Picking a free name, or claiming a path, directly.
    [GeneratedRegex(@"\bGetUnique(FilePath|Name)\(|\.Claim\(")]
    private static partial Regex NamingCall();

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "windows-archiver-wrapper.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Repository root not found.");
    }
}
