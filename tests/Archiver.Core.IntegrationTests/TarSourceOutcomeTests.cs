using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

// T-F260/T-F245/T-F265: tar-family per-source outcomes and the cancellation contract — a cancelled
// tar operation throws OperationCanceledException instead of returning a finished-looking result.
[Collection("TarSandbox")]
public sealed class TarSourceOutcomeTests : IDisposable
{
    private sealed class SynchronousProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    private readonly TarSandboxedService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string WriteTar(string name, params string[] entryNames)
    {
        string path = Path.Combine(_temp.Path, name);
        TarBuilder.WriteTar(path, [.. entryNames.Select(n => new TarBuilder.Entry { Name = n, Content = Encoding.ASCII.GetBytes("data") })]);
        return path;
    }

    private string Dest(string name) => Path.Combine(_temp.Path, name);

    [Integration]
    public async Task ExtractAsync_CleanTar_SourceCompleted()
    {
        string tar = WriteTar("clean.tar", "a.txt", "b.txt");

        var result = await _sut.ExtractAsync(new ExtractOptions { ArchivePaths = [tar], DestinationFolder = Dest("out") });

        result.Sources.Should().ContainSingle().Which.Should().Be(new SourceResult { Path = tar, Outcome = SourceOutcome.Completed });
    }

    [Integration]
    public async Task ExtractAsync_SelectedEntriesOnly_SourcePartial()
    {
        string tar = WriteTar("subset.tar", "a.txt", "b.txt");

        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [tar],
            DestinationFolder = Dest("out"),
            SelectedEntryPaths = ["a.txt"],
        });

        result.Errors.Should().BeEmpty();
        result.FullyProcessedSources.Should().BeEmpty();
    }

    [Integration]
    public async Task ExtractAsync_CancelledBetweenArchives_Throws()
    {
        string first = WriteTar("first.tar", "a.txt");
        string second = WriteTar("second.tar", "b.txt");
        using var cts = new CancellationTokenSource();
        var progress = new SynchronousProgress<ProgressReport>(r => { if (r.Percent >= 50) cts.Cancel(); });

        var act = () => _sut.ExtractAsync(
            new ExtractOptions { ArchivePaths = [first, second], DestinationFolder = Dest("out") }, progress, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Integration]
    public async Task ExtractAsync_CancelledDuringArchive_Throws()
    {
        // The conflict callback runs inside the per-archive extraction — cancelling there is a
        // deterministic "mid-archive" cancel (T-F245's cause: ExtractOneArchiveAsync swallowed it).
        string tar = WriteTar("mid.tar", "a.txt", "b.txt");
        string dest = Dest("out");
        Directory.CreateDirectory(Path.Combine(dest, "mid"));
        File.WriteAllText(Path.Combine(dest, "mid", "a.txt"), "existing");
        using var cts = new CancellationTokenSource();

        var act = () => _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [tar],
            DestinationFolder = dest,
            OnConflict = ConflictBehavior.Ask,
            ResolveConflictAsync = _ =>
            {
                cts.Cancel();
                return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Skip });
            },
        }, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Integration]
    public async Task ExtractAsync_AlreadyCancelledToken_Throws()
    {
        string tar = WriteTar("a.tar", "a.txt");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _sut.ExtractAsync(new ExtractOptions { ArchivePaths = [tar], DestinationFolder = Dest("out") }, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Integration]
    public async Task CompressAsync_SeparateArchives_EverySourceCompleted()
    {
        string a = Path.Combine(_temp.Path, "a.txt");
        string b = Path.Combine(_temp.Path, "b.txt");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");

        var result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [a, b],
            DestinationFolder = Dest("out"),
            Mode = ArchiveMode.SeparateArchives,
            Format = ArchiveContainerFormat.Tar,
        });

        result.FullyProcessedSources.Should().BeEquivalentTo([a, b]);
    }

    [Integration]
    public async Task CompressAsync_SingleArchive_EverySourceCompleted()
    {
        string a = Path.Combine(_temp.Path, "a.txt");
        File.WriteAllText(a, "a");

        var result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [a],
            DestinationFolder = Dest("out"),
            ArchiveName = "one",
            Format = ArchiveContainerFormat.Tar,
        });

        result.FullyProcessedSources.Should().Equal(a);
    }

    [Integration]
    public async Task CompressAsync_SeparateArchives_MissingSource_OnlyThatOneNotCompleted()
    {
        string a = Path.Combine(_temp.Path, "a.txt");
        File.WriteAllText(a, "a");

        var result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [a, Path.Combine(_temp.Path, "missing.txt")],
            DestinationFolder = Dest("out"),
            Mode = ArchiveMode.SeparateArchives,
            Format = ArchiveContainerFormat.Tar,
        });

        result.FullyProcessedSources.Should().Equal(a);
    }

    [Integration]
    public async Task CompressAsync_SeparateArchives_AlreadyCancelledToken_Throws()
    {
        string a = Path.Combine(_temp.Path, "a.txt");
        File.WriteAllText(a, "a");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [a],
            DestinationFolder = Dest("out"),
            Mode = ArchiveMode.SeparateArchives,
            Format = ArchiveContainerFormat.Tar,
        }, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
