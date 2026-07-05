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
    private readonly ZipArchiveService _sut = new();

    [Fact]
    public async Task TestAsync_ValidArchive_Passes()
    {
        var result = await _sut.TestAsync([FixtureHelper.Archive("valid_multiple_files.zip")]);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task TestAsync_CorruptedCrcArchive_Fails()
    {
        // Stored (uncompressed) entry, data byte flipped after write — reads back cleanly,
        // but no longer matches the CRC-32 declared in the entry header.
        var result = await _sut.TestAsync([FixtureHelper.Archive("corrupted_crc_stored.zip")]);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("CRC-32"));
    }

    [Fact]
    public async Task TestAsync_EncryptedArchive_ReturnsError()
    {
        var result = await _sut.TestAsync([FixtureHelper.Archive("encrypted_zipcrypto.zip")]);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("password-protected"));
    }

    [Fact]
    public async Task TestAsync_MultipleArchives_OneCorruptedOneValid_ReportsOnlyTheCorruptedOne()
    {
        var result = await _sut.TestAsync([
            FixtureHelper.Archive("valid_single_file.zip"),
            FixtureHelper.Archive("corrupted_crc_stored.zip"),
        ]);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.SourcePath.EndsWith("corrupted_crc_stored.zip"));
    }
}
