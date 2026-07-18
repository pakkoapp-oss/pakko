using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.Core.Tests.Models;

public sealed class GroupPolicyOptionsTests
{
    [Fact]
    public void IsFormatAllowed_NoListsSet_AllowsEverything()
    {
        new GroupPolicyOptions().IsFormatAllowed("zip").Should().BeTrue();
    }

    [Fact]
    public void IsFormatAllowed_InAllowedList_ReturnsTrue()
    {
        var options = new GroupPolicyOptions { AllowedFormats = new[] { "zip", "tar" } };

        options.IsFormatAllowed("zip").Should().BeTrue();
        options.IsFormatAllowed("rar").Should().BeFalse();
    }

    [Fact]
    public void IsFormatAllowed_InBlockedList_ReturnsFalse()
    {
        var options = new GroupPolicyOptions { BlockedFormats = new[] { "rar" } };

        options.IsFormatAllowed("rar").Should().BeFalse();
        options.IsFormatAllowed("zip").Should().BeTrue();
    }

    [Fact]
    public void IsFormatAllowed_BlockedTakesPrecedenceOverAllowed()
    {
        var options = new GroupPolicyOptions
        {
            AllowedFormats = new[] { "zip", "rar" },
            BlockedFormats = new[] { "rar" },
        };

        options.IsFormatAllowed("rar").Should().BeFalse();
        options.IsFormatAllowed("zip").Should().BeTrue();
    }

    [Fact]
    public void IsFormatAllowed_IsCaseInsensitive()
    {
        var options = new GroupPolicyOptions { AllowedFormats = new[] { "ZIP" } };

        options.IsFormatAllowed("zip").Should().BeTrue();
    }
}
