using Archiver.Core.Models;
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

    // One line under the fields: a Cyrillic password is both unsupported and a layout slip, and
    // the layout hint is the one that tells the user what to do.
    [Theory]
    [InlineData("Ыускуе1", "Ыускуе1", "EncryptPasswordLayoutHint")]
    [InlineData("pass—word", "pass—word", "EncryptPasswordErrorCharacters")]
    [InlineData("Secret1", "Secret2", "EncryptPasswordErrorMismatch")]
    public void MessageKey_OneLinePerShownIssue(string password, string confirmation, string key)
    {
        With(password, confirmation).MessageKey.Should().Be(key);
    }

    [Fact]
    public void MessageKey_TooLong()
    {
        string longPassword = new('a', 100);

        With(longPassword, longPassword).MessageKey.Should().Be("EncryptPasswordErrorTooLong");
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("Secret1", "")]
    [InlineData("Secret1", "Secret1")]
    public void MessageKey_NoneWhileTypingOrValid(string password, string confirmation)
    {
        With(password, confirmation).MessageKey.Should().BeNull();
    }

    [Fact]
    public void Applies_OnlyToZipWithTheBoxTicked()
    {
        InlinePasswordState.Applies(encryptChecked: true, ArchiveContainerFormat.Zip).Should().BeTrue();
        InlinePasswordState.Applies(encryptChecked: false, ArchiveContainerFormat.Zip).Should().BeFalse();
        InlinePasswordState.Applies(encryptChecked: true, ArchiveContainerFormat.TarGz).Should().BeFalse();
    }

    [Fact]
    public void AllowsCompress_BlocksZipUntilThePasswordIsValid()
    {
        var state = new InlinePasswordState();

        state.AllowsCompress(encryptChecked: true, ArchiveContainerFormat.Zip).Should().BeFalse();
        state.AllowsCompress(encryptChecked: false, ArchiveContainerFormat.Zip).Should().BeTrue();

        state.SetPassword("Secret1");
        state.SetConfirmation("Secret1");
        state.AllowsCompress(encryptChecked: true, ArchiveContainerFormat.Zip).Should().BeTrue();
    }

    // The checkbox stays ticked, only hidden, when a tar format is picked; tar has no password.
    [Fact]
    public void AllowsCompress_TarWithTheHiddenBoxTicked()
    {
        new InlinePasswordState().AllowsCompress(encryptChecked: true, ArchiveContainerFormat.Tar).Should().BeTrue();
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
