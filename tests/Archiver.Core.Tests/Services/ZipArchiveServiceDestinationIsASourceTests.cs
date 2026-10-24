using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F421: an archive's name can be a source of the run - in separate-archive mode "a.txt" and
/// "a.zip" both map to "a.zip", and "a.zip" alone maps to itself; in single mode an archive named
/// after a source. A file that is a source of the run is never replaced: the name counts as a
/// same-run collision (T-F12/T-F158), numbered under Overwrite and Rename, skipped under Skip.
/// </summary>
public sealed class ZipArchiveServiceDestinationIsASourceTests : IDisposable
{
    private static readonly byte[] Hello = [.. "hello"u8];
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string CreateSourceZip(string name)
    {
        string path = Path.Combine(_temp.Path, name);
        using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using StreamWriter writer = new(zip.CreateEntry("inner.txt").Open());
            writer.Write("inner content");
        }
        return path;
    }

    private string CreateTextFile(string name)
    {
        string path = Path.Combine(_temp.Path, name);
        File.WriteAllBytes(path, Hello);
        return path;
    }

    private ArchiveOptions Options(ConflictBehavior onConflict, params string[] sources) => new()
    {
        SourcePaths = sources,
        DestinationFolder = _temp.Path,
        Mode = ArchiveMode.SeparateArchives,
        OnConflict = onConflict,
        ResolveConflictAsync = onConflict == ConflictBehavior.Ask
            ? _ => Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Overwrite })
            : null,
    };

    private static Dictionary<string, byte[]> Entries(string archivePath)
    {
        using ZipArchive zip = ZipFile.OpenRead(archivePath);
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            using Stream s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            entries[entry.FullName] = ms.ToArray();
        }
        return entries;
    }

    // Ask is answered Overwrite: the answer that would replace the source.
    public static TheoryData<ConflictBehavior> ChoicesThatWrite() =>
        [ConflictBehavior.Overwrite, ConflictBehavior.Rename, ConflictBehavior.Ask];

    // Optimal runs the sequential writer, Fastest the hand-rolled one (T-F299).
    public static TheoryData<ConflictBehavior, CompressionLevel> ChoicesThatWriteByWriter()
    {
        var data = new TheoryData<ConflictBehavior, CompressionLevel>();
        foreach (ConflictBehavior choice in (ConflictBehavior[])[ConflictBehavior.Overwrite, ConflictBehavior.Rename, ConflictBehavior.Ask])
        {
            data.Add(choice, CompressionLevel.Optimal);
            data.Add(choice, CompressionLevel.Fastest);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ChoicesThatWriteByWriter))]
    public async Task ArchiveAsync_TwoSourcesMapToOneName_NeitherSourceIsReplacedAndEachGetsItsOwnArchive(ConflictBehavior onConflict, CompressionLevel level)
    {
        string txt = CreateTextFile("a.txt");
        string zip = CreateSourceZip("a.zip");
        byte[] zipBytes = File.ReadAllBytes(zip);

        ArchiveResult result = await _sut.ArchiveAsync(Options(onConflict, txt, zip) with { CompressionLevel = level });

        File.ReadAllBytes(zip).Should().Equal(zipBytes, "a source of the run is never replaced");
        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        result.CreatedFiles.Should().BeEquivalentTo([Path.Combine(_temp.Path, "a (1).zip"), Path.Combine(_temp.Path, "a (2).zip")]);
        List<Dictionary<string, byte[]>> archives = [.. result.CreatedFiles.Select(Entries)];
        archives.Should().ContainSingle(e => e.Count == 1 && e.ContainsKey("a.txt") && e["a.txt"].SequenceEqual(Hello));
        archives.Should().ContainSingle(e => e.Count == 1 && e.ContainsKey("a.zip") && e["a.zip"].SequenceEqual(zipBytes));
        result.Sources.Should().HaveCount(2).And.OnlyContain(s => s.Outcome == SourceOutcome.Completed);
    }

    [Fact]
    public async Task ArchiveAsync_TwoSourcesMapToOneName_Skip_SkipsBothAndKeepsTheSource()
    {
        string txt = CreateTextFile("a.txt");
        string zip = CreateSourceZip("a.zip");
        byte[] zipBytes = File.ReadAllBytes(zip);

        ArchiveResult result = await _sut.ArchiveAsync(Options(ConflictBehavior.Skip, txt, zip));

        File.ReadAllBytes(zip).Should().Equal(zipBytes);
        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Select(s => s.Path).Should().BeEquivalentTo([txt, zip]);
        result.Sources.Should().NotContain(s => s.Outcome == SourceOutcome.Completed);
    }

    [Theory]
    [MemberData(nameof(ChoicesThatWrite))]
    public async Task ArchiveAsync_SourceMapsToItsOwnName_SourceIsKeptAndTheArchiveIsNumbered(ConflictBehavior onConflict)
    {
        string zip = CreateSourceZip("a.zip");
        byte[] zipBytes = File.ReadAllBytes(zip);

        ArchiveResult result = await _sut.ArchiveAsync(Options(onConflict, zip));

        File.ReadAllBytes(zip).Should().Equal(zipBytes, "the source itself is never replaced");
        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().Equal(Path.Combine(_temp.Path, "a (1).zip"));
        Entries(result.CreatedFiles[0])["a.zip"].Should().Equal(zipBytes);
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Completed);
    }

    [Fact]
    public async Task ArchiveAsync_SourceSpelledWithTheLongPathPrefix_IsStillKnownAsTheArchivesName()
    {
        string zip = CreateSourceZip("a.zip");
        byte[] zipBytes = File.ReadAllBytes(zip);

        ArchiveResult result = await _sut.ArchiveAsync(Options(ConflictBehavior.Overwrite, @"\\?\" + zip));

        File.ReadAllBytes(zip).Should().Equal(zipBytes, "the source is the same file by another spelling");
        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().Equal(Path.Combine(_temp.Path, "a (1).zip"));
    }

    [Fact]
    public async Task ArchiveAsync_FilterLeavesTheSameNamedSourceOut_ItIsStillNeverReplaced()
    {
        string txt = CreateTextFile("a.txt");
        string zip = CreateSourceZip("a.zip");
        byte[] zipBytes = File.ReadAllBytes(zip);

        ArchiveResult result = await _sut.ArchiveAsync(Options(ConflictBehavior.Overwrite, txt, zip) with
        {
            EntryFilter = (name, _) => !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase),
        });

        File.ReadAllBytes(zip).Should().Equal(zipBytes, "a source the filter leaves out is still the user's file");
        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().Equal(Path.Combine(_temp.Path, "a (1).zip"));
        Entries(result.CreatedFiles[0]).Keys.Should().Equal("a.txt");
    }

    [Theory]
    [MemberData(nameof(ChoicesThatWrite))]
    public async Task ArchiveAsync_SingleArchiveNamedAfterASource_SourceIsKeptAndTheArchiveIsNumbered(ConflictBehavior onConflict)
    {
        string txt = CreateTextFile("a.txt");
        string zip = CreateSourceZip("a.zip");
        byte[] zipBytes = File.ReadAllBytes(zip);

        ArchiveResult result = await _sut.ArchiveAsync(Options(onConflict, txt, zip) with { Mode = ArchiveMode.SingleArchive, ArchiveName = "a" });

        File.ReadAllBytes(zip).Should().Equal(zipBytes, "a source of the run is never replaced");
        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().Equal(Path.Combine(_temp.Path, "a (1).zip"));
        Entries(result.CreatedFiles[0]).Keys.Should().BeEquivalentTo(["a.txt", "a.zip"]);
    }

    [Fact]
    public async Task ArchiveAsync_SingleArchiveNamedAfterASource_Skip_SkipsAndKeepsTheSource()
    {
        string txt = CreateTextFile("a.txt");
        string zip = CreateSourceZip("a.zip");
        byte[] zipBytes = File.ReadAllBytes(zip);

        ArchiveResult result = await _sut.ArchiveAsync(Options(ConflictBehavior.Skip, txt, zip) with { Mode = ArchiveMode.SingleArchive, ArchiveName = "a" });

        File.ReadAllBytes(zip).Should().Equal(zipBytes);
        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(ArchiveMode.SeparateArchives)]
    [InlineData(ArchiveMode.SingleArchive)]
    public async Task ArchiveAsync_AnArchiveOfTheSameNameThatIsNoSource_OverwriteStillReplacesIt(ArchiveMode mode)
    {
        string txt = CreateTextFile("a.txt");
        string old = CreateSourceZip("a.zip");

        ArchiveResult result = await _sut.ArchiveAsync(Options(ConflictBehavior.Overwrite, txt) with { Mode = mode, ArchiveName = "a" });

        result.CreatedFiles.Should().Equal(old);
        Entries(old).Keys.Should().Equal("a.txt");
    }
}
