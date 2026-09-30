using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F230: one entry Windows cannot write (a name legal on macOS/Linux, e.g. "What?.txt") aborted
// the whole archive with a message naming the internal staging path, and a failed run left the
// destination folder it had created behind, empty.
public sealed class ZipArchiveServiceExtractPerEntryFailureTests : IDisposable
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string CreateZip(string name, params string[] entries)
    {
        string zipPath = Path.Combine(_temp.Path, name);
        using ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (string entry in entries)
        {
            using var w = new StreamWriter(archive.CreateEntry(entry).Open());
            w.Write("content of " + entry);
        }
        return zipPath;
    }

    [Theory]
    [InlineData("What?.txt")]
    [InlineData("bad|pipe/inner.txt")]
    public async Task ExtractAsync_OneUnwritableEntry_OthersStillExtractAndErrorNamesTheEntry(string badName)
    {
        string zip = CreateZip("qmark.zip", "ok1.txt", badName, "ok2.txt");
        string dest = Path.Combine(_temp.Path, "q");
        Directory.CreateDirectory(dest);

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
        });

        File.Exists(Path.Combine(dest, "ok1.txt")).Should().BeTrue();
        File.Exists(Path.Combine(dest, "ok2.txt")).Should().BeTrue();
        ArchiveError error = result.Errors.Should().ContainSingle().Subject;
        error.Message.Should().Contain(badName).And.NotContain(".pakko-x");
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.Partial);
        Directory.GetDirectories(dest, ".pakko-x-*").Should().BeEmpty();
    }

    // T-F237: a 20,000-level name fails only itself (over MaxEntryDepth), and the error stays
    // readable - it used to carry the whole 40,000-character path twice (80 KB).
    [Fact]
    public async Task ExtractAsync_TwentyThousandSegmentName_OthersExtractAndTheErrorIsShort()
    {
        string deep = string.Concat(Enumerable.Repeat("a/", 20_000)) + "x.txt";
        string zip = CreateZip("deep.zip", "ok1.txt", deep, "ok2.txt");
        string dest = Path.Combine(_temp.Path, "d");
        Directory.CreateDirectory(dest);

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
        });

        File.Exists(Path.Combine(dest, "ok1.txt")).Should().BeTrue();
        File.Exists(Path.Combine(dest, "ok2.txt")).Should().BeTrue();
        Directory.Exists(Path.Combine(dest, "a")).Should().BeFalse();
        ArchiveError error = result.Errors.Should().ContainSingle().Subject;
        error.Message.Length.Should().BeLessThan(8 * 1024);
        error.Message.Should().StartWith("Entry 'a/a/").And.Contain("x.txt").And.Contain("unsafe path");
    }

    [Fact]
    public async Task ExtractAsync_NameDeeperThanTheLimit_OnlyThatEntryIsRefusedAndNoFolderIsMade()
    {
        string deep = string.Concat(Enumerable.Repeat("a/", ArchiveEntrySecurity.MaxEntryDepth)) + "x.txt";
        string zip = CreateZip("deep.zip", "ok1.txt", deep, "ok2.txt");
        string dest = Path.Combine(_temp.Path, "lim");
        Directory.CreateDirectory(dest);

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
        });

        File.Exists(Path.Combine(dest, "ok1.txt")).Should().BeTrue();
        File.Exists(Path.Combine(dest, "ok2.txt")).Should().BeTrue();
        Directory.Exists(Path.Combine(dest, "a")).Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("unsafe path");
    }

    // Shell's "Extract to <name>\" passes a fresh folder that does not exist yet.
    [Fact]
    public async Task ExtractAsync_NothingExtracted_RemovesTheDestinationFolderItCreated()
    {
        string zip = CreateZip("evil.zip", "../escape.txt");
        string fresh = Path.Combine(_temp.Path, "evil (1)");

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = fresh,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeFalse();
        Directory.Exists(fresh).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_NothingExtracted_KeepsADestinationFolderThatAlreadyExisted()
    {
        string zip = CreateZip("evil.zip", "../escape.txt");
        string existing = Path.Combine(_temp.Path, "mine");
        Directory.CreateDirectory(existing);

        await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = existing,
            Mode = ExtractMode.SingleFolder,
        });

        Directory.Exists(existing).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_SomethingExtracted_KeepsTheDestinationFolderItCreated()
    {
        string zip = CreateZip("mixed.zip", "ok.txt", "../escape.txt");
        string fresh = Path.Combine(_temp.Path, "mixed (1)");

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = fresh,
            Mode = ExtractMode.SingleFolder,
        });

        File.Exists(Path.Combine(fresh, "ok.txt")).Should().BeTrue();
        result.CreatedFiles.Should().OnlyContain(p => Directory.Exists(p) || File.Exists(p));
    }

    [Theory]
    [InlineData(unchecked((int)0x80070070), true)]  // ERROR_DISK_FULL
    [InlineData(unchecked((int)0x80070027), true)]  // ERROR_HANDLE_DISK_FULL
    [InlineData(unchecked((int)0x8007007B), false)] // ERROR_INVALID_NAME
    [InlineData(unchecked((int)0x80070020), false)] // ERROR_SHARING_VIOLATION
    public void IsDiskFull_RecognizesOnlyTheTwoDiskFullCodes(int hResult, bool expected)
    {
        ZipArchiveService.IsDiskFull(new IOException("x", hResult)).Should().Be(expected);
    }
}
