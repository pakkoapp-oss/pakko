using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.Shell.Tests;

public sealed class StickyApplyToAllConflictResolverTests
{
    private static ConflictInfo Conflict(string path = "C:\\dest\\file.txt") => new() { ExistingPath = path };

    [Fact]
    public async Task ResolveAsync_FirstCall_AlwaysInvokesInner()
    {
        int innerCalls = 0;
        var resolver = new StickyApplyToAllConflictResolver(_ =>
        {
            innerCalls++;
            return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Skip, ApplyToAll = false });
        });

        await resolver.ResolveAsync(Conflict());

        innerCalls.Should().Be(1);
    }

    [Fact]
    public async Task ResolveAsync_ApplyToAllFalse_InvokesInnerAgainOnNextCall()
    {
        int innerCalls = 0;
        var resolver = new StickyApplyToAllConflictResolver(_ =>
        {
            innerCalls++;
            return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Skip, ApplyToAll = false });
        });

        await resolver.ResolveAsync(Conflict());
        await resolver.ResolveAsync(Conflict());

        innerCalls.Should().Be(2);
    }

    [Fact]
    public async Task ResolveAsync_ApplyToAllTrue_ShortCircuitsEverySubsequentCallWithoutInvokingInnerAgain()
    {
        int innerCalls = 0;
        var resolver = new StickyApplyToAllConflictResolver(_ =>
        {
            innerCalls++;
            return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Overwrite, ApplyToAll = true });
        });

        var first = await resolver.ResolveAsync(Conflict("a.txt"));
        var second = await resolver.ResolveAsync(Conflict("b.txt"));
        var third = await resolver.ResolveAsync(Conflict("c.txt"));

        innerCalls.Should().Be(1);
        second.Should().BeEquivalentTo(first);
        third.Should().BeEquivalentTo(first);
        second.Resolution.Should().Be(ConflictResolution.Overwrite);
    }
}
