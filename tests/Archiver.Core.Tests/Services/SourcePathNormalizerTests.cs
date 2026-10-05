using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>T-F338/T-F153: the form of a source path the creation engines name entries from.</summary>
public sealed class SourcePathNormalizerTests
{
    [Theory]
    [InlineData(@"C:\data\folder", @"C:\data\folder")]
    [InlineData(@"C:\data\folder\", @"C:\data\folder")]
    [InlineData(@"C:\data\folder\.", @"C:\data\folder")]
    [InlineData(@"C:\data\folder\sub\..", @"C:\data\folder")]
    [InlineData(@"C:\data\folder\.\", @"C:\data\folder")]
    [InlineData(@"C:\", @"C:\")] // T-F99: a drive root keeps its separator
    [InlineData(@"C:\.", @"C:\")]
    [InlineData(@"\\server\share\folder\", @"\\server\share\folder")]
    public void Normalize_FullyQualifiedPath(string path, string expected)
    {
        SourcePathNormalizer.Normalize(path).Should().Be(expected);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(@"sub\file.txt")]
    public void Normalize_RelativePath_IsResolvedAgainstTheCurrentDirectory(string path)
    {
        SourcePathNormalizer.Normalize(path).Should().Be(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
    }

    // A name only reachable with the \\?\ prefix ends in a dot; resolving it would drop the dot.
    [Fact]
    public void Normalize_FullyQualifiedOrdinaryName_IsNotResolved()
    {
        SourcePathNormalizer.Normalize(@"\\?\C:\data\name.").Should().Be(@"\\?\C:\data\name.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("a\0b")]
    public void Normalize_PathWindowsCannotResolve_IsLeftAsTyped(string path)
    {
        SourcePathNormalizer.Normalize(path).Should().Be(path);
    }
}
