using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

public sealed class ConflictResolverTests
{
    [Theory]
    [InlineData(ConflictBehavior.Overwrite)]
    [InlineData(ConflictBehavior.Skip)]
    [InlineData(ConflictBehavior.Rename)]
    public async Task ResolveAsync_NonAsk_PassesThroughWithoutInvokingCallback(ConflictBehavior configured)
    {
        int callCount = 0;
        var sut = new ConflictResolver(configured, _ =>
        {
            callCount++;
            return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Skip });
        });

        var result = await sut.ResolveAsync(@"C:\file.txt");

        result.Should().Be(configured);
        callCount.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_AskWithNullCallback_DefaultsToSkip()
    {
        var sut = new ConflictResolver(ConflictBehavior.Ask, resolveConflictAsync: null);

        var result = await sut.ResolveAsync(@"C:\file.txt");

        result.Should().Be(ConflictBehavior.Skip);
    }

    [Theory]
    [InlineData(ConflictResolution.Skip, ConflictBehavior.Skip)]
    [InlineData(ConflictResolution.Overwrite, ConflictBehavior.Overwrite)]
    [InlineData(ConflictResolution.Rename, ConflictBehavior.Rename)]
    public async Task ResolveAsync_AskWithCallback_MapsEachResolution(
        ConflictResolution resolution, ConflictBehavior expected)
    {
        var sut = new ConflictResolver(ConflictBehavior.Ask,
            _ => Task.FromResult(new ConflictDecision { Resolution = resolution }));

        var result = await sut.ResolveAsync(@"C:\file.txt");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task ResolveAsync_ApplyToAllTrue_SuppressesFurtherCallbackInvocations()
    {
        int callCount = 0;
        var sut = new ConflictResolver(ConflictBehavior.Ask, _ =>
        {
            callCount++;
            return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Rename, ApplyToAll = true });
        });

        var first = await sut.ResolveAsync(@"C:\a.txt");
        var second = await sut.ResolveAsync(@"C:\b.txt");
        var third = await sut.ResolveAsync(@"C:\c.txt");

        first.Should().Be(ConflictBehavior.Rename);
        second.Should().Be(ConflictBehavior.Rename);
        third.Should().Be(ConflictBehavior.Rename);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task ResolveAsync_ApplyToAllFalse_InvokesCallbackEveryTime()
    {
        int callCount = 0;
        var sut = new ConflictResolver(ConflictBehavior.Ask, _ =>
        {
            callCount++;
            return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Skip, ApplyToAll = false });
        });

        await sut.ResolveAsync(@"C:\a.txt");
        await sut.ResolveAsync(@"C:\b.txt");

        callCount.Should().Be(2);
    }
}
