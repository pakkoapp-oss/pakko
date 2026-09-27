using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F232: past the command-line limit ActivateApplication blocks forever instead of failing, so the
// length guard is the only thing between an oversized Explorer selection and a hung Archiver.Shell.
public sealed class AppLauncherTests
{
    [Fact]
    public void TryBuildArguments_SmallSelection_ReturnsParsableArguments()
    {
        string[] files = [@"C:\a b\x.zip", @"\\сервер\спільна\y.tar"];

        bool ok = AppLauncher.TryBuildArguments(LaunchOperation.Extract, files, out string? arguments);

        ok.Should().BeTrue();
        LaunchArguments.TryParse(arguments, out LaunchOperation operation, out IReadOnlyList<string>? parsed).Should().BeTrue();
        operation.Should().Be(LaunchOperation.Extract);
        parsed.Should().Equal(files);
    }

    [Fact]
    public void TryBuildArguments_AtTheBoundary_AcceptsLongestFittingAndRefusesNextLonger()
    {
        // base64 grows in 4-character steps, so no path formats to exactly MaxLength; find the path
        // length n where the formatted string still fits and n + 1 no longer does.
        int n = LaunchArguments.MaxLength * 3 / 4 - 100;
        Length(n).Should().BeLessThanOrEqualTo(LaunchArguments.MaxLength);
        while (Length(n + 1) <= LaunchArguments.MaxLength)
            n++;

        AppLauncher.TryBuildArguments(LaunchOperation.Archive, Path(n), out string? fitting).Should().BeTrue();
        fitting.Length.Should().BeGreaterThan(LaunchArguments.MaxLength - 4);
        AppLauncher.TryBuildArguments(LaunchOperation.Archive, Path(n + 1), out string? over).Should().BeFalse();
        over.Length.Should().BeGreaterThan(LaunchArguments.MaxLength);
    }

    [Fact]
    public void TryBuildArguments_ThreeHundredLongCyrillicPaths_IsRefused()
    {
        string[] files = Enumerable.Range(0, 300).Select(i => $@"\\сервер\спільна\{new string('Ж', 100)}_{i}.zip").ToArray();

        AppLauncher.TryBuildArguments(LaunchOperation.Extract, files, out _).Should().BeFalse();
    }

    private static string[] Path(int length) => ["C:\\" + new string('a', length)];

    private static int Length(int pathLength) => LaunchArguments.Format(LaunchOperation.Archive, Path(pathLength)).Length;
}
