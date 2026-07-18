using System.IO.Compression;
using Archiver.CLI;
using FluentAssertions;

namespace Archiver.CLI.Tests;

public sealed class CliCompressionLevelMapperTests
{
    [Fact]
    public void Zero_MapsToNoCompression()
    {
        CliCompressionLevelMapper.TryMap(0).Should().Be(CompressionLevel.NoCompression);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OneOrTwo_MapsToFastest(int mx)
    {
        CliCompressionLevelMapper.TryMap(mx).Should().Be(CompressionLevel.Fastest);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    public void ThreeToSixBoundaries_MapToOptimal(int mx)
    {
        CliCompressionLevelMapper.TryMap(mx).Should().Be(CompressionLevel.Optimal);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(9)]
    public void SevenToNineBoundaries_MapToSmallestSize(int mx)
    {
        CliCompressionLevelMapper.TryMap(mx).Should().Be(CompressionLevel.SmallestSize);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public void OutOfRange_ReturnsNull(int mx)
    {
        CliCompressionLevelMapper.TryMap(mx).Should().BeNull();
    }
}
