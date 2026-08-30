using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// Exercises TarSandboxedService.DetectCapabilitiesAsync's real process-invocation path (T-F48).
/// tar.exe ships with Windows 10 1803+/11, so this runs unconditionally rather than requiring
/// an [Integration] skip — unlike T-F49's format-specific extraction tests.
/// </summary>
public sealed class TarSandboxedServiceTests
{
    private readonly TarSandboxedService _sut = new();

    [Fact]
    public async Task DetectCapabilitiesAsync_RealTarExe_ReturnsParsedVersionWithoutThrowing()
    {
        var result = await _sut.DetectCapabilitiesAsync();

        result.Version.Should().NotBeNullOrEmpty();
    }

    // T-F182 (test-coverage audit): the public DetectCapabilitiesAsync() always points at the
    // hardcoded C:\Windows\System32\tar.exe const (CLAUDE.md's PATH-hijack hard constraint) — this
    // exercises the same real detection logic against a path that doesn't exist on disk, via the
    // internal test-only overload, to prove the documented "sensible all-false defaults if tar.exe
    // is absent" contract (ITarService's own XML doc) holds for actual absence, not just
    // unparseable --version output (already covered by TarVersionParserTests).
    [Fact]
    public async Task DetectCapabilitiesAsync_ExecutableMissingFromDisk_ReturnsAllFalseDefaultsNotThrow()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-tar.exe");
        File.Exists(missingPath).Should().BeFalse();

        var result = await TarSandboxedService.DetectCapabilitiesAsync(missingPath);

        result.Should().Be(new Archiver.Core.Models.TarCapabilities());
    }
}
