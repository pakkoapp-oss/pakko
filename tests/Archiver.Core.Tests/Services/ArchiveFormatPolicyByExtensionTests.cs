using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F212: the App enables Extract only when a listed item is an archive that may be opened — the
// same policy and tar.exe rules as the router, decided from the extension (no disk I/O).
public sealed class ArchiveFormatPolicyByExtensionTests
{
    private static readonly TarCapabilities AllTar = new()
    {
        SupportsRar = true, Supports7z = true, SupportsZstd = true, SupportsXz = true, SupportsBz2 = true, SupportsLzma = true,
    };

    [Theory]
    [InlineData("a.zip")]
    [InlineData("a.tar.gz")]
    [InlineData("a.7z")]
    public void SupportedArchive_True(string path) =>
        ArchiveFormatPolicy.CanOpenByExtension(path, AllTar, new GroupPolicyOptions()).Should().BeTrue();

    [Theory]
    [InlineData("a.txt")]
    [InlineData("folder")]
    [InlineData("")]
    public void NotAnArchive_False(string path) =>
        ArchiveFormatPolicy.CanOpenByExtension(path, AllTar, new GroupPolicyOptions()).Should().BeFalse();

    [Fact]
    public void BlockedByPolicy_False() =>
        ArchiveFormatPolicy.CanOpenByExtension("a.rar", AllTar, new GroupPolicyOptions { BlockedFormats = ["rar"] })
            .Should().BeFalse();

    [Fact]
    public void TarDisabledByPolicy_ZipStillTrue()
    {
        var policy = new GroupPolicyOptions { DisableTarExtraction = true };

        ArchiveFormatPolicy.CanOpenByExtension("a.tar.gz", AllTar, policy).Should().BeFalse();
        ArchiveFormatPolicy.CanOpenByExtension("a.zip", AllTar, policy).Should().BeTrue();
    }

    [Fact]
    public void TarCannotReadRar_False() =>
        ArchiveFormatPolicy.CanOpenByExtension("a.rar", AllTar with { SupportsRar = false }, new GroupPolicyOptions())
            .Should().BeFalse();
}
