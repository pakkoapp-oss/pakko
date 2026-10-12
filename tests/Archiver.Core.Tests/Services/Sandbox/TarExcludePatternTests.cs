using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

/// <summary>
/// T-F391: the pattern text. That tar.exe reads it as meant is checked against the real tar.exe
/// in TarSandboxedServiceNestedLinkTests.
/// </summary>
public sealed class TarExcludePatternTests
{
    // --- Happy path ---

    [Fact]
    public void For_NestedPath_IsAnchoredAndUsesForwardSlashes() =>
        TarExcludePattern.For("src", @"sub\link").Should().Be("^src/sub/link");

    // --- Security & Boundary: nothing in a name is left as pattern syntax ---

    [Theory]
    [InlineData("br[ack]et", @"^src/br\[ack\]et")]
    [InlineData("cash$", @"^src/cash\$")]
    [InlineData("^caret", @"^src/\^caret")]
    [InlineData("a*b?c", @"^src/a\*b\?c")]
    public void For_PatternSyntaxInAName_IsEscaped(string name, string expected) =>
        TarExcludePattern.For("src", name).Should().Be(expected);

    [Fact]
    public void For_PatternSyntaxInTheRootName_IsEscaped() =>
        TarExcludePattern.For("[root]", "link").Should().Be(@"^\[root\]/link");

    // --- Misuse: names that look like options or the list's keyword ---

    [Theory]
    [InlineData("-C", "link", "^-C/link")]
    [InlineData("src", "--exclude=x", "^src/--exclude=x")]
    public void For_OptionLikeNames_StayBehindTheAnchor(string root, string relative, string expected) =>
        TarExcludePattern.For(root, relative).Should().Be(expected);

    // --- Error path: a name outside ASCII is passed through for the caller's encoding check ---

    [Fact]
    public void For_NonAsciiName_IsUnchanged() =>
        TarExcludePattern.For("src", "caf\u00E9").Should().Be("^src/caf\u00E9");
}
