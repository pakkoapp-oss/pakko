using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;
using System.Formats.Tar;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// Exercises TarSandboxedService.ListEntriesAsync against the real system tar.exe. tar.exe ships
/// with Windows 10 1803+/11, so this runs unconditionally — same rationale as
/// TarSandboxedServiceTests's DetectCapabilitiesAsync test, not gated behind [Integration].
/// </summary>
public sealed class TarSandboxedServiceListEntriesTests
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());

    [Fact]
    public async Task ListEntriesAsync_NestedFoldersFixture_ReturnsFlatEntriesMatchingZipCounterpart()
    {
        // valid_nested_folders.tar mirrors valid_nested_folders.zip's exact structure (built via
        // System.Formats.Tar.TarFile.CreateFromDirectory, which does write explicit directory
        // entries — unlike the ZIP counterpart) — see Archiver.Core.Tests.GenerateFixtures.
        string archivePath = FixtureHelper.Archive("valid_nested_folders.tar");

        ArchiveListResult result = await _sut.ListEntriesAsync(archivePath);

        result.Success.Should().BeTrue();
        var filePaths = result.Entries.Where(e => !e.IsDirectory).Select(e => e.Path).ToList();
        filePaths.Should().BeEquivalentTo(
        [
            "root.txt", "docs/readme.txt", "docs/manual.txt",
            "docs/sub/appendix.txt", "src/main.cs", "src/utils.cs",
        ]);
        result.Entries.Where(e => !e.IsDirectory).Should().OnlyContain(e => e.Size > 0);
        result.Entries.Where(e => e.IsDirectory).Should().Contain(e => e.Path == "docs");

        // T-F214: tar has no per-entry packed size — unknown, not 0.
        result.Entries.Should().OnlyContain(e => e.CompressedSize == null && e.Modified != null);
    }

    [Fact]
    public async Task ListEntriesAsync_ReadsModifiedFromTheVerboseListing()
    {
        // T-F214: tar.exe prints local time to the minute within half a year of now, and only
        // the date for anything older.
        using var temp = new TempDirectory();
        string archivePath = Path.Combine(temp.Path, "dates.tar");
        DateTime recent = DateTime.Now.AddDays(-3);
        recent = new DateTime(recent.Year, recent.Month, recent.Day, recent.Hour, recent.Minute, 0);
        DateTime old = new(2020, 3, 5, 14, 30, 0);
        using (var writer = new TarWriter(File.Create(archivePath), TarEntryFormat.Pax))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "recent.txt") { ModificationTime = new DateTimeOffset(recent) });
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "old/") { ModificationTime = new DateTimeOffset(old) });
        }

        ArchiveListResult result = await _sut.ListEntriesAsync(archivePath);

        result.Success.Should().BeTrue();
        result.Entries.Single(e => e.Path == "recent.txt").Modified.Should().Be(recent);
        result.Entries.Single(e => e.Path == "old").Modified.Should().Be(old.Date);
    }

    [Fact]
    public async Task ListEntriesAsync_NonExistentPath_ReturnsFailureNotException()
    {
        ArchiveListResult result = await _sut.ListEntriesAsync(@"C:\definitely\does\not\exist.tar");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }
}
