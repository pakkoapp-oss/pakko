using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F50: formats tar.exe/libarchive can only read, never create (7z; RAR is the same gap but
/// has no encoder available on this machine at all - see Fixtures/README.md) - use a committed
/// binary fixture instead of test-time generation.
/// </summary>
public sealed class TarProcessServiceExternalFormatsTests : IDisposable
{
    private readonly TarProcessService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name);

    [SkipIfFormatUnsupported("7z")]
    public async Task ExtractAsync_Valid7z_ExtractsFileWithContent()
    {
        string destDir = Path.Combine(_temp.Path, "out");
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixturePath("valid.7z")],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(destDir, "seven.txt")).Should().Be("hello from a real 7z fixture\n");
    }
}
