using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

public sealed class PasswordResolverTests
{
    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_CorrectPasswordFirstAttempt_ReturnsItWithoutRetrying()
    {
        int callCount = 0;
        var sut = new PasswordResolver(_ =>
        {
            callCount++;
            return Task.FromResult(new PasswordDecision { Password = "correct" });
        }, maxAttempts: 3);

        string? result = await sut.ResolveAsync("a.zip", PasswordPurpose.Decrypt, pwd => pwd == "correct");

        result.Should().Be("correct");
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task ResolveAsync_WrongThenCorrect_RetriesAndSetsPreviousAttemptWasWrongOnSecondPrompt()
    {
        var promptedInfos = new List<PasswordPromptInfo>();
        var sut = new PasswordResolver(info =>
        {
            promptedInfos.Add(info);
            string pwd = info.AttemptNumber == 1 ? "wrong" : "correct";
            return Task.FromResult(new PasswordDecision { Password = pwd });
        }, maxAttempts: 3);

        string? result = await sut.ResolveAsync("a.zip", PasswordPurpose.Decrypt, pwd => pwd == "correct");

        result.Should().Be("correct");
        promptedInfos.Should().HaveCount(2);
        promptedInfos[0].PreviousAttemptWasWrong.Should().BeFalse();
        promptedInfos[0].AttemptNumber.Should().Be(1);
        promptedInfos[1].PreviousAttemptWasWrong.Should().BeTrue();
        promptedInfos[1].AttemptNumber.Should().Be(2);
    }

    [Fact]
    public async Task ResolveAsync_ApplyToRemaining_SuppressesFurtherPromptsAcrossCalls()
    {
        int callCount = 0;
        var sut = new PasswordResolver(_ =>
        {
            callCount++;
            return Task.FromResult(new PasswordDecision { Password = "correct", ApplyToRemaining = true });
        }, maxAttempts: 3);

        var first = await sut.ResolveAsync("a.zip", PasswordPurpose.Decrypt, pwd => pwd == "correct");
        var second = await sut.ResolveAsync("b.zip", PasswordPurpose.Decrypt, pwd => pwd == "correct");

        first.Should().Be("correct");
        second.Should().Be("correct");
        callCount.Should().Be(1);
    }

    // ── Security & Boundary ──────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_AllAttemptsWrong_ReturnsNullAfterExactlyMaxAttemptsPrompts()
    {
        int callCount = 0;
        var sut = new PasswordResolver(_ =>
        {
            callCount++;
            return Task.FromResult(new PasswordDecision { Password = "wrong" });
        }, maxAttempts: 3);

        string? result = await sut.ResolveAsync("a.zip", PasswordPurpose.Decrypt, _ => false);

        result.Should().BeNull();
        callCount.Should().Be(3);
    }

    [Fact]
    public async Task ResolveAsync_NoResolverWired_ReturnsNullWithoutInvokingVerify()
    {
        var sut = new PasswordResolver(resolvePasswordAsync: null, maxAttempts: 3);
        bool verifyCalled = false;

        string? result = await sut.ResolveAsync("a.zip", PasswordPurpose.Decrypt, _ => { verifyCalled = true; return true; });

        result.Should().BeNull();
        verifyCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_ApplyToRemainingFalse_PromptsAgainForNextArchive()
    {
        int callCount = 0;
        var sut = new PasswordResolver(_ =>
        {
            callCount++;
            return Task.FromResult(new PasswordDecision { Password = "correct", ApplyToRemaining = false });
        }, maxAttempts: 3);

        await sut.ResolveAsync("a.zip", PasswordPurpose.Decrypt, pwd => pwd == "correct");
        await sut.ResolveAsync("b.zip", PasswordPurpose.Decrypt, pwd => pwd == "correct");

        callCount.Should().Be(2);
    }

    // ── Misuse & Fool ────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_UserCancelsOnFirstAttempt_ReturnsNullWithoutFurtherPrompts()
    {
        int callCount = 0;
        var sut = new PasswordResolver(_ =>
        {
            callCount++;
            return Task.FromResult(new PasswordDecision { Password = null });
        }, maxAttempts: 3);

        string? result = await sut.ResolveAsync("a.zip", PasswordPurpose.Decrypt, _ => true);

        result.Should().BeNull();
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task ResolveAsync_UserCancelsAfterOneWrongAttempt_ReturnsNull()
    {
        int callCount = 0;
        var sut = new PasswordResolver(_ =>
        {
            callCount++;
            return Task.FromResult(callCount == 1
                ? new PasswordDecision { Password = "wrong" }
                : new PasswordDecision { Password = null });
        }, maxAttempts: 3);

        string? result = await sut.ResolveAsync("a.zip", PasswordPurpose.Decrypt, _ => false);

        result.Should().BeNull();
        callCount.Should().Be(2);
    }

    // ── Error path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_MaxAttemptsOne_PromptsExactlyOnceForEncryptDirection()
    {
        // Mirrors T-F193's future ArchiveAsync call site: no "wrong password" concept when
        // setting a new password, so maxAttempts=1 and verify always accepts.
        int callCount = 0;
        var sut = new PasswordResolver(info =>
        {
            callCount++;
            info.Purpose.Should().Be(PasswordPurpose.Encrypt);
            return Task.FromResult(new PasswordDecision { Password = "newpassword" });
        }, maxAttempts: 1);

        string? result = await sut.ResolveAsync("a.zip", PasswordPurpose.Encrypt, _ => true);

        result.Should().Be("newpassword");
        callCount.Should().Be(1);
    }
}
