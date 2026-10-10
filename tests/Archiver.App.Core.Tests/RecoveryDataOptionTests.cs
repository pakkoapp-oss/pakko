using System.Globalization;
using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F275 step 2: the "New archive" card's recovery data option (5, 10 or 20 %, default 5).
public sealed class RecoveryDataOptionTests
{
    private static readonly GroupPolicyOptions NoPolicy = new();
    private static readonly GroupPolicyOptions Disabled = new() { DisableRecoveryData = true };

    [Fact]
    public void Choices_AreFiveTenTwenty_DefaultFive()
    {
        RecoveryDataOption.Percents.Should().Equal(5, 10, 20);
        RecoveryDataOption.Percents[RecoveryDataOption.DefaultIndex].Should().Be(5);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    public void Ticked_GivesTheChosenPercent(int index, int expected) =>
        RecoveryDataOption.PercentFor(NoPolicy, add: true, index).Should().Be(expected);

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Ticked_UnknownIndex_GivesTheDefault(int index) =>
        RecoveryDataOption.PercentFor(NoPolicy, add: true, index).Should().Be(5);

    [Fact]
    public void Unticked_GivesNone() =>
        RecoveryDataOption.PercentFor(NoPolicy, add: false, 2).Should().Be(0);

    [Fact]
    public void Policy_HidesTheOption_AndATickLeftOverGivesNone()
    {
        RecoveryDataOption.IsOffered(Disabled).Should().BeFalse();
        RecoveryDataOption.IsOffered(NoPolicy).Should().BeTrue();
        RecoveryDataOption.PercentFor(Disabled, add: true, 1).Should().Be(0);
    }

    [Theory]
    [InlineData("en-US", "5%")]
    [InlineData("tr-TR", "%5")]
    public void PercentText_FollowsTheCulture(string culture, string expected) =>
        RecoveryDataOption.PercentText(5, CultureInfo.GetCultureInfo(culture)).Should().Be(expected);
}
