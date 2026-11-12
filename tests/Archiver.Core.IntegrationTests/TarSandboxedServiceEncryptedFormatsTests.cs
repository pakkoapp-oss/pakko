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
    private const string ExpectedExtractMessage = "This archive is password-protected. Passwords are supported for ZIP archives only.";
    private const string ExpectedBrowseMessage = ExpectedExtractMessage;

    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name);

    [SkipIfFormatUnsupported("7z")]
    public async Task ExtractAsync_Encrypted7z_DataOnly_FailsWithCleanMessage()
    {
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
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
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
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
        ArchiveListResult result = await _sut.ListEntriesAsync(FixturePath("encrypted.7z"));

        result.Success.Should().BeTrue();
        result.Entries.Should().ContainSingle(e => e.Path == "entry.txt");
    }

    [SkipIfFormatUnsupported("7z")]
    public async Task ListEntriesAsync_Encrypted7z_HeaderEncrypted_FailsWithCleanMessage()
    {
        ArchiveListResult result = await _sut.ListEntriesAsync(FixturePath("encrypted_headers.7z"));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be(ExpectedBrowseMessage);
    }

    [SkipIfFormatUnsupported("rar")]
    public async Task ExtractAsync_EncryptedRar_DataOnly_FailsWithCleanMessage()
    {
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
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
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
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
        ArchiveListResult result = await _sut.ListEntriesAsync(FixturePath("encrypted.rar"));

        result.Success.Should().BeTrue();
        result.Entries.Should().ContainSingle(e => e.Path == "entry.txt");
    }

    [SkipIfFormatUnsupported("rar")]
    public async Task ListEntriesAsync_EncryptedRar_HeaderEncrypted_FailsWithCleanMessage()
    {
        ArchiveListResult result = await _sut.ListEntriesAsync(FixturePath("encrypted_headers.rar"));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be(ExpectedBrowseMessage);
    }

    // T-F518: tar.exe's stderr names the entry it failed on ("encrypted_notes.txt: Decompression
    // failed (9)"), so a bare "encrypt" in it called a damaged plain 7z password-protected. The
    // listing reads no data, so it passes; extraction and Test fail on the damaged data.
    [SkipIfFormatUnsupported("7z")]
    public async Task DamagedPlain7z_EntryNamedEncrypted_IsNotPasswordProtected()
    {
        ArchiveResult extracted = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [FixturePath("damaged_data.7z")],
            DestinationFolder = Path.Combine(_temp.Path, "out"),
            Mode = ExtractMode.SingleFolder,
        });
        ArchiveResult tested = await _sut.TestAsync([FixturePath("damaged_data.7z")]);

        extracted.Errors.Should().ContainSingle().Which.Message.Should().NotBe(ExpectedExtractMessage);
        tested.Errors.Should().ContainSingle().Which.Message.Should().NotBe(ExpectedExtractMessage);
    }
}
