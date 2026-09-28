using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F199: the encryption password moves from a modal that validated only after OK to two inline
// fields with live validation (a Ukrainian keyboard layout was the trap).
public sealed class InlinePasswordStateTests
{
    private static InlinePasswordState With(string password, string confirmation)
    {
        var state = new InlinePasswordState();
        state.SetPassword(password);
        state.SetConfirmation(confirmation);
        return state;
    }

    [Fact]
    public void Fresh_BlocksWithoutShowingAnError()
    {
        var state = new InlinePasswordState();

        state.Issue.Should().Be(InlinePasswordIssue.Empty);
        state.CanProceed.Should().BeFalse();
        state.ShowsError.Should().BeFalse();
    }

    [Fact]
    public void MatchingAsciiPassword_Proceeds()
    {
        InlinePasswordState state = With("Secret 1!", "Secret 1!");

        state.Issue.Should().Be(InlinePasswordIssue.None);
        state.CanProceed.Should().BeTrue();
        state.ShowsError.Should().BeFalse();
    }

    [Fact]
    public void ConfirmationNotTypedYet_BlocksQuietly()
    {
        InlinePasswordState state = With("Secret1", "");

        state.Issue.Should().Be(InlinePasswordIssue.ConfirmationEmpty);
        state.CanProceed.Should().BeFalse();
        state.ShowsError.Should().BeFalse();
    }

    [Fact]
    public void Mismatch_ShowsError()
    {
        InlinePasswordState state = With("Secret1", "Secret2");

        state.Issue.Should().Be(InlinePasswordIssue.Mismatch);
        state.ShowsError.Should().BeTrue();
    }

    [Fact]
    public void CyrillicLetters_UnsupportedWithLayoutHint()
    {
        InlinePasswordState state = With("Ыускуе1", "Ыускуе1");

        state.Issue.Should().Be(InlinePasswordIssue.UnsupportedCharacters);
        state.LooksLikeWrongKeyboardLayout.Should().BeTrue();
        state.ShowsError.Should().BeTrue();
        state.CanProceed.Should().BeFalse();
    }

    [Fact]
    public void NonLetterNonAscii_UnsupportedWithoutLayoutHint()
    {
        InlinePasswordState state = With("pass—word", "pass—word");

        state.Issue.Should().Be(InlinePasswordIssue.UnsupportedCharacters);
        state.LooksLikeWrongKeyboardLayout.Should().BeFalse();
    }

    [Fact]
    public void CharactersCheckedBeforeMismatch()
    {
        With("Ыуск", "other").Issue.Should().Be(InlinePasswordIssue.UnsupportedCharacters);
    }

    [Fact]
    public void TooLong_ShowsError()
    {
        string longPassword = new('a', 100);

        With(longPassword, longPassword).Issue.Should().Be(InlinePasswordIssue.TooLong);
    }

    [Fact]
    public void Clear_ForgetsBothFields()
    {
        InlinePasswordState state = With("Secret1", "Secret1");

        state.Clear();

        state.Password.Should().BeEmpty();
        state.Issue.Should().Be(InlinePasswordIssue.Empty);
        state.CanProceed.Should().BeFalse();
    }
}
