using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F50: formats read from a committed binary fixture instead of one generated at test time -
/// RAR has no encoder available on this machine at all, and the 7z comes from another program
/// than the tar.exe under test (which can write 7z itself, T-F342) - see Fixtures/README.md.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceExternalFormatsTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name);

    [SkipIfFormatUnsupported("7z")]
    public async Task ExtractAsync_Valid7z_ExtractsFileWithContent()
    {
        string destDir = Path.Combine(_temp.Path, "out");
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixturePath("valid.7z")],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(destDir, "seven.txt")).Should().Be("hello from a real 7z fixture\n");
    }

    [SkipIfFormatUnsupported("rar")]
    public async Task ExtractAsync_ValidRar_ExtractsFileWithContent()
    {
        string destDir = Path.Combine(_temp.Path, "out");
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixturePath("valid.rar")],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(destDir, "rar.txt")).Should().Be("hello from a real rar fixture\n");
    }
}
