using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

public sealed class ZipArchiveServiceListEntriesTests
{
    private readonly ZipArchiveService _sut = new();

    [Fact]
    public async Task ListEntriesAsync_NestedFoldersFixture_ReturnsFlatEntriesMatchingKnownShape()
    {
        // valid_nested_folders.zip: 6 entries, 3-level nesting (root.txt, docs/readme.txt,
        // docs/manual.txt, docs/sub/appendix.txt, src/main.cs, src/utils.cs) — no explicit
        // directory entries, folders are only implied by '/' in file paths (confirmed by reading
        // the generator: every entry is CreateEntryFromContent, never CreateEntry("dir/")).
        string archivePath = FixtureHelper.Archive("valid_nested_folders.zip");

        var result = await _sut.ListEntriesAsync(archivePath);

        result.Success.Should().BeTrue();
        result.Entries.Should().HaveCount(6);
        result.Entries.Should().OnlyContain(e => !e.IsDirectory,
            "this fixture has no explicit directory entries — folders are implied by path only");
        result.Entries.Select(e => e.Path).Should().BeEquivalentTo(
        [
            "root.txt", "docs/readme.txt", "docs/manual.txt",
            "docs/sub/appendix.txt", "src/main.cs", "src/utils.cs",
        ]);
        result.Entries.Should().OnlyContain(e => e.Size > 0);
    }

    [Fact]
    public async Task ListEntriesAsync_CorruptedCentralDirectory_ReturnsFailureNotException()
    {
        string archivePath = FixtureHelper.Archive("corrupted_central_directory.zip");

        var result = await _sut.ListEntriesAsync(archivePath);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ListEntriesAsync_NonExistentPath_ReturnsFailureNotException()
    {
        var result = await _sut.ListEntriesAsync(@"C:\definitely\does\not\exist.zip");

        result.Success.Should().BeFalse();
    }
}
