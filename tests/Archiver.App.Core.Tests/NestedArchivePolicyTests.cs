using FluentAssertions;

namespace Archiver.App.Core.Tests;

public sealed class NestedArchivePolicyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void ExceedsMaxDepth_BelowLimit_ReturnsFalse(int currentDepth)
    {
        NestedArchivePolicy.ExceedsMaxDepth(currentDepth).Should().BeFalse();
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(100)]
    public void ExceedsMaxDepth_AtOrAboveLimit_ReturnsTrue(int currentDepth)
    {
        NestedArchivePolicy.ExceedsMaxDepth(currentDepth).Should().BeTrue();
    }

    [Fact]
    public void MaxDepth_Is4()
    {
        NestedArchivePolicy.MaxDepth.Should().Be(4);
    }
}
