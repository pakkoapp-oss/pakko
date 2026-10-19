using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F420 independent review: what the shared path registry makes deletable.
/// </summary>
public sealed class IndependentReviewRegistryTests : IDisposable
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string CreateZip(string name, params (string Name, string Content)[] entries)
    {
        string zipPath = Path.Combine(_temp.Path, name);
        using ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach ((string entry, string content) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
            writer.Write(content);
        }
        return zipPath;
    }

    // -aot across archives: the second archive's a.txt renames the first archive's file, which stays
    // on disk as "a (1).txt". Every entry of both archives is on disk, yet Claim() of the same final
    // path marks the first archive not wholly on disk, so "Delete after" keeps it, unreported.
    [Fact]
    public async Task RenameExisting_AcrossArchives_BothArchivesWhollyOnDisk_BothDeletable()
    {
        string first = CreateZip("first.zip", ("a.txt", "FIRST"));
        string second = CreateZip("second.zip", ("a.txt", "SECOND"));
        string dest = Path.Combine(_temp.Path, "dest");

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [first, second], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.RenameExisting,
        });

        result.Errors.Should().BeEmpty();
        Directory.GetFiles(dest).Select(File.ReadAllText).Should().BeEquivalentTo(["FIRST", "SECOND"], "precondition: both entries are on disk");
        result.FullyProcessedSources.Should().BeEquivalentTo([first, second]);
    }
}
