using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// Fixture-based tests for ZipArchiveService.TestAsync (T-F62).
/// Requires generated fixtures — run: dotnet run --project tests/Archiver.Core.Tests.GenerateFixtures
/// </summary>
public sealed class ZipArchiveServiceTestAsyncTests
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());

    // T-F268 (found while pinning Archiver.Shell's cancel path): a cancel between archives used to
    // `break` and return Success = true, so a cancelled Test reported "no errors detected". The
    // T-F260 contract is OperationCanceledException on cancellation.
    [Fact]
    public async Task TestAsync_CancelledBeforeAnyArchive_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task<ArchiveResult>> act = () => _sut.TestAsync([FixtureHelper.Archive("valid_multiple_files.zip")], cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // T-F279: a cancel inside one archive used to `break` out of the entry loop, and one large
    // entry was read to the end before the token was looked at again — the archive was reported
    // as tested with no errors, and the App showed "no errors found" for a cancelled Test.
    [Fact]
    public async Task TestAsync_CancelledInsideOneLargeEntry_StopsAndThrows()
    {
        string dir = Directory.CreateTempSubdirectory("PakkoTestCancel").FullName;
        try
        {
            string archive = Path.Combine(dir, "zeros.zip");
            using (var zip = new System.IO.Compression.ZipArchive(File.Create(archive), System.IO.Compression.ZipArchiveMode.Create))
            using (Stream entry = zip.CreateEntry("zeros.bin", System.IO.Compression.CompressionLevel.Fastest).Open())
            {
                byte[] block = new byte[4 * 1024 * 1024];
                for (int i = 0; i < 256; i++)
                    entry.Write(block);
            }
            using var cts = new CancellationTokenSource();
            // Not CancellationTokenSource(TimeSpan): its timer fires on the ThreadPool, which a full
            // parallel CI run starved past the whole ~3 s Test, so nothing was cancelled (5cb41cb).
            var canceller = new Thread(() =>
            {
                using var never = new ManualResetEventSlim();
                never.Wait(TimeSpan.FromMilliseconds(100));
                cts.Cancel();
            });
            var clock = System.Diagnostics.Stopwatch.StartNew();
            canceller.Start();

            Func<Task<ArchiveResult>> act = () => _sut.TestAsync([archive], cancellationToken: cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
            canceller.Join();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task TestAsync_ValidArchive_Passes()
    {
        ArchiveResult result = await _sut.TestAsync([FixtureHelper.Archive("valid_multiple_files.zip")]);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task TestAsync_CorruptedCrcArchive_Fails()
    {
        // Stored (uncompressed) entry, data byte flipped after write — reads back cleanly,
        // but no longer matches the CRC-32 declared in the entry header.
        ArchiveResult result = await _sut.TestAsync([FixtureHelper.Archive("corrupted_crc_stored.zip")]);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("CRC-32"));
    }

    [Fact]
    public async Task TestAsync_EncryptedArchive_ReturnsError()
    {
        ArchiveResult result = await _sut.TestAsync([FixtureHelper.Archive("encrypted_zipcrypto.zip")]);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("password-protected"));
    }

    // T-F167: same bit-0 general-purpose-flag check as ExtractAsync, now proven against a real
    // AES-256/WinZip AES fixture rather than only ZipCrypto's older method.
    [Fact]
    public async Task TestAsync_EncryptedAes256Archive_ReturnsError()
    {
        ArchiveResult result = await _sut.TestAsync([FixtureHelper.Archive("encrypted_aes256.zip")]);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("password-protected"));
    }

    [Fact]
    public async Task TestAsync_RandomBinaryFile_ReportsErrorAsUnrecognizedFormat()
    {
        // T-F117: TestAsync shares ExtractAsync's IsZipFile/GetKnownArchiveReason gate — bytes
        // matching no known archive signature must surface as a real error, not a silent no-op.
        using var temp = new TempDirectory();
        string binaryPath = Path.Combine(temp.Path, "data.bin");
        File.WriteAllBytes(binaryPath, [0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x11]);

        ArchiveResult result = await _sut.TestAsync([binaryPath]);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.SourcePath == binaryPath
            && e.Message == "File is not a recognized archive format and cannot be tested.");
        result.SkippedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task TestAsync_MultipleArchives_OneCorruptedOneValid_ReportsOnlyTheCorruptedOne()
    {
        ArchiveResult result = await _sut.TestAsync([
            FixtureHelper.Archive("valid_single_file.zip"),
            FixtureHelper.Archive("corrupted_crc_stored.zip"),
        ]);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.SourcePath.EndsWith("corrupted_crc_stored.zip"));
    }
}
