using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F50: round-trips every tar-family compression variant TarSandboxedService.ExtractAsync
/// supports, through the real system tar.exe (ExternalTarFixtureBuilder), rather than a
/// committed binary corpus - tar.exe can create every one of these formats itself (empirically
/// confirmed; see DECISIONS.md's T-F50 entry). Each test generates its own fixture inside the
/// [SkipIfFormatUnsupported]-gated test body, not in shared setup, so an unsupported format
/// skips cleanly instead of throwing during fixture creation.
/// </summary>
public sealed class TarSandboxedServiceCompressedFormatsTests : IDisposable
{
    private readonly TarSandboxedService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Integration]
    public async Task ExtractAsync_TarGz_ExtractsFileWithContent()
    {
        string archivePath = Path.Combine(_temp.Path, "valid.tar.gz");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-czf", [("a.txt", "hello gz")]);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(destDir, "a.txt")).Should().Be("hello gz");
    }

    // T-F103: SeparateFolders mode derives its per-archive subfolder name from the archive's own
    // file name — "browse_test.tar.gz" must produce a "browse_test" subfolder, not "browse_test.tar".
    // This mode wasn't previously exercised by this file (every other test here uses SingleFolder
    // with an explicit destDir), so the naming bug went uncaught until T-F05's real on-device use.
    [Integration]
    public async Task ExtractAsync_TarGz_SeparateFoldersMode_StripsCompoundExtensionForSubfolderName()
    {
        string archivePath = Path.Combine(_temp.Path, "browse_test.tar.gz");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-czf", [("a.txt", "hello gz")]);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SeparateFolders,
        });

        result.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(destDir, "browse_test", "a.txt")).Should().Be("hello gz");
        Directory.Exists(Path.Combine(destDir, "browse_test.tar")).Should().BeFalse();
    }

    [SkipIfFormatUnsupported("bz2")]
    public async Task ExtractAsync_TarBz2_ExtractsFileWithContent()
    {
        string archivePath = Path.Combine(_temp.Path, "valid.tar.bz2");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-cjf", [("a.txt", "hello bz2")]);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(destDir, "a.txt")).Should().Be("hello bz2");
    }

    [SkipIfFormatUnsupported("xz")]
    public async Task ExtractAsync_TarXz_ExtractsFileWithContent()
    {
        string archivePath = Path.Combine(_temp.Path, "valid.tar.xz");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-cJf", [("a.txt", "hello xz")]);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(destDir, "a.txt")).Should().Be("hello xz");
    }

    [SkipIfFormatUnsupported("zst")]
    public async Task ExtractAsync_TarZst_ExtractsFileWithContent()
    {
        string archivePath = Path.Combine(_temp.Path, "valid.tar.zst");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "--zstd -cf", [("a.txt", "hello zst")]);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(destDir, "a.txt")).Should().Be("hello zst");
    }

    [SkipIfFormatUnsupported("lzma")]
    public async Task ExtractAsync_TarLzma_ExtractsFileWithContent()
    {
        string archivePath = Path.Combine(_temp.Path, "valid.tar.lzma");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "--lzma -cf", [("a.txt", "hello lzma")]);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(destDir, "a.txt")).Should().Be("hello lzma");
    }

    [Integration]
    public async Task ExtractAsync_TarGzWithUnicodeFilenameAndContent_ExtractsCorrectly()
    {
        string archivePath = Path.Combine(_temp.Path, "unicode.tar.gz");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-czf",
            [("привіт.txt", "Вміст файлу з юнікодом. Hello, 世界!")]);

        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(destDir, "привіт.txt"))
            .Should().Be("Вміст файлу з юнікодом. Hello, 世界!");
    }
}
