using System.IO.Compression;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F260/T-F229/T-F245/T-F265: ArchiveResult.Sources is the only input to "Delete after
// operation". A source may be deleted only when it is Completed; cancellation throws instead of
// returning a result that looks finished.
public sealed class SourceOutcomeTests : IDisposable
{
    private sealed class SynchronousProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string WriteZip(string name, params string[] entryNames)
    {
        string path = Path.Combine(_temp.Path, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (string entryName in entryNames)
        {
            using var stream = archive.CreateEntry(entryName).Open();
            stream.Write("data"u8);
        }
        return path;
    }

    private string Dest(string name) => Path.Combine(_temp.Path, name);

    // --- Extraction ---

    [Fact]
    public async Task ExtractAsync_CleanZip_SourceCompleted()
    {
        string zip = WriteZip("clean.zip", "a.txt", "b.txt");

        var result = await _sut.ExtractAsync(new ExtractOptions { ArchivePaths = [zip], DestinationFolder = Dest("out") });

        result.Sources.Should().ContainSingle().Which.Should().Be(new SourceResult { Path = zip, Outcome = SourceOutcome.Completed });
        result.FullyProcessedSources.Should().Equal(zip);
    }

    [Fact]
    public async Task ExtractAsync_ReservedNameEntrySkipped_SourcePartialNotDeletable()
    {
        string zip = WriteZip("reserved.zip", "ok.txt", "CON.txt");

        var result = await _sut.ExtractAsync(new ExtractOptions { ArchivePaths = [zip], DestinationFolder = Dest("out") });

        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Partial);
        result.FullyProcessedSources.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_EveryEntryConflictSkipped_SourceNotProcessed()
    {
        string zip = WriteZip("twice.zip", "a.txt", "b.txt");
        var options = new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = Dest("out"),
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Skip,
        };
        await _sut.ExtractAsync(options);

