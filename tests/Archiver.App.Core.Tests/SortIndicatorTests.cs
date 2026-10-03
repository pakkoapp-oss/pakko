using FluentAssertions;

namespace Archiver.App.Core.Tests;

public sealed class SortIndicatorTests
{
    [Fact]
    public void SortedColumn_ShowsItsDirection()
    {
        SortIndicator.Glyph("Size", "Size", ascending: true).Should().Be(SortIndicator.Ascending);
        SortIndicator.Glyph("Size", "Size", ascending: false).Should().Be(SortIndicator.Descending);
    }

    [Fact]
    public void OtherColumns_AndNoSortYet_ShowNothing()
    {
        SortIndicator.Glyph("Name", "Size", ascending: true).Should().BeEmpty();
        SortIndicator.Glyph("Name", null, ascending: true).Should().BeEmpty();
    }

    [Fact]
    public void Glyphs_AreTheSegoeChevrons()
    {
        SortIndicator.Ascending.Should().Be(((char)0xE70E).ToString());
        SortIndicator.Descending.Should().Be(((char)0xE70D).ToString());
    }
}
