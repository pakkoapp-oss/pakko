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
}
