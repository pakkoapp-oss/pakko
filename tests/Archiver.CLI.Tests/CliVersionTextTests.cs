using Archiver.CLI;
using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F222: a release prints exactly its tag; anything else must never look like a release.
public sealed class CliVersionTextTests
{
    [Theory]
    [InlineData("1.5.0+0e379cc825f3b0e685e8c48a65363a8426461a7d", "pakko 1.5.0")]
    [InlineData("1.5.0", "pakko 1.5.0")]
    [InlineData("0.0.0-dev+0e379cc825f3b0e685e8c48a65363a8426461a7d", "pakko 0.0.0-dev+0e379cc")]
    [InlineData("0.0.0-dev", "pakko 0.0.0-dev")]
    [InlineData("1.6.0-rc.1+abc", "pakko 1.6.0-rc.1+abc")]
    [InlineData(null, "pakko 0.0.0")]
    [InlineData("", "pakko 0.0.0")]
    public void Format_ReleaseDropsMetadata_PrereleaseKeepsShortSha(string? informationalVersion, string expected)
    {
        CliVersionText.Format(informationalVersion).Should().Be(expected);
    }

    // T-F317: the copy inside the MSIX (the "pakko" alias) names its package, so with both the
    // Store alias and the winget/zip copy on PATH, -v shows which one ran.
    [Theory]
    [InlineData("pakko 1.7.0", "PavloRybchenko.Pakko_1.7.0.0_x64__8wekyb3d8bbwe", "pakko 1.7.0 (package PavloRybchenko.Pakko_1.7.0.0_x64__8wekyb3d8bbwe)")]
    [InlineData("pakko 0.0.0-dev+0e379cc", "PavloRybchenko.Pakko_1.6.0.9_x64__abc", "pakko 0.0.0-dev+0e379cc (package PavloRybchenko.Pakko_1.6.0.9_x64__abc)")]
    [InlineData("pakko 1.7.0", null, "pakko 1.7.0")]
    [InlineData("pakko 1.7.0", "", "pakko 1.7.0")]
    public void WithPackage_AppendsThePackageFullNameOnlyWhenPackaged(string line, string? packageFullName, string expected)
    {
        CliVersionText.WithPackage(line, packageFullName).Should().Be(expected);
    }
}
