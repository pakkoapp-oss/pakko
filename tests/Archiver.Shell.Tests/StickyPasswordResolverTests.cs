using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.Shell.Tests;

public sealed class StickyPasswordResolverTests
{
    private static PasswordPromptInfo Info(string archiveName = "a.zip") =>
        new() { ArchiveName = archiveName, Purpose = PasswordPurpose.Decrypt };

    [Fact]
    public async Task ResolveAsync_FirstCall_AlwaysInvokesInner()
    {
        int innerCalls = 0;
        var resolver = new StickyPasswordResolver((_, _) =>
        {
            innerCalls++;
            return Task.FromResult(new PasswordDecision { Password = "pw", ApplyToRemaining = false });
        }, canApplyToRemaining: true);

        await resolver.ResolveAsync(Info());

        innerCalls.Should().Be(1);
    }

    [Fact]
    public async Task ResolveAsync_ApplyToRemainingFalse_InvokesInnerAgainOnNextCall()
    {
        int innerCalls = 0;
        var resolver = new StickyPasswordResolver((_, _) =>
        {
            innerCalls++;
            return Task.FromResult(new PasswordDecision { Password = "pw", ApplyToRemaining = false });
        }, canApplyToRemaining: true);

        await resolver.ResolveAsync(Info("a.zip"));
        await resolver.ResolveAsync(Info("b.zip"));

        innerCalls.Should().Be(2);
    }

    [Fact]
    public async Task ResolveAsync_ApplyToRemainingTrue_ShortCircuitsEverySubsequentCallWithoutInvokingInnerAgain()
    {
        int innerCalls = 0;
        var resolver = new StickyPasswordResolver((_, _) =>
        {
            innerCalls++;
            return Task.FromResult(new PasswordDecision { Password = "sticky-pw", ApplyToRemaining = true });
        }, canApplyToRemaining: true);

        var first = await resolver.ResolveAsync(Info("a.zip"));
        var second = await resolver.ResolveAsync(Info("b.zip"));
        var third = await resolver.ResolveAsync(Info("c.zip"));

        innerCalls.Should().Be(1);
        second.Should().BeEquivalentTo(first);
        third.Should().BeEquivalentTo(first);
        second.Password.Should().Be("sticky-pw");
    }

    [Fact]
    public async Task ResolveAsync_PassesCanApplyToRemainingThroughToInner()
    {
        bool? observed = null;
        var resolver = new StickyPasswordResolver((_, canApply) =>
        {
            observed = canApply;
            return Task.FromResult(new PasswordDecision { Password = "pw" });
        }, canApplyToRemaining: false);

        await resolver.ResolveAsync(Info());

        observed.Should().BeFalse();
    }
}
