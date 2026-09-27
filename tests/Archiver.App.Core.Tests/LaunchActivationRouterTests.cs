using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

public sealed class LaunchActivationRouterTests
{
    [Fact]
    public void Decide_BrowseWithOneFile_ReturnsBrowse()
    {
        string args = LaunchArguments.Format(LaunchOperation.Browse, [@"\\сервер\спільна\архів.zip"]);

        LaunchActivationDecision? decision = LaunchActivationRouter.Decide(args);

        decision.Should().NotBeNull();
        decision!.Mode.Should().Be(FileActivationMode.Browse);
        decision.BrowsePath.Should().Be(@"\\сервер\спільна\архів.zip");
    }

    [Fact]
    public void Decide_BrowseWithTwoFiles_AddsBothToList()
    {
        string args = LaunchArguments.Format(LaunchOperation.Browse, [@"C:\a.zip", @"C:\b.zip"]);

        LaunchActivationDecision? decision = LaunchActivationRouter.Decide(args);

        decision!.Mode.Should().Be(FileActivationMode.AddToList);
        decision.BrowsePath.Should().BeNull();
        decision.Paths.Should().Equal(@"C:\a.zip", @"C:\b.zip");
    }

    [Theory]
    [InlineData(LaunchOperation.Extract)]
    [InlineData(LaunchOperation.Archive)]
    public void Decide_ExtractOrArchive_AddsToList(LaunchOperation operation)
    {
        string args = LaunchArguments.Format(operation, [@"C:\a.zip"]);

        LaunchActivationDecision? decision = LaunchActivationRouter.Decide(args);

        decision!.Mode.Should().Be(FileActivationMode.AddToList);
        decision.Paths.Should().Equal(@"C:\a.zip");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("--browse")]
    [InlineData("pakko://browse?files=WyJDOlxcYS56aXAiXQ==")]
    public void Decide_PlainLaunchOrUnrecognized_ReturnsNull(string? args)
    {
        LaunchActivationRouter.Decide(args).Should().BeNull();
    }
}
