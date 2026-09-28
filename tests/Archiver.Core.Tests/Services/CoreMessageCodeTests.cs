using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F209: the messages users actually meet carry their code, so every frontend can show them in
// the user's language — and the English text stays exactly what it was.
public sealed class CoreMessageCodeTests : IDisposable
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private Task<ArchiveResult> ExtractAsync(string archive) => _sut.ExtractAsync(new ExtractOptions
    {
        ArchivePaths = [archive],
        DestinationFolder = _temp.Path,
        Mode = ExtractMode.SeparateFolders,
    });

    [Fact]
    public async Task Extract_CorruptedZip_ReportsZipCorrupted()
    {
        ArchiveResult result = await ExtractAsync(FixtureHelper.Archive("corrupted_central_directory.zip"));

        ArchiveError error = result.Errors.Should().ContainSingle().Subject;
        error.Text!.Code.Should().Be(MessageCode.ZipCorrupted);
        error.Message.Should().Be(error.Text.English);
    }

    [Fact]
    public async Task Extract_GZipThroughZipEngine_ReportsUnsupportedFormatWithItsName()
    {
        string gz = Path.Combine(_temp.Path, "notes.gz");
        using (var gzip = new GZipStream(File.Create(gz), CompressionLevel.Fastest))
            gzip.Write("hello"u8);

        ArchiveResult result = await ExtractAsync(gz);

        SkippedFile skipped = result.SkippedFiles.Should().ContainSingle().Subject;
        skipped.Text!.Code.Should().Be(MessageCode.UnsupportedByZipEngine);
        skipped.Text.Arguments.Should().Equal("GZip");
        skipped.Reason.Should().Be("GZip format is not supported. Only ZIP-based formats are supported.");
    }

    [Fact]
    public async Task Extract_TextFile_ReportsNotAnArchive()
    {
        string text = _temp.CreateFile("notes.txt", "not an archive");

        ArchiveResult result = await ExtractAsync(text);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.NotAnArchiveExtract);
    }

    [Fact]
    public async Task Archive_MissingSource_ReportsSourceNotFoundWithThePath()
    {
        string missing = Path.Combine(_temp.Path, "nope.txt");

        ArchiveResult result = await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [missing],
            DestinationFolder = _temp.Path,
            ArchiveName = "out",
        });

        ArchiveError error = result.Errors.Should().ContainSingle().Subject;
        error.Text!.Code.Should().Be(MessageCode.SourceNotFound);
        error.Text.Arguments.Should().Equal(missing);
        error.Message.Should().Be($"Source path does not exist: {missing}");
    }

    [Fact]
    public async Task Extract_EveryEntryAlreadyThere_ReportsAllEntriesSkipped()
    {
        string zip = FixtureHelper.Archive("valid_single_file.zip");
        await _sut.ExtractAsync(new ExtractOptions { ArchivePaths = [zip], DestinationFolder = _temp.Path, Mode = ExtractMode.SingleFolder });

        ArchiveResult again = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = _temp.Path,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Skip,
        });

        again.SkippedFiles.Should().Contain(s => s.Text!.Code == MessageCode.AllEntriesSkipped);
    }

    [Fact]
    public async Task Extract_CompressionBombDeclined_ReportsRatioAndSize()
    {
        string file = _temp.CreateFile("compressible.txt", new string('A', 50 * 1024 * 1024));
        await _sut.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [file],
            DestinationFolder = _temp.Path,
            ArchiveName = "bomb",
            CompressionLevel = CompressionLevel.SmallestSize,
        });
        string archive = Path.Combine(_temp.Path, "bomb.zip");

        ArchiveResult result = await ExtractAsync(archive);

        SkippedFile skipped = result.SkippedFiles.Should().ContainSingle(s => s.Path == archive).Subject;
        skipped.Text!.Code.Should().Be(MessageCode.ZipBombDeclined);
        skipped.Text.Arguments.Should().HaveCount(2);
        skipped.Reason.Should().StartWith("Suspicious compression ratio (");
    }

    [Fact]
    public async Task ListEntries_BlockedZip_CarriesThePolicyCode()
    {
        ArchiveListResult result = await new ZipArchiveService(new GroupPolicyOptions { BlockedFormats = ["zip"] })
            .ListEntriesAsync(FixtureHelper.Archive("valid_single_file.zip"));

        result.ErrorText!.Code.Should().Be(MessageCode.FormatBlocked);
        result.ErrorMessage.Should().Be("This archive format (zip) is blocked by Group Policy.");
    }
}
