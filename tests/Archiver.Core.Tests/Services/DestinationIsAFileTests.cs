using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F406: a destination folder that is an existing file made Directory.CreateDirectory throw out
// of ExtractAsync, ArchiveAsync and CompressAsync, and `pakko x a.zip -o<file>` died with an
// unhandled exception. Found by T-F343's measurement (7-Zip exits 2 with an error).
public sealed class DestinationIsAFileTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _file;
    private readonly string _source;

    public DestinationIsAFileTests()
    {
        _file = Path.Combine(_temp.Path, "not-a-folder");
        File.WriteAllText(_file, "keep me");
        _source = Path.Combine(_temp.Path, "a.txt");
        File.WriteAllText(_source, "a");
    }

    public void Dispose() => _temp.Dispose();

    private static TarSandboxedService Tar() => new(
        new GroupPolicyOptions(),
        () => Task.FromResult(TarVersionParser.Parse("bsdtar 3.8.8 - libarchive 3.8.8 zlib/1.2.13 liblzma/5.8.1 bz2lib/1.0.8 libzstd/1.5.7")),
        sevenZipWriterProbe: () => Task.FromResult<bool?>(true));

    private void FileIsUntouched() => File.ReadAllText(_file).Should().Be("keep me");

    [Fact]
    public async Task ZipExtract_IntoAFile_IsAnErrorForTheArchive()
    {
        string zip = FixtureHelper.Archive("valid_single_file.zip");

        ArchiveResult result = await new ZipArchiveService(new GroupPolicyOptions())
            .ExtractAsync(new ExtractOptions { ArchivePaths = [zip], DestinationFolder = _file, Mode = ExtractMode.SingleFolder });

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.CannotExtractArchive);
        result.Errors[0].SourcePath.Should().Be(zip);
        result.CreatedFiles.Should().BeEmpty();
        FileIsUntouched();
    }

    [Fact]
    public async Task TarExtract_IntoAFile_IsAnErrorForTheArchive()
    {
        string tar = FixtureHelper.Archive("valid_nested_folders.tar");

        ArchiveResult result = await Tar()
            .ExtractAsync(new ExtractOptions { ArchivePaths = [tar], DestinationFolder = _file, Mode = ExtractMode.SingleFolder });

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.CannotExtractArchive);
        result.Errors[0].SourcePath.Should().Be(tar);
        FileIsUntouched();
    }

    [Theory]
    [InlineData(ArchiveMode.SingleArchive)]
    [InlineData(ArchiveMode.SeparateArchives)]
    public async Task ZipArchive_IntoAFile_IsAnErrorNotAThrow(ArchiveMode mode)
    {
        ArchiveResult result = await new ZipArchiveService(new GroupPolicyOptions()).ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [_source],
            DestinationFolder = _file,
            ArchiveName = "out",
            Mode = mode,
        });

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.CannotCreateArchive);
        result.CreatedFiles.Should().BeEmpty();
        FileIsUntouched();
    }

    [Fact]
    public async Task TarCompress_IntoAFile_IsAnErrorNotAThrow()
    {
        ArchiveResult result = await Tar().CompressAsync(new ArchiveOptions
        {
            SourcePaths = [_source],
            DestinationFolder = _file,
            ArchiveName = "out",
            Format = ArchiveContainerFormat.TarGz,
        });

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.CannotCreateArchive);
        result.CreatedFiles.Should().BeEmpty();
        FileIsUntouched();
    }
}
