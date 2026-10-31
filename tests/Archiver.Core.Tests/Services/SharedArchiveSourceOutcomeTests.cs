using Archiver.Core.IO;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F434: several sources into one archive. A skip keeps only the source it lies in; an error, a
// filter, or a skip that cannot be placed under one source by its full path keeps them all. Before,
// one skipped link in folder B kept a clean folder A out of the Recycle Bin too (APP-027).
public sealed class SharedArchiveSourceOutcomeTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly List<string> _junctions = [];

    public void Dispose()
    {
        // Links first, non-recursively, so the scratch cleanup can never walk through one.
        foreach (string junction in _junctions)
        {
            try { Directory.Delete(junction, recursive: false); } catch (IOException) { /* best-effort: TempDirectory retries */ }
        }
        _temp.Dispose();
    }

    private static SkippedFile Skip(string path) => new() { Path = path };

    private static Dictionary<string, SourceOutcome> Outcomes(IEnumerable<SourceResult> results) =>
        results.ToDictionary(r => r.Path, r => r.Outcome);

    // --- the rule ---

    [Fact]
    public void SkipInOneSource_KeepsOnlyThatSource()
    {
        Outcomes(SourceOutcomeRules.ClassifyShared([@"C:\w\a", @"C:\w\b"], produced: true, errorsOrFilters: false, [Skip(@"C:\w\b\link")]))
            .Should().Equal(new Dictionary<string, SourceOutcome> { [@"C:\w\a"] = SourceOutcome.Completed, [@"C:\w\b"] = SourceOutcome.Partial });
    }

    [Fact]
    public void NoSkip_EverySourceCompleted()
    {
        SourceOutcomeRules.ClassifyShared([@"C:\w\a", @"C:\w\b"], true, false, [])
            .Should().OnlyContain(r => r.Outcome == SourceOutcome.Completed);
    }

    [Fact]
    public void NothingProduced_NoSourceProcessed()
    {
        SourceOutcomeRules.ClassifyShared([@"C:\w\a"], produced: false, false, [])
            .Should().OnlyContain(r => r.Outcome == SourceOutcome.NotProcessed);
    }

    [Fact]
    public void AnErrorOrAFilter_KeepsEverySource()
    {
        SourceOutcomeRules.ClassifyShared([@"C:\w\a", @"C:\w\b"], true, errorsOrFilters: true, [])
            .Should().OnlyContain(r => r.Outcome == SourceOutcome.Partial);
    }

    [Theory]
    [InlineData(@"C:\elsewhere\x")] // under no source
    [InlineData(@"C:\w\ab\x")] // a prefix of a source's name is not that source
    [InlineData(@"b\link")] // relative: placed by no current folder
    [InlineData(@"\\?\C:\w\b\link")] // another spelling: not matched, so not trusted
    [InlineData(@"C:\w\b\..\a\x")] // literally under b, really in a
    [InlineData(@"C:\w\a\.\x")]
    [InlineData("")]
    public void SkipNotPlacedUnderOneSource_KeepsEverySource(string skipPath)
    {
        SourceOutcomeRules.ClassifyShared([@"C:\w\a", @"C:\w\b"], true, false, [Skip(skipPath)])
            .Should().OnlyContain(r => r.Outcome == SourceOutcome.Partial);
    }

    [Fact]
    public void RelativeSkip_WithTheCurrentFolderAsASource_KeepsEverySource()
    {
        string here = Environment.CurrentDirectory;
        string other = Path.Combine(Path.GetTempPath(), "other");

        SourceOutcomeRules.ClassifyShared([here, other], true, false, [Skip(@"sub\link")])
            .Should().OnlyContain(r => r.Outcome == SourceOutcome.Partial, "a relative skip could be the other source's");
    }

    [Fact]
    public void RelativeSource_KeepsEverySource()
    {
        SourceOutcomeRules.ClassifyShared(["a", @"C:\w\b"], true, false, [Skip(@"C:\w\b\link")])
            .Should().OnlyContain(r => r.Outcome == SourceOutcome.Partial);
    }

    [Theory]
    [InlineData(@"C:\W\B\Link")] // case
    [InlineData(@"C:\w\b")] // the source itself (a link given as a source)
    [InlineData(@"C:\w\b\deep\er\x")]
    public void SkipPlacedUnderB_KeepsOnlyB(string skipPath)
    {
        Outcomes(SourceOutcomeRules.ClassifyShared([@"C:\w\a\", @"C:\w\b"], true, false, [Skip(skipPath)]))
            .Should().Equal(new Dictionary<string, SourceOutcome> { [@"C:\w\a\"] = SourceOutcome.Completed, [@"C:\w\b"] = SourceOutcome.Partial });
    }

    // The fresh review: ZIP writes a skip in GetFullPath's form (FileSystemInfo.FullName), so a source
    // spelled otherwise must still hold it - "C:/w/a" with a link at "C:\w\a\lnk" was Completed.
    [Theory]
    [InlineData(@"C:/w/a")]
    [InlineData(@"C:\w\a.")]
    [InlineData(@"C:\w\\a")]
    public void SourceSpelledOtherwise_StillHoldsItsSkip(string spelled)
    {
        Outcomes(SourceOutcomeRules.ClassifyShared([spelled, @"C:\w\c"], true, false, [Skip(@"C:\w\a\lnk")]))
            .Should().Equal(new Dictionary<string, SourceOutcome> { [spelled] = SourceOutcome.Partial, [@"C:\w\c"] = SourceOutcome.Completed });
    }

    [Fact]
    public void NestedSources_ASkipInTheInnerKeepsBoth()
    {
        Outcomes(SourceOutcomeRules.ClassifyShared([@"C:\w\a", @"C:\w\a\sub", @"C:\w\c"], true, false, [Skip(@"C:\w\a\sub\link")]))
            .Should().Equal(new Dictionary<string, SourceOutcome>
            {
                [@"C:\w\a"] = SourceOutcome.Partial, [@"C:\w\a\sub"] = SourceOutcome.Partial, [@"C:\w\c"] = SourceOutcome.Completed,
            });
    }

    [Fact]
    public void DriveRootAsASource_HoldsWhatIsUnderIt()
    {
        Outcomes(SourceOutcomeRules.ClassifyShared([@"D:\", @"C:\w\b"], true, false, [Skip(@"D:\x\link")]))
            .Should().Equal(new Dictionary<string, SourceOutcome> { [@"D:\"] = SourceOutcome.Partial, [@"C:\w\b"] = SourceOutcome.Completed });
    }

    // --- the engines, every ZIP writer and tar's own in the integration tests ---

    public static TheoryData<string> Writers => ["sequential", "parallel", "encrypted"];

    [Theory]
    [MemberData(nameof(Writers))]
    public async Task Zip_SingleArchive_LinkSkippedInB_AIsDeletable(string writer)
    {
        string a = Folder("a", writer == "parallel" ? 70 : 2);
        string b = Folder("b", 2);
        string target = Folder("target", 1);
        DirectoryJunction.Create(Path.Combine(b, "link"), target);
        _junctions.Add(Path.Combine(b, "link"));
        var sut = new ZipArchiveService(new GroupPolicyOptions()) { HasNoSeekPenalty = _ => writer == "parallel" };

        ArchiveResult result = await sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [a, b],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
            ArchiveName = "all",
            Mode = ArchiveMode.SingleArchive,
            ResolvePasswordAsync = writer == "encrypted"
                ? _ => Task.FromResult(new PasswordDecision { Password = "correct horse" })
                : null,
        });

        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().ContainSingle();
        result.FullyProcessedSources.Should().Equal(a);
    }

    [Fact]
    public async Task Zip_SingleArchive_LinkSkippedInB_AndAnErrorInA_NoSourceDeletable()
    {
        string a = Folder("a", 2);
        string b = Folder("b", 2);
        string target = Folder("target", 1);
        DirectoryJunction.Create(Path.Combine(b, "link"), target);
        _junctions.Add(Path.Combine(b, "link"));
        string locked = Path.Combine(a, "locked.bin");
        File.WriteAllText(locked, "cannot be read");
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        ArchiveResult result = await new ZipArchiveService(new GroupPolicyOptions()).ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [a, b],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
            ArchiveName = "all",
            Mode = ArchiveMode.SingleArchive,
        });

        result.FullyProcessedSources.Should().BeEmpty();
    }

    private string Folder(string name, int files)
    {
        string path = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(path);
        for (int i = 0; i < files; i++)
            File.WriteAllText(Path.Combine(path, $"f{i:D3}.txt"), new string('x', 512));
        return path;
    }
}
