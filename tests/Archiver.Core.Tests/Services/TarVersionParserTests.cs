using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// Unit tests for TarVersionParser (T-F48) — mocked tar.exe --version output, no process launch.
/// </summary>
public sealed class TarVersionParserTests
{
    [Fact]
    public void Parse_PreWindows11_23H2Version_SupportsCoreFormatsOnlyNotRarOr7zOrZstd()
    {
        // Even if this hypothetical older build happened to link libzstd, TESTING.md documents
        // zstd (like RAR/7z) as requiring Win 11 23H2+ tar.exe — version-gated, not token-gated.
        const string output = "bsdtar 3.5.2 - libarchive 3.5.2 zlib/1.2.11.zlib-ng liblzma/5.2.5 bz2lib/1.0.6 libzstd/1.4.5";

        var result = TarVersionParser.Parse(output);

        result.Version.Should().Be("3.5.2");
        result.SupportsXz.Should().BeTrue();
        result.SupportsLzma.Should().BeTrue();
        result.SupportsBz2.Should().BeTrue();
        result.SupportsZstd.Should().BeFalse();
        result.Supports7z.Should().BeFalse();
        result.SupportsRar.Should().BeFalse();
    }

    [Fact]
    public void Parse_Windows11_23H2Version_SupportsRarAnd7zAndZstd()
    {
        const string output = "bsdtar 3.7.2 - libarchive 3.7.2 zlib/1.2.11.zlib-ng liblzma/5.2.5 bz2lib/1.0.6 libzstd/1.5.2";

        var result = TarVersionParser.Parse(output);

        result.Version.Should().Be("3.7.2");
        result.SupportsZstd.Should().BeTrue();
        result.Supports7z.Should().BeTrue();
        result.SupportsRar.Should().BeTrue();
    }

    [Fact]
    public void Parse_ExactMinimumVersion_SupportsRarAnd7zAndZstd()
    {
        const string output = "bsdtar 3.7.0 - libarchive 3.7.0";

        var result = TarVersionParser.Parse(output);

        result.Supports7z.Should().BeTrue();
        result.SupportsRar.Should().BeTrue();
        result.SupportsZstd.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tar.exe: command not found")]
    [InlineData("bsdtar - no version info")]
    public void Parse_UnrecognizedOrEmptyOutput_ReturnsAllUnsupportedDefaults(string output)
    {
        var result = TarVersionParser.Parse(output);

        result.Should().Be(new Archiver.Core.Models.TarCapabilities());
    }
}
