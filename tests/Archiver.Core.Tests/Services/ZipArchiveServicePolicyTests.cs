using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F250: the ZIP engine honors BlockedFormats/AllowedFormats for "zip" itself, not only through
/// the routers — Shell used to call TestAsync on it directly, and a ZIP the magic-byte detector
/// calls Unknown (an empty archive, a self-extractor) reaches the engine through the Unknown bucket.
/// </summary>
public sealed class ZipArchiveServicePolicyTests : IDisposable
{
    private const string ZipBlockedReason = "This archive format (zip) is blocked by Group Policy.";
    private static readonly GroupPolicyOptions ZipBlocked = new() { BlockedFormats = ["zip"] };

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // An archive with no entries is only an end-of-central-directory record ("PK\x05\x06"), which
    // ArchiveFormatDetector classifies as Unknown — yet ZipArchive opens it.
    private string WriteEmptyZip(string name)
    {
        string path = Path.Combine(_temp.Path, name);
        using (ZipFile.Open(path, ZipArchiveMode.Create)) { /* no entries */ }
        return path;
    }

    [Fact]
    public async Task TestAsync_ZipBlocked_SkipsWithPolicyReasonWithoutReadingEntries()
    {
        string corrupted = FixtureHelper.Archive("corrupted_crc_stored.zip");

        ArchiveResult result = await new ZipArchiveService(ZipBlocked).TestAsync([corrupted]);

        result.Errors.Should().BeEmpty("a blocked archive is never opened, so its bad CRC is never seen");
        SkippedFile skipped = result.SkippedFiles.Should().ContainSingle().Subject;
        (skipped.Path, skipped.Reason, skipped.Text!.Code).Should().Be((corrupted, ZipBlockedReason, MessageCode.FormatBlocked));
    }

    [Fact]
    public async Task TestAsync_OnlyAnotherFormatBlocked_StillTests()
    {
        string corrupted = FixtureHelper.Archive("corrupted_crc_stored.zip");

        ArchiveResult result = await new ZipArchiveService(new GroupPolicyOptions { BlockedFormats = ["rar"] }).TestAsync([corrupted]);

        result.Errors.Should().ContainSingle(e => e.Message.Contains("CRC-32"));
    }

    [Fact]
    public async Task TestAsync_ZipBlocked_UnrecognizedFileStillReportedAsBefore()
    {
        string text = Path.Combine(_temp.Path, "notes.txt");
        File.WriteAllText(text, "not an archive");

        ArchiveResult result = await new ZipArchiveService(ZipBlocked).TestAsync([text]);

        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("not a recognized archive format");
    }

    [Fact]
    public async Task ExtractAsync_ZipBlocked_WritesNothing()
    {
        string zip = FixtureHelper.Archive("valid_multiple_files.zip");
        string dest = Path.Combine(_temp.Path, "out");

        ArchiveResult result = await new ZipArchiveService(ZipBlocked).ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
        });

        result.SkippedFiles.Should().ContainSingle().Which.Reason.Should().Be(ZipBlockedReason);
        result.CreatedFiles.Should().BeEmpty();
        (Directory.Exists(dest) ? Directory.EnumerateFileSystemEntries(dest) : []).Should().BeEmpty();
    }

    [Fact]
    public async Task ListEntriesAsync_ZipBlocked_Refuses()
    {
        ArchiveListResult result = await new ZipArchiveService(ZipBlocked).ListEntriesAsync(FixtureHelper.Archive("valid_multiple_files.zip"));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be(ZipBlockedReason);
    }

    [Fact]
    public async Task ListingRouter_ZipBlocked_ZipTheDetectorCallsUnknownIsStillRefused()
    {
        string empty = WriteEmptyZip("empty.zip");
        ArchiveFormatDetector.Detect(empty).Should().Be(ArchiveFormat.Unknown, "this is the bypass the engine gate closes");
        var router = new ArchiveListingRouter(new ZipArchiveService(ZipBlocked), new TarSandboxedService(ZipBlocked),
            new TarCapabilities(), ZipBlocked);

        ArchiveListResult result = await router.ListEntriesAsync(empty);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be(ZipBlockedReason);
    }

    // Scan reads ZIPs itself, not through ZipArchiveService, so it needs its own zip gate.
    [Fact]
    public async Task Scan_ZipBlocked_ZipTheDetectorCallsUnknownIsNotOpened()
    {
        string empty = WriteEmptyZip("empty.zip");
        var scanner = new AntivirusScanService(new TarCapabilities(), ZipBlocked,
            () => throw new InvalidOperationException("no scanner may be opened"), () => true);

        ThreatScanResult result = await scanner.ScanAsync(new AntivirusScanOptions { ArchivePaths = [empty] });

        result.OverallVerdict.Should().Be(ThreatVerdict.Inconclusive);
        result.Findings.Should().ContainSingle().Which.Reason.Should().Be(ZipBlockedReason);
    }

    [Fact]
    public async Task ListEntriesAsync_ZipAllowed_EmptyArchiveListsAsBefore()
    {
        ArchiveListResult result = await new ZipArchiveService(new GroupPolicyOptions()).ListEntriesAsync(WriteEmptyZip("empty.zip"));

        result.Success.Should().BeTrue();
        result.Entries.Should().BeEmpty();
    }
}