        var result = await _sut.ExtractAsync(options);

        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.NotProcessed);
        result.FullyProcessedSources.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_SelectedEntriesOnly_SourcePartialNotDeletable()
    {
        string zip = WriteZip("subset.zip", "a.txt", "b.txt");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = Dest("out"),
            SelectedEntryPaths = ["a.txt"],
        });

        result.Errors.Should().BeEmpty();
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Partial);
        result.FullyProcessedSources.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_CorruptZip_SourceNotProcessed()
    {
        string zip = Path.Combine(_temp.Path, "corrupt.zip");
        File.WriteAllBytes(zip, [0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5, 6, 7, 8]);

        var result = await _sut.ExtractAsync(new ExtractOptions { ArchivePaths = [zip], DestinationFolder = Dest("out") });

        result.FullyProcessedSources.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_OneGoodOneCorrupt_OnlyGoodCompleted()
    {
        string good = WriteZip("good.zip", "a.txt");
        string corrupt = Path.Combine(_temp.Path, "corrupt.zip");
        File.WriteAllBytes(corrupt, [0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4]);

        var result = await _sut.ExtractAsync(new ExtractOptions { ArchivePaths = [good, corrupt], DestinationFolder = Dest("out") });

        result.FullyProcessedSources.Should().Equal(good);
    }

    [Fact]
    public async Task ExtractAsync_CancelledBetweenArchives_Throws()
    {
        string first = WriteZip("first.zip", "a.txt");
        string second = WriteZip("second.zip", "b.txt");
        using var cts = new CancellationTokenSource();
        var progress = new SynchronousProgress<ProgressReport>(r => { if (r.Percent >= 50) cts.Cancel(); });

        var act = () => _sut.ExtractAsync(
            new ExtractOptions { ArchivePaths = [first, second], DestinationFolder = Dest("out") }, progress, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExtractAsync_AlreadyCancelledToken_Throws()
    {
        string zip = WriteZip("a.zip", "a.txt");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _sut.ExtractAsync(new ExtractOptions { ArchivePaths = [zip], DestinationFolder = Dest("out") }, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // --- Creation ---

    [Fact]
    public async Task ArchiveAsync_SeparateArchives_EverySourceCompleted()
    {
        var files = new[] { _temp.CreateFile("a.txt"), _temp.CreateFile("b.txt"), _temp.CreateFile("c.txt") };

        var result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = files,
            DestinationFolder = Dest("out"),
            Mode = ArchiveMode.SeparateArchives,
        });

        result.FullyProcessedSources.Should().BeEquivalentTo(files);
    }

    [Fact]
    public async Task ArchiveAsync_SeparateArchives_MissingSource_OnlyThatOneNotCompleted()
    {
        string a = _temp.CreateFile("a.txt");
        string missing = Path.Combine(_temp.Path, "missing.txt");

        var result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [a, missing],
            DestinationFolder = Dest("out"),
            Mode = ArchiveMode.SeparateArchives,
        });

        result.FullyProcessedSources.Should().Equal(a);
    }

    [Fact]
    public async Task ArchiveAsync_SeparateArchives_TwentySourcesOneLocked_ExactlyOneNotCompleted()
    {
        var files = Enumerable.Range(1, 20).Select(i => _temp.CreateFile($"f{i:D2}.txt")).ToList();
        using var locked = new FileStream(files[7], FileMode.Open, FileAccess.Read, FileShare.None);

        var result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = files,
            DestinationFolder = Dest("out"),
            Mode = ArchiveMode.SeparateArchives,
        });

        result.FullyProcessedSources.Should().BeEquivalentTo(files.Where((_, i) => i != 7));
    }

    [Fact]
    public async Task ArchiveAsync_SeparateArchives_FolderWithOneLockedFile_ThatFolderPartialOthersCompleted()
    {
        // The archive for the folder is still committed (its other files made it in), so only the
        // worker's own issue count can mark it Partial — the shared bags hold every worker's issues.
        var dirs = Enumerable.Range(1, 4).Select(i =>
        {
            string dir = Path.Combine(_temp.Path, $"d{i}");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "a.txt"), "a");
            File.WriteAllText(Path.Combine(dir, "b.txt"), "b");
            return dir;
        }).ToList();
        using var locked = new FileStream(Path.Combine(dirs[2], "b.txt"), FileMode.Open, FileAccess.Read, FileShare.None);

        var result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = dirs,
            DestinationFolder = Dest("out"),
            Mode = ArchiveMode.SeparateArchives,
        });

        result.CreatedFiles.Should().HaveCount(4);
        result.Sources.Single(s => s.Path == dirs[2]).Outcome.Should().Be(SourceOutcome.Partial);
        result.FullyProcessedSources.Should().BeEquivalentTo(dirs.Where((_, i) => i != 2));
    }

    [Theory]
    [InlineData(@"C:\src\dir", @"C:\src\dir\out\dir.zip", true)]
    [InlineData(@"C:\src\dir", @"C:\src\DIR\dir.zip", true)]
    [InlineData(@"C:\src\dir", @"C:\src\dir2\dir.zip", false)]
    [InlineData(@"C:\src\dir", @"C:\src\dir.zip", false)]
    [InlineData(@"C:\", @"C:\archive.zip", true)]
    public void DowngradeSourcesContainingOutputs_OnlySourcesContainingAnOutput(string source, string created, bool downgraded)
    {
        var sources = new[] { new SourceResult { Path = source, Outcome = SourceOutcome.Completed } };

        var result = SourceOutcomeRules.DowngradeSourcesContainingOutputs(sources, [created]);

        result.Single().Outcome.Should().Be(downgraded ? SourceOutcome.Partial : SourceOutcome.Completed);
    }

    [Fact]
    public async Task ArchiveAsync_SingleArchive_Clean_EverySourceCompleted()
    {
        string a = _temp.CreateFile("a.txt");
        string dir = Path.Combine(_temp.Path, "dir");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "inner.txt"), "x");

        var result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [a, dir],
            DestinationFolder = Dest("out"),
            ArchiveName = "all",
        });

        result.FullyProcessedSources.Should().BeEquivalentTo([a, dir]);
    }

    [Fact]
    public async Task ArchiveAsync_SingleArchive_OneSourceMissing_NoSourceDeletable()
    {
        string a = _temp.CreateFile("a.txt");

        var result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [a, Path.Combine(_temp.Path, "missing.txt")],
            DestinationFolder = Dest("out"),
            ArchiveName = "all",
        });

        result.CreatedFiles.Should().HaveCount(1);
        result.FullyProcessedSources.Should().BeEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_SourceWithTrailingSeparator_RecordedTrimmed()
    {
        string dir = Path.Combine(_temp.Path, "dir");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "inner.txt"), "x");

        var result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [dir + Path.DirectorySeparatorChar],
            DestinationFolder = Dest("out"),
            Mode = ArchiveMode.SeparateArchives,
        });

        result.FullyProcessedSources.Should().Equal(dir);
    }

    [Fact]
    public async Task ArchiveAsync_DestinationInsideSource_SourceNotDeletable()
    {
        string dir = Path.Combine(_temp.Path, "dir");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "inner.txt"), "x");

        var result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [dir],
            DestinationFolder = Path.Combine(dir, "archives"),
            Mode = ArchiveMode.SeparateArchives,
        });

        result.CreatedFiles.Should().ContainSingle();
        result.FullyProcessedSources.Should().BeEmpty();
    }

    [Fact]
    public async Task ArchiveAsync_SingleArchiveSkippedAsExisting_NoSourceDeletable()
    {
        string a = _temp.CreateFile("a.txt");
        var options = new ArchiveOptions
        {
            SourcePaths = [a],
            DestinationFolder = Dest("out"),
            ArchiveName = "one",
            OnConflict = ConflictBehavior.Skip,
        };
        await _sut.ArchiveAsync(options);

        var result = await _sut.ArchiveAsync(options);

        result.FullyProcessedSources.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ArchiveMode.SeparateArchives)]
    [InlineData(ArchiveMode.SingleArchive)]
    public async Task ArchiveAsync_AlreadyCancelledToken_Throws(ArchiveMode mode)
    {
        string a = _temp.CreateFile("a.txt");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [a],
            DestinationFolder = Dest("out"),
            ArchiveName = "one",
            Mode = mode,
        }, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // --- Router ---

    [Fact]
    public async Task Router_MergesSourcesFromBothEngines_UnsupportedNeverDeletable()
    {
        string zip = WriteZip("a.zip", "x.txt");
        string tar = Path.Combine(_temp.Path, "b.tar");
        var header = new byte[512];
        "ustar"u8.CopyTo(header.AsSpan(257));
        File.WriteAllBytes(tar, header);
        string rar = Path.Combine(_temp.Path, "c.rar");
        File.WriteAllBytes(rar, [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00]);

        var tarService = new SourcesTarService(new ArchiveResult
        {
            Success = true,
            Sources = [new SourceResult { Path = tar, Outcome = SourceOutcome.Completed }],
        });
        var router = new ExtractionRouter(_sut, tarService, new TarCapabilities());

        var result = await router.ExtractAsync(new ExtractOptions { ArchivePaths = [zip, tar, rar], DestinationFolder = Dest("out") });

        result.FullyProcessedSources.Should().BeEquivalentTo([zip, tar]);
    }

    [Fact]
    public async Task Router_TarEngineCancelled_PropagatesAfterZipPartRan()
    {
        string zip = WriteZip("a.zip", "x.txt");
        string tar = Path.Combine(_temp.Path, "b.tar");
        var header = new byte[512];
        "ustar"u8.CopyTo(header.AsSpan(257));
        File.WriteAllBytes(tar, header);
        var router = new ExtractionRouter(_sut, new SourcesTarService(null), new TarCapabilities());

        var act = () => router.ExtractAsync(new ExtractOptions { ArchivePaths = [zip, tar], DestinationFolder = Dest("out") });

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

// Returns the given result, or throws OperationCanceledException when given null.
file sealed class SourcesTarService(ArchiveResult? result) : ITarService
{
    public Task<TarCapabilities> DetectCapabilitiesAsync() => Task.FromResult(new TarCapabilities());

    public Task<ArchiveResult> ExtractAsync(ExtractOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => result is null ? throw new OperationCanceledException() : Task.FromResult(result);

    public Task<ArchiveListResult> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ArchiveResult> CompressAsync(ArchiveOptions options, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
