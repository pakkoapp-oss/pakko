using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

public sealed class EncryptionPasswordRuleTests
{
    // ── Happy path ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Secret123")]
    [InlineData("with space and !@#$%^&*()_+-={}[]|\\:;\"'<>,.?/~`")]
    [InlineData(" ")]
    public void Check_PrintableAscii_IsAccepted(string password) =>
        EncryptionPasswordRule.Check(password).Should().Be(EncryptionPasswordProblem.None);

    // ── Security & Boundary ──────────────────────────────────────────────────

    [Fact]
    public void Check_LowestAndHighestAllowedCharacters_AreAccepted() =>
        EncryptionPasswordRule.Check(" \u007F").Should().Be(EncryptionPasswordProblem.None);

    [Theory]
    [InlineData("a\u001Fb")]
    [InlineData("a\u0080b")]
    [InlineData("\u0000")]
    [InlineData("tab\there")]
    public void Check_CharacterJustOutsideTheAsciiRange_IsRejected(string password) =>
        EncryptionPasswordRule.Check(password).Should().Be(EncryptionPasswordProblem.UnsupportedCharacters);

    [Fact]
    public void Check_ExactlyMaxLength_IsAccepted() =>
        EncryptionPasswordRule.Check(new string('a', EncryptionPasswordRule.MaxLength))
            .Should().Be(EncryptionPasswordProblem.None);

    [Fact]
    public void Check_OneOverMaxLength_IsRejected() =>
        EncryptionPasswordRule.Check(new string('a', EncryptionPasswordRule.MaxLength + 1))
            .Should().Be(EncryptionPasswordProblem.TooLong);

    [Fact]
    public void MaxLength_Matches7ZipWzAesLimit() =>
        EncryptionPasswordRule.MaxLength.Should().Be(99);

    // ── Misuse & Fool ────────────────────────────────────────────────────────

    [Fact]
    public void Check_Empty_IsRejected() =>
        EncryptionPasswordRule.Check("").Should().Be(EncryptionPasswordProblem.Empty);

    [Theory]
    [InlineData("пароль")]
    [InlineData("passwörd")]
    [InlineData("emoji🔒")]
    public void Check_NonAscii_IsRejected(string password) =>
        EncryptionPasswordRule.Check(password).Should().Be(EncryptionPasswordProblem.UnsupportedCharacters);

    [Fact]
    public void Check_TooLongAndNonAscii_ReportsCharactersFirst() =>
        EncryptionPasswordRule.Check(new string('ж', EncryptionPasswordRule.MaxLength + 1))
            .Should().Be(EncryptionPasswordProblem.UnsupportedCharacters);
}
