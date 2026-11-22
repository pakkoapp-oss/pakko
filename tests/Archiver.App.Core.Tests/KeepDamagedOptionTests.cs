using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F496: the extract card's "keep what a damaged file holds" checkbox.
public sealed class KeepDamagedOptionTests
{
    [Fact]
    public void ExtractionOffered_NoPolicy_Shown()
    {
        KeepDamagedOption.Offered(extractOffered: true, new GroupPolicyOptions()).Should().BeTrue();
    }

    [Fact]
    public void NoExtraction_Hidden()
    {
        KeepDamagedOption.Offered(extractOffered: false, new GroupPolicyOptions()).Should().BeFalse();
    }

    [Fact]
    public void PolicyForbids_Hidden()
    {
        KeepDamagedOption.Offered(true, new GroupPolicyOptions { DisableKeepDamagedFiles = true }).Should().BeFalse();
    }

    [Fact]
    public void Checked_Requested()
    {
        KeepDamagedOption.Requested(offered: true, userChecked: true, openingOneFile: false).Should().BeTrue();
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void HiddenUncheckedOrOpeningOneFile_NotRequested(bool offered, bool userChecked, bool openingOneFile)
    {
        KeepDamagedOption.Requested(offered, userChecked, openingOneFile).Should().BeFalse();
    }
}
