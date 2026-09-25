using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F228: an entry whose name climbs with ".." could leave the staging folder and come back
// into it, so the conflict check (built from the raw name against the destination) never saw it
// and the commit overwrote the user's file. Every ".." or rooted entry name is now rejected per
// entry, as tar's pre-scan already does for the whole archive.
public sealed class ZipArchiveServiceExtractUnsafePathTests : IDisposable
{
    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string CreateZip(string name, params string[] entries)
    {
        string zipPath = Path.Combine(_temp.Path, name);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (string entry in entries)
        {
            using var w = new StreamWriter(archive.CreateEntry(entry).Open());
            w.Write("ARCHIVE " + entry);
        }
        return zipPath;
    }

    private Task<ArchiveResult> ExtractIntoAsync(string zip, string dest, ConflictBehavior onConflict,
        Func<ConflictInfo, Task<ConflictDecision>>? ask = null) =>
        _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
            OnConflict = onConflict,
            ResolveConflictAsync = ask,
        });

    [Theory]
    [InlineData(ConflictBehavior.Skip)]
    [InlineData(ConflictBehavior.Rename)]
    [InlineData(ConflictBehavior.Ask)]
    public async Task ExtractAsync_EntryClimbingOutAndBack_NeverOverwritesExistingFile(ConflictBehavior onConflict)
    {
        string dest = Path.Combine(_temp.Path, "t");
        Directory.CreateDirectory(dest);
        string userFile = Path.Combine(dest, "b.txt");
        File.WriteAllText(userFile, "ORIGINAL");
        string zip = CreateZip("evil.zip", "a.txt", "../t_tmp/b.txt", "sub/../b.txt");

        var result = await ExtractIntoAsync(zip, dest, onConflict,
            _ => Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Skip }));

        File.ReadAllText(userFile).Should().Be("ORIGINAL");
        File.Exists(Path.Combine(dest, "a.txt")).Should().BeTrue("the safe entry still extracts");
        result.Errors.Should().HaveCount(2).And.OnlyContain(e => e.Message.Contains("unsafe path"));
        result.Errors.Should().Contain(e => e.Message.Contains("'../t_tmp/b.txt'"));
        result.Errors.Should().Contain(e => e.Message.Contains("'sub/../b.txt'"));
    }

    [Theory]
    [InlineData("/abs.txt")]
    [InlineData("C:/rooted.txt")]
    [InlineData("C:drive-relative.txt")]
    [InlineData("sub\\..\\..\\back.txt")]
    [InlineData("..\\up.txt")]
    public async Task ExtractAsync_RootedOrClimbingEntry_RejectedPerEntry(string unsafeName)
    {
        string dest = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(dest);
        string zip = CreateZip("evil.zip", "good.txt", unsafeName);

        var result = await ExtractIntoAsync(zip, dest, ConflictBehavior.Overwrite);

        result.Success.Should().BeFalse();
        // T-F234: names are reported '/'-separated, the same form the listing shows.
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("unsafe path").And.Contain(unsafeName.Replace('\\', '/'));
        File.ReadAllText(Path.Combine(dest, "good.txt")).Should().Be("ARCHIVE good.txt");
        Directory.GetFiles(_temp.Path, "*", SearchOption.AllDirectories)
            .Should().OnlyContain(f => f.EndsWith("evil.zip") || f.EndsWith("good.txt"));
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Partial);
    }

    [Fact]
    public async Task ExtractAsync_DotsInsideNames_AreNotTreatedAsTraversal()
    {
        string dest = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(dest);
        string zip = CreateZip("fine.zip", "a..b.txt", "..hidden.txt", "x/..y/c.txt");

        var result = await ExtractIntoAsync(zip, dest, ConflictBehavior.Overwrite);

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(dest, "a..b.txt")).Should().BeTrue();
        File.Exists(Path.Combine(dest, "x", "..y", "c.txt")).Should().BeTrue();
        File.Exists(Path.Combine(dest, "..hidden.txt")).Should().BeTrue();
    }
}
