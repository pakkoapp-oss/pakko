using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F200: previewing a second file of an encrypted archive asked for the password again.
public sealed class SessionPasswordMemoryTests
{
    private const string Archive = @"C:\a\enc.zip";

    private int _prompts;

    private Func<PasswordPromptInfo, Task<PasswordDecision>> Prompt(string? answer) => _ =>
    {
        _prompts++;
        return Task.FromResult(new PasswordDecision { Password = answer });
    };

    private static PasswordPromptInfo First => new() { ArchiveName = "enc.zip", Purpose = PasswordPurpose.Decrypt };

    private static PasswordPromptInfo Retry => new()
    {
        ArchiveName = "enc.zip", Purpose = PasswordPurpose.Decrypt, AttemptNumber = 2, PreviousAttemptWasWrong = true,
    };

    private static ArchiveResult Clean => new();

    private static ArchiveResult Rejected => new()
    {
        Errors = [CoreMessages.Error(Archive, MessageCode.PasswordProtectedExtract)],
    };

    [Fact]
    public async Task AcceptedPassword_SecondOperationDoesNotPrompt()
    {
        var memory = new SessionPasswordMemory();
        (await memory.Wrap(Archive, Prompt("secret"))(First)).Password.Should().Be("secret");
        memory.Complete(Archive, Clean);

        PasswordDecision second = await memory.Wrap(Archive, Prompt("other"))(First);

        second.Password.Should().Be("secret");
        _prompts.Should().Be(1);
    }

    [Fact]
    public async Task RejectedResult_IsNotRemembered()
    {
        var memory = new SessionPasswordMemory();
        await memory.Wrap(Archive, Prompt("wrong"))(First);
        memory.Complete(Archive, Rejected);

        await memory.Wrap(Archive, Prompt("again"))(First);

        _prompts.Should().Be(2);
    }

    [Fact]
    public async Task RememberedPasswordRejectedByCore_IsForgottenAndUserAsked()
    {
        var memory = new SessionPasswordMemory();
        await memory.Wrap(Archive, Prompt("secret"))(First);
        memory.Complete(Archive, Clean);

        Func<PasswordPromptInfo, Task<PasswordDecision>> resolver = memory.Wrap(Archive, Prompt("new"));
        await resolver(First);
        PasswordDecision retry = await resolver(Retry);

        retry.Password.Should().Be("new");
        _prompts.Should().Be(2);
    }

    [Fact]
    public async Task CancelledPrompt_RemembersNothing()
    {
        var memory = new SessionPasswordMemory();
        await memory.Wrap(Archive, Prompt(null))(First);
        memory.Complete(Archive, Clean);

        await memory.Wrap(Archive, Prompt("x"))(First);

        _prompts.Should().Be(2);
    }

    [Fact]
    public async Task OtherArchive_IsAskedSeparately()
    {
        var memory = new SessionPasswordMemory();
        await memory.Wrap(Archive, Prompt("secret"))(First);
        memory.Complete(Archive, Clean);

        await memory.Wrap(@"C:\a\other.zip", Prompt("x"))(First);

        _prompts.Should().Be(2);
    }

    [Fact]
    public async Task Clear_ForgetsEverything()
    {
        var memory = new SessionPasswordMemory();
        await memory.Wrap(Archive, Prompt("secret"))(First);
        memory.Complete(Archive, Clean);

        memory.Clear();
        await memory.Wrap(Archive, Prompt("x"))(First);

        _prompts.Should().Be(2);
    }

    [Fact]
    public async Task CompleteForAnotherArchive_DoesNotRememberThePendingPassword()
    {
        var memory = new SessionPasswordMemory();
        await memory.Wrap(Archive, Prompt("secret"))(First);
        memory.Complete(@"C:\a\other.zip", Clean);

        await memory.Wrap(Archive, Prompt("x"))(First);

        _prompts.Should().Be(2);
    }

    [Fact]
    public void WasPasswordRejected_WrongPasswordSkip_True()
    {
        var result = new ArchiveResult { SkippedFiles = [CoreMessages.Skip("a.txt", MessageCode.EntryWrongPassword, "a.txt")] };

        SessionPasswordMemory.WasPasswordRejected(result).Should().BeTrue();
    }

    [Fact]
    public void WasPasswordRejected_OtherError_False()
    {
        var result = new ArchiveResult { Errors = [CoreMessages.Error(Archive, MessageCode.SourceNotFound, Archive)] };

        SessionPasswordMemory.WasPasswordRejected(result).Should().BeFalse();
    }
}
