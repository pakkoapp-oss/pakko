using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F113: an encrypted 7z/RAR must fail with a clean, consistent message instead of raw
/// libarchive stderr. RAR's cases are caught proactively (ArchiveFormatDetector.IsEncryptedRar/
/// IsRarHeaderEncrypted, no tar.exe launch needed); 7z's are caught reactively
/// (TarSandboxedService.IsLikelyEncryptionFailure), since 7z's header metadata isn't cheaply
/// inspectable the way RAR's is — see DECISIONS.md's T-F113 entry for the full empirical trail
/// and Fixtures/README.md for how these four fixtures were built.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceEncryptedFormatsTests : IDisposable
{
    private const string ExpectedExtractMessage = "This archive is password-protected and cannot be extracted.";
    private const string ExpectedBrowseMessage = "This archive is password-protected and cannot be browsed.";

    private readonly TarSandboxedService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name);

    [SkipIfFormatUnsupported("7z")]
    public async Task ExtractAsync_Encrypted7z_DataOnly_FailsWithCleanMessage()
    {
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixturePath("encrypted.7z")],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message == ExpectedExtractMessage);
    }

    [SkipIfFormatUnsupported("7z")]
    public async Task ExtractAsync_Encrypted7z_HeaderEncrypted_FailsWithCleanMessage()
    {
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixturePath("encrypted_headers.7z")],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message == ExpectedExtractMessage);
    }

    [SkipIfFormatUnsupported("7z")]
    public async Task ListEntriesAsync_Encrypted7z_DataOnly_ListsNameSuccessfully()
    {
        // Data-only encryption doesn't encrypt filenames — listing should still succeed,
        // matching ZipArchiveService.ListEntriesAsync's parity for an encrypted ZIP.
        var result = await _sut.ListEntriesAsync(FixturePath("encrypted.7z"));

        result.Success.Should().BeTrue();
        result.Entries.Should().ContainSingle(e => e.Path == "entry.txt");
    }

    [SkipIfFormatUnsupported("7z")]
    public async Task ListEntriesAsync_Encrypted7z_HeaderEncrypted_FailsWithCleanMessage()
    {
        var result = await _sut.ListEntriesAsync(FixturePath("encrypted_headers.7z"));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be(ExpectedBrowseMessage);
    }

    [SkipIfFormatUnsupported("rar")]
    public async Task ExtractAsync_EncryptedRar_DataOnly_FailsWithCleanMessage()
    {
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixturePath("encrypted.rar")],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message == ExpectedExtractMessage);
    }

    [SkipIfFormatUnsupported("rar")]
    public async Task ExtractAsync_EncryptedRar_HeaderEncrypted_FailsWithCleanMessage()
    {
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixturePath("encrypted_headers.rar")],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
            Mode = ExtractMode.SingleFolder,
        });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message == ExpectedExtractMessage);
    }

    [SkipIfFormatUnsupported("rar")]
    public async Task ListEntriesAsync_EncryptedRar_DataOnly_ListsNameSuccessfully()
    {
        // Same parity as the 7z case above — RAR's proactive check (IsRarHeaderEncrypted) is
        // deliberately narrower than the one ExtractAsync uses, so browsing still works here.
        var result = await _sut.ListEntriesAsync(FixturePath("encrypted.rar"));

        result.Success.Should().BeTrue();
        result.Entries.Should().ContainSingle(e => e.Path == "entry.txt");
    }

    [SkipIfFormatUnsupported("rar")]
    public async Task ListEntriesAsync_EncryptedRar_HeaderEncrypted_FailsWithCleanMessage()
    {
        var result = await _sut.ListEntriesAsync(FixturePath("encrypted_headers.rar"));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be(ExpectedBrowseMessage);
    }
}
