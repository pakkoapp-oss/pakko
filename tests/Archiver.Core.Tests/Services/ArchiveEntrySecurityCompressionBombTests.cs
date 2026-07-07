using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// Unit tests for ArchiveEntrySecurity.EvaluateCompressionBombAsync (T-F94) — pure decision
/// logic, availableFreeSpaceBytes injected as a plain long so the insufficient-space branch
/// doesn't need a real full/near-full disk. See DECISIONS.md's T-F94 entry.
/// </summary>
public sealed class ArchiveEntrySecurityCompressionBombTests
{
    private const long AmpleSpace = long.MaxValue / 2;

    [Fact]
    public async Task EvaluateCompressionBombAsync_RatioUnderThreshold_ReturnsNotABomb()
    {
        var outcome = await ArchiveEntrySecurity.EvaluateCompressionBombAsync(
            "archive.zip", declaredUncompressedSize: 500, compressedSize: 100,
            availableFreeSpaceBytes: AmpleSpace, confirmCallback: null);

        outcome.Should().Be(CompressionBombOutcome.NotABomb);
    }

    [Fact]
    public async Task EvaluateCompressionBombAsync_ZeroCompressedSize_ReturnsNotABomb()
    {
        var outcome = await ArchiveEntrySecurity.EvaluateCompressionBombAsync(
            "archive.zip", declaredUncompressedSize: 500, compressedSize: 0,
            availableFreeSpaceBytes: AmpleSpace, confirmCallback: null);

        outcome.Should().Be(CompressionBombOutcome.NotABomb);
    }

    [Fact]
    public async Task EvaluateCompressionBombAsync_RatioOverThreshold_InsufficientSpace_ReturnsInsufficientDiskSpace()
    {
        var outcome = await ArchiveEntrySecurity.EvaluateCompressionBombAsync(
            "archive.zip", declaredUncompressedSize: 2_000_000, compressedSize: 100,
            availableFreeSpaceBytes: 1_000_000, confirmCallback: _ => Task.FromResult(true));

        outcome.Should().Be(CompressionBombOutcome.InsufficientDiskSpace);
    }

    [Fact]
    public async Task EvaluateCompressionBombAsync_RatioOverThreshold_NullCallback_ReturnsUserDeclined()
    {
        var outcome = await ArchiveEntrySecurity.EvaluateCompressionBombAsync(
            "archive.zip", declaredUncompressedSize: 2_000_000, compressedSize: 100,
            availableFreeSpaceBytes: AmpleSpace, confirmCallback: null);

        outcome.Should().Be(CompressionBombOutcome.UserDeclined);
    }

    [Fact]
    public async Task EvaluateCompressionBombAsync_RatioOverThreshold_CallbackReturnsFalse_ReturnsUserDeclined()
    {
        var outcome = await ArchiveEntrySecurity.EvaluateCompressionBombAsync(
            "archive.zip", declaredUncompressedSize: 2_000_000, compressedSize: 100,
            availableFreeSpaceBytes: AmpleSpace, confirmCallback: _ => Task.FromResult(false));

        outcome.Should().Be(CompressionBombOutcome.UserDeclined);
    }

    [Fact]
    public async Task EvaluateCompressionBombAsync_RatioOverThreshold_CallbackReturnsTrue_ReturnsUserConfirmed()
    {
        var outcome = await ArchiveEntrySecurity.EvaluateCompressionBombAsync(
            "archive.zip", declaredUncompressedSize: 2_000_000, compressedSize: 100,
            availableFreeSpaceBytes: AmpleSpace, confirmCallback: _ => Task.FromResult(true));

        outcome.Should().Be(CompressionBombOutcome.UserConfirmed);
    }

    [Fact]
    public async Task EvaluateCompressionBombAsync_CallbackReceivesCorrectWarningDetails()
    {
        CompressionBombWarning? captured = null;

        await ArchiveEntrySecurity.EvaluateCompressionBombAsync(
            "bomb.tar.gz", declaredUncompressedSize: 5_000_000, compressedSize: 1_000,
            availableFreeSpaceBytes: AmpleSpace,
            confirmCallback: w => { captured = w; return Task.FromResult(true); });

        captured.Should().NotBeNull();
        captured!.ArchivePath.Should().Be("bomb.tar.gz");
        captured.DeclaredUncompressedSize.Should().Be(5_000_000);
        captured.CompressedSize.Should().Be(1_000);
        captured.Ratio.Should().Be(5_000);
    }
}
