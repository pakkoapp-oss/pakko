using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F216: an entry the user explicitly chose to skip in the conflict prompt is the user's own
/// decision, not a problem to warn about — "No entries were extracted — every entry was skipped"
/// is only for skips nobody asked for. The archive still never counts as fully processed.
/// </summary>
public sealed class ZipArchiveServiceUserSkipTests : IDisposable
{
    private const string EverySkipped = "No entries were extracted from this archive — every entry was skipped.";

    private readonly TempDirectory _temp = new();
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());

    public void Dispose() => _temp.Dispose();

    private string WriteZip(params string[] names)
    {
        string path = Path.Combine(_temp.Path, "in.zip");
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (string name in names)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write("new " + name);
        }
        return path;
    }

    private string DestWith(params string[] existing)
    {
        string dest = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(dest);
        foreach (string name in existing)
            File.WriteAllText(Path.Combine(dest, name), "old");
        return dest;
    }

    private static ExtractOptions Ask(string zip, string dest, Func<ConflictInfo, Task<ConflictDecision>>? answer) => new()
    {
        ArchivePaths = [zip],
        DestinationFolder = dest,
        Mode = ExtractMode.SingleFolder,
        OnConflict = ConflictBehavior.Ask,
        ResolveConflictAsync = answer,
    };

    [Fact]
    public async Task UserChoseSkipAll_NoEverySkippedWarningAndNothingDeletable()
    {
        string zip = WriteZip("a.txt", "b.txt");
        string dest = DestWith("a.txt", "b.txt");
        int prompts = 0;

        ArchiveResult result = await _sut.ExtractAsync(Ask(zip, dest, _ =>
        {
            prompts++;
            return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Skip, ApplyToAll = true });
        }));

        prompts.Should().Be(1);
        result.SkippedFiles.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().BeEmpty();
        result.FullyProcessedSources.Should().BeEmpty();
        File.ReadAllText(Path.Combine(dest, "a.txt")).Should().Be("old");
    }

    [Fact]
    public async Task UserSkippedEachEntryOneByOne_NoEverySkippedWarning()
    {
        string zip = WriteZip("a.txt", "b.txt");
        string dest = DestWith("a.txt", "b.txt");

        ArchiveResult result = await _sut.ExtractAsync(Ask(zip, dest,
            _ => Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Skip })));

        result.SkippedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task AskWithNoPromptWired_DefaultSkipStillWarns()
    {
        string zip = WriteZip("a.txt");
        string dest = DestWith("a.txt");

        ArchiveResult result = await _sut.ExtractAsync(Ask(zip, dest, answer: null));

        result.SkippedFiles.Should().ContainSingle().Which.Reason.Should().Be(EverySkipped);
    }

    [Fact]
    public async Task UserSkipPlusASafetySkip_StillWarns()
    {
        string zip = WriteZip("a.txt", "CON.txt");
        string dest = DestWith("a.txt");

        ArchiveResult result = await _sut.ExtractAsync(Ask(zip, dest,
            _ => Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Skip, ApplyToAll = true })));

        result.SkippedFiles.Should().Contain(s => s.Reason == EverySkipped);
    }

    [Fact]
    public async Task UserSkippedOneEntryAndAnotherWasExtracted_SourceIsPartial()
    {
        string zip = WriteZip("a.txt", "b.txt");
        string dest = DestWith("a.txt");

        ArchiveResult result = await _sut.ExtractAsync(Ask(zip, dest,
            _ => Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Skip })));

        result.SkippedFiles.Should().BeEmpty();
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Partial);
        File.ReadAllText(Path.Combine(dest, "b.txt")).Should().Be("new b.txt");
    }

    [Fact]
    public async Task UserChoseOverwrite_ExtractsAsBefore()
    {
        string zip = WriteZip("a.txt");
        string dest = DestWith("a.txt");

        ArchiveResult result = await _sut.ExtractAsync(Ask(zip, dest,
            _ => Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Overwrite })));

        result.SkippedFiles.Should().BeEmpty();
        File.ReadAllText(Path.Combine(dest, "a.txt")).Should().Be("new a.txt");
    }
}
