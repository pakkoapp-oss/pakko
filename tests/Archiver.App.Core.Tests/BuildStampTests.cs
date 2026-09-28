using System.Globalization;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F218 / T-F198 item 4.
public sealed class BuildStampTests
{
    [Fact]
    public void SideloadBuild_ShowsTheCompileTimeInLocalTime()
    {
        var utc = new DateTime(2026, 9, 28, 10, 15, 30, DateTimeKind.Utc);
        string expected = utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        BuildStamp.Title(utc.ToString("o"), isStoreBuild: false).Should().Be($"Pakko — build {expected}");
    }

    [Fact]
    public void StoreBuild_ShowsNoStamp()
    {
        BuildStamp.Title("2026-09-28T10:15:30.0000000Z", isStoreBuild: true).Should().Be("Pakko");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a date")]
    public void MissingOrBadMetadata_ShowsNoStamp(string? value)
    {
        BuildStamp.Title(value, isStoreBuild: false).Should().Be("Pakko");
    }

    [Fact]
    public void Read_AssemblyWithoutTheKey_ReturnsNull()
    {
        BuildStamp.Read(typeof(BuildStampTests).Assembly).Should().BeNull();
    }
}
