using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F221: what a missing or non-archive input says, and an archive written under exactly the
// name asked for. Every frontend shows these; the CLI reported them worst.
public sealed class CliFacingMessagesTests : IDisposable
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Extract_MissingArchive_SaysNotFound()
    {
        string missing = Path.Combine(_temp.Path, "nosuch.zip");

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions { ArchivePaths = [missing], DestinationFolder = _temp.Path });

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.SourceNotFound);
    }

    [Fact]
    public async Task Test_MissingArchive_SaysNotFound()
    {
        ArchiveResult result = await _sut.TestAsync([Path.Combine(_temp.Path, "missing.tar.gz")]);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.SourceNotFound);
    }

    [Fact]
    public async Task List_MissingArchive_SaysNotFound()
    {
        ArchiveListResult result = await _sut.ListEntriesAsync(Path.Combine(_temp.Path, "nosuch.zip"));

        result.ErrorText!.Code.Should().Be(MessageCode.SourceNotFound);
    }

    [Fact]
    public async Task List_NotAnArchive_SaysSoInsteadOfARawZipReaderMessage()
    {
        string text = _temp.CreateFile("notes.txt", "not an archive");

        ArchiveListResult result = await _sut.ListEntriesAsync(text);

        result.ErrorText!.Code.Should().Be(MessageCode.NotAnArchiveList);
        result.ErrorMessage.Should().NotContain("Central Directory");
    }

    [Fact]
    public async Task List_EmptyFile_SaysNotAnArchive()
    {
        string empty = _temp.CreateFile("stdin.bin", string.Empty);

        ArchiveListResult result = await _sut.ListEntriesAsync(empty);

        result.ErrorText!.Code.Should().Be(MessageCode.NotAnArchiveList);
    }

    [Fact]
    public async Task Hash_MissingFile_SaysNotFound()
    {
        string missing = Path.Combine(_temp.Path, "nope.bin");

        HashResult result = await FileHashService.ComputeAsync([missing, _temp.CreateFile("a.txt", "a")], HashAlgorithmKind.Crc32, progress: null, CancellationToken.None);

        result.Entries.Single(e => e.SourcePath == missing).ErrorText!.Code.Should().Be(MessageCode.SourceNotFound);
    }

    [Theory]
    [InlineData("x.gz")]
    [InlineData("backup.out")]
    [InlineData("plain.zip")]
    public async Task Archive_ExactFileName_IsWrittenAsGiven(string fileName)
    {
        string source = _temp.CreateFile("a.txt", "a");
        string dest = Path.Combine(_temp.Path, "out");

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [source],
            DestinationFolder = dest,
            ExactFileName = fileName,
        });

        result.CreatedFiles.Should().Equal(Path.Combine(dest, fileName));
        File.Exists(Path.Combine(dest, fileName)).Should().BeTrue();
    }
}
