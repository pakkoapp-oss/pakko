using System.IO.Compression;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F410: ZipArchiveService.TestAsync with an entry filter reads only the entries it passes, as
/// 7-Zip's "t" with -i/-x does (measured: a damaged entry the filter leaves out is not reported).
/// </summary>
public sealed class ZipArchiveServiceTestFilterTests : IDisposable
{
    private const string DamagedContent = "damaged-entry-payload-0123456789";
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly string _dir = Directory.CreateTempSubdirectory("PakkoTestFilter").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task TestAsync_FilterLeavesOutTheDamagedEntry_Passes()
    {
        string archive = ArchiveWithOneDamagedEntry();

        ArchiveResult result = await _sut.TestAsync([archive], entryFilter: (name, _) => name != "a.bin");

        result.Errors.Should().BeEmpty();
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Completed);
    }

    [Fact]
    public async Task TestAsync_FilterTakesTheDamagedEntry_Fails()
    {
        string archive = ArchiveWithOneDamagedEntry();

        ArchiveResult result = await _sut.TestAsync([archive], entryFilter: (name, _) => name == "a.bin");

        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("a.bin");
    }

    [Fact]
    public async Task TestAsync_Filter_IsAskedForEveryEntryFoldersIncluded()
    {
        string archive = ArchiveWithOneDamagedEntry();
        var asked = new List<(string Name, bool IsFolder)>();

        await _sut.TestAsync([archive], entryFilter: (name, isFolder) =>
        {
            asked.Add((name, isFolder));
            return false;
        });

        asked.Should().BeEquivalentTo([("a.bin", false), ("dir/", true), ("dir/c.txt", false)]);
    }

    [Fact]
    public async Task TestAsync_FilterPassesNothing_CleanWithNothingRead()
    {
        string archive = ArchiveWithOneDamagedEntry();

        ArchiveResult result = await _sut.TestAsync([archive], entryFilter: (_, _) => false);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Completed);
    }

    // a.bin is stored, so flipping a byte of its content leaves the ZIP's structure intact and
    // fails only that entry's CRC-32.
    private string ArchiveWithOneDamagedEntry()
    {
        string archive = Path.Combine(_dir, "t.zip");
        using (var zip = new ZipArchive(File.Create(archive), ZipArchiveMode.Create))
        {
            Write(zip, "a.bin", DamagedContent);
            zip.CreateEntry("dir/");
            Write(zip, "dir/c.txt", "fine");
        }
        byte[] bytes = File.ReadAllBytes(archive);
        int at = bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(DamagedContent));
        at.Should().BePositive();
        bytes[at] ^= 0xFF;
        File.WriteAllBytes(archive, bytes);
        return archive;
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using Stream entry = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
        entry.Write(Encoding.ASCII.GetBytes(content));
    }
}
