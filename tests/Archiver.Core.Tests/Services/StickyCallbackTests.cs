using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F160: replaces Archiver.Shell's StickyApplyToAllConflictResolver (T-F155) and
// StickyPasswordResolver (T-F192) — the same scenarios those two classes' own tests covered,
// now against the one shared helper all three frontends-with-per-call-scope use (Shell's
// per-archive loop, CLI's zip/tar router split).
public sealed class StickyCallbackTests
{
    private static ConflictInfo Conflict(string path = "a.txt") => new() { ExistingPath = path };

    private static StickyCallback<ConflictInfo, ConflictDecision> Conflicts(
        Func<ConflictInfo, Task<ConflictDecision>> inner) => new(inner, d => d.ApplyToAll);

    [Fact]
    public async Task ResolveAsync_FirstCall_AlwaysInvokesInner()
    {
        int calls = 0;
        var sticky = Conflicts(_ => { calls++; return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Skip }); });

        await sticky.ResolveAsync(Conflict());

        calls.Should().Be(1);
    }

    [Fact]
    public async Task ResolveAsync_NonStickyDecision_InvokesInnerAgainOnNextCall()
    {
        int calls = 0;
        var sticky = Conflicts(_ => { calls++; return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Overwrite }); });

        await sticky.ResolveAsync(Conflict("a.txt"));
        await sticky.ResolveAsync(Conflict("b.txt"));

        calls.Should().Be(2);
    }

    [Fact]
    public async Task ResolveAsync_StickyDecision_ShortCircuitsEverySubsequentCallWithTheSameDecision()
    {
        int calls = 0;
        var sticky = Conflicts(_ =>
        {
            calls++;
            return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Rename, ApplyToAll = true });
        });

        var first = await sticky.ResolveAsync(Conflict("a.txt"));
        var second = await sticky.ResolveAsync(Conflict("b.txt"));
        var third = await sticky.ResolveAsync(Conflict("c.txt"));

        calls.Should().Be(1);
        second.Should().BeSameAs(first);
        third.Should().BeSameAs(first);
    }

    [Fact]
    public async Task ResolveAsync_PasswordDecisions_StickOnApplyToRemaining()
    {
        int calls = 0;
        var sticky = new StickyCallback<PasswordPromptInfo, PasswordDecision>(
            _ => { calls++; return Task.FromResult(new PasswordDecision { Password = "pw", ApplyToRemaining = true }); },
            d => d.ApplyToRemaining);
        var info = new PasswordPromptInfo { ArchiveName = "a.zip", Purpose = PasswordPurpose.Decrypt };

        (await sticky.ResolveAsync(info)).Password.Should().Be("pw");
        (await sticky.ResolveAsync(info with { ArchiveName = "b.zip" })).Password.Should().Be("pw");

        calls.Should().Be(1);
    }

    [Fact]
    public async Task ResolveAsync_SeparateInstances_DoNotShareStickyState()
    {
        int calls = 0;
        Func<ConflictInfo, Task<ConflictDecision>> inner = _ =>
        {
            calls++;
            return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Skip, ApplyToAll = true });
        };

        await Conflicts(inner).ResolveAsync(Conflict());
        await Conflicts(inner).ResolveAsync(Conflict());

        calls.Should().Be(2);
    }
}
