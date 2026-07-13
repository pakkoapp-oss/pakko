using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// Exercises TarProcessService.ListEntriesAsync against the real system tar.exe. tar.exe ships
/// with Windows 10 1803+/11, so this runs unconditionally — same rationale as
/// TarProcessServiceTests's DetectCapabilitiesAsync test, not gated behind [Integration].
/// </summary>
public sealed class TarProcessServiceListEntriesTests
{
    private readonly TarProcessService _sut = new();

    [Fact]
    public async Task ListEntriesAsync_NestedFoldersFixture_ReturnsFlatEntriesMatchingZipCounterpart()
    {
        // valid_nested_folders.tar mirrors valid_nested_folders.zip's exact structure (built via
        // System.Formats.Tar.TarFile.CreateFromDirectory, which does write explicit directory
        // entries — unlike the ZIP counterpart) — see Archiver.Core.Tests.GenerateFixtures.
        string archivePath = FixtureHelper.Archive("valid_nested_folders.tar");

        var result = await _sut.ListEntriesAsync(archivePath);

        result.Success.Should().BeTrue();
        var filePaths = result.Entries.Where(e => !e.IsDirectory).Select(e => e.Path).ToList();
        filePaths.Should().BeEquivalentTo(
        [
            "root.txt", "docs/readme.txt", "docs/manual.txt",
            "docs/sub/appendix.txt", "src/main.cs", "src/utils.cs",
        ]);
        result.Entries.Where(e => !e.IsDirectory).Should().OnlyContain(e => e.Size > 0);
        result.Entries.Where(e => e.IsDirectory).Should().Contain(e => e.Path == "docs");

        // Date column is known locale-mangled (see TarProcessService's ScanForUnsafeEntriesAsync
        // comments) — deliberately left null rather than half-parsed.
        result.Entries.Should().OnlyContain(e => e.Modified == null);
    }

    [Fact]
    public async Task ListEntriesAsync_NonExistentPath_ReturnsFailureNotException()
    {
        var result = await _sut.ListEntriesAsync(@"C:\definitely\does\not\exist.tar");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }
}
