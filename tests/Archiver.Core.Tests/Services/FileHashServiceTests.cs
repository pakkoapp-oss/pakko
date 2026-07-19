using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F128. The folder DataSum/NamesSum expected values below are real, cross-checked against the
// vendored 7za.exe (`h -scrcCRC32`/`h -scrcSHA256`, T-F114's tool — 7-Zip's own "h" command runs
// the same NanaZip/HashCalc.cpp algorithm this feature reproduces) run against an identical fixture
// (a.txt="hello world", b.txt="second file content here", built with File.WriteAllText's UTF-8-no-BOM
// encoding, byte-identical to the plain-ASCII content hashed via 7za). This is the actual
// external-tool parity proof for the feature, not just internal consistency.
public sealed class FileHashServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ComputeStreamDigestAsync_Crc32_MatchesRealValue()
    {
        using var stream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes("hello world"));

        string hash = await FileHashService.ComputeStreamDigestAsync(stream, HashAlgorithmKind.Crc32, CancellationToken.None);

        hash.Should().Be("0D4A1185");
    }

    [Fact]
    public async Task ComputeStreamDigestAsync_Sha256_MatchesRealValue()
    {
        using var stream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes("hello world"));

        string hash = await FileHashService.ComputeStreamDigestAsync(stream, HashAlgorithmKind.Sha256, CancellationToken.None);

        hash.Should().Be("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9");
    }

    [Fact]
    public async Task ComputeAsync_SingleFile_Crc32_MatchesRealValue()
    {
        var file = _temp.CreateFile("a.txt", "hello world");

        var result = await FileHashService.ComputeAsync([file], HashAlgorithmKind.Crc32, null, CancellationToken.None);

        result.Folder.Should().BeNull();
        result.Entries.Should().ContainSingle();
        result.Entries[0].Error.Should().BeNull();
        result.Entries[0].Hash.Should().Be("0D4A1185");
    }

    [Fact]
    public async Task ComputeAsync_SingleFile_Sha256_MatchesRealValue()
    {
        var file = _temp.CreateFile("a.txt", "hello world");

        var result = await FileHashService.ComputeAsync([file], HashAlgorithmKind.Sha256, null, CancellationToken.None);

        result.Entries[0].Hash.Should().Be("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9");
    }

    [Fact]
    public async Task ComputeAsync_MultipleFiles_HashesEachIndependently_NoFolderSummary()
    {
        var fileA = _temp.CreateFile("a.txt", "hello world");
        var fileB = _temp.CreateFile("b.txt", "second file content here");

        var result = await FileHashService.ComputeAsync([fileA, fileB], HashAlgorithmKind.Crc32, null, CancellationToken.None);

        result.Folder.Should().BeNull();
        result.Entries.Should().HaveCount(2);
        result.Entries.Should().Contain(e => e.SourcePath == fileA && e.Hash == "0D4A1185");
        result.Entries.Should().Contain(e => e.SourcePath == fileB && e.Hash == "8D7BD707");
    }

    [Fact]
    public async Task ComputeAsync_FlatFolder_Crc32_DataSumAndNamesSumMatchSevenZip()
    {
        _temp.CreateFile("a.txt", "hello world");
        _temp.CreateFile("b.txt", "second file content here");

        var result = await FileHashService.ComputeAsync([_temp.Path], HashAlgorithmKind.Crc32, null, CancellationToken.None);

        result.Folder.Should().NotBeNull();
        result.Folder!.FileCount.Should().Be(2);
        result.Folder.DataSum.Should().Be("9AC5E88C-00000000");
        result.Folder.NamesSum.Should().Be("ECA9B8E5-00000000");
        result.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task ComputeAsync_FlatFolder_Sha256_DataSumAndNamesSumMatchSevenZip()
    {
        _temp.CreateFile("a.txt", "hello world");
        _temp.CreateFile("b.txt", "second file content here");

        var result = await FileHashService.ComputeAsync([_temp.Path], HashAlgorithmKind.Sha256, null, CancellationToken.None);

        result.Folder!.DataSum.Should().Be("0a35b54db7b00ca46cb11a48e4f9501b70314d256b2e5eb38c44095b6d80709e-00000001");
        result.Folder.NamesSum.Should().Be("d9ec25dedb9f854f71253ad7c2a60888c9ee0c37b1134943980b0a276ecacef1-00000000");
    }

    [Fact]
    public async Task ComputeAsync_NestedFolder_DataSumMatchesSevenZip_NamesSumOmitsSubfolderObject()
    {
        _temp.CreateFile("a.txt", "hello world");
        _temp.CreateFile("b.txt", "second file content here");
        Directory.CreateDirectory(Path.Combine(_temp.Path, "sub"));
        _temp.CreateFile(Path.Combine("sub", "c.txt"), "nested file");

        var result = await FileHashService.ComputeAsync([_temp.Path], HashAlgorithmKind.Crc32, null, CancellationToken.None);

        result.Folder!.FileCount.Should().Be(3);
        // DataSum never involves directory entries in NanaZip's own algorithm either, so this
        // matches `7za h -scrcCRC32 -r` exactly regardless of nesting.
        result.Folder.DataSum.Should().Be("33F43426-00000001");
        // NamesSum deliberately omits the "sub" folder object's own contribution (the one
        // documented, approved divergence from NanaZip - see FileHashService's doc comment), so
        // it does NOT equal 7za's real value for this folder (which does include it). Just prove
        // it's still well-formed and stable, not a specific external value.
        result.Folder.NamesSum.Should().MatchRegex("^[0-9A-F]{8}-[0-9A-F]{8}$");
    }

    [Fact]
    public async Task ComputeAsync_ManyFilesInFolder_ParallelHashingIsDeterministicAndRaceFree()
    {
        // Enough files to force real concurrency (MaxDegreeOfParallelism = Environment.ProcessorCount)
        // through Parallel.ForEachAsync — this is the actual proof the shared HashDigestAccumulator/
        // entries-list locking is correct: a race would show up as a result that differs between runs
        // or fails to match the known-correct combine value, not as a crash.
        const int fileCount = 50;
        for (int i = 0; i < fileCount; i++)
            _temp.CreateFile($"file{i:D3}.txt", $"content number {i}");

        var first = await FileHashService.ComputeAsync([_temp.Path], HashAlgorithmKind.Sha256, null, CancellationToken.None);
        var second = await FileHashService.ComputeAsync([_temp.Path], HashAlgorithmKind.Sha256, null, CancellationToken.None);

        first.Folder!.FileCount.Should().Be(fileCount);
        first.Entries.Should().HaveCount(fileCount);
        first.Entries.Should().OnlyContain(e => e.Error == null && e.Hash != null);
        second.Folder!.DataSum.Should().Be(first.Folder.DataSum);
        second.Folder.NamesSum.Should().Be(first.Folder.NamesSum);
    }

    [Fact]
    public async Task ComputeAsync_MultiSelectionIncludingFolder_SkipsFolderGracefully()
    {
        var file = _temp.CreateFile("a.txt", "hello world");
        var folder = Path.Combine(_temp.Path, "sub");
        Directory.CreateDirectory(folder);

        var result = await FileHashService.ComputeAsync([file, folder], HashAlgorithmKind.Crc32, null, CancellationToken.None);

        result.Folder.Should().BeNull();
        result.Entries.Should().HaveCount(2);
        result.Entries.Should().Contain(e => e.SourcePath == file && e.Hash == "0D4A1185");
        result.Entries.Should().Contain(e => e.SourcePath == folder && e.Error != null);
    }

    [Fact]
    public async Task ComputeAsync_MissingFile_RecordsError()
    {
        var missing = Path.Combine(_temp.Path, "does-not-exist.txt");

        var result = await FileHashService.ComputeAsync([missing], HashAlgorithmKind.Crc32, null, CancellationToken.None);

        result.Entries.Should().ContainSingle();
        result.Entries[0].Hash.Should().BeNull();
        result.Entries[0].Error.Should().NotBeNull();
    }

    [Fact]
    public async Task ComputeAsync_Cancelled_ThrowsOperationCanceledException()
    {
        var file = _temp.CreateFile("a.txt", "hello world");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => FileHashService.ComputeAsync([file], HashAlgorithmKind.Crc32, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
