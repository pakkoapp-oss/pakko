using Archiver.Core.Services;

namespace Archiver.App.Core;

/// <summary>What stops an inline encryption password from being used.</summary>
public enum InlinePasswordIssue
{
    /// <summary>The password can be used.</summary>
    None,

    /// <summary>Nothing typed yet.</summary>
    Empty,

    /// <summary>A character outside printable ASCII (<see cref="EncryptionPasswordRule"/>).</summary>
    UnsupportedCharacters,

    /// <summary>Longer than <see cref="EncryptionPasswordRule.MaxLength"/>.</summary>
    TooLong,

    /// <summary>The confirmation field is still empty.</summary>
    ConfirmationEmpty,

    /// <summary>The two fields differ.</summary>
    Mismatch,
}

/// <summary>
/// T-F199: the encryption password typed inline under the checkbox, checked while typing with
/// Core's <see cref="EncryptionPasswordRule"/>. Holds the text only while the window needs it: the
/// caller clears it after the operation, when encryption is turned off, when the format changes
/// and when the window closes; nothing is persisted or logged.
/// </summary>
public sealed class InlinePasswordState
{
    private string _confirmation = string.Empty;

    /// <summary>The password as typed.</summary>
    public string Password { get; private set; } = string.Empty;

    /// <summary>Sets the password field's text.</summary>
    public void SetPassword(string value) => Password = value;

    /// <summary>Sets the confirmation field's text.</summary>
    public void SetConfirmation(string value) => _confirmation = value;

    /// <summary>The first problem, checked in the rule's order, then the confirmation.</summary>
    public InlinePasswordIssue Issue => EncryptionPasswordRule.Check(Password) switch
    {
        EncryptionPasswordProblem.Empty => InlinePasswordIssue.Empty,
        EncryptionPasswordProblem.UnsupportedCharacters => InlinePasswordIssue.UnsupportedCharacters,
        EncryptionPasswordProblem.TooLong => InlinePasswordIssue.TooLong,
        _ when _confirmation.Length == 0 => InlinePasswordIssue.ConfirmationEmpty,
        _ when !string.Equals(Password, _confirmation, StringComparison.Ordinal) => InlinePasswordIssue.Mismatch,
        _ => InlinePasswordIssue.None,
    };

    /// <summary>True when the password can be used.</summary>
    public bool CanProceed => Issue == InlinePasswordIssue.None;

    /// <summary>True when the issue deserves a visible message (not merely "keep typing").</summary>
    public bool ShowsError => Issue is InlinePasswordIssue.UnsupportedCharacters or InlinePasswordIssue.TooLong
        or InlinePasswordIssue.Mismatch;

    /// <summary>A non-ASCII letter suggests a non-English keyboard layout (the Ukrainian-layout trap).</summary>
    public bool LooksLikeWrongKeyboardLayout => Password.Any(c => c > 0x7F && char.IsLetter(c));

    /// <summary>Forgets both fields.</summary>
    public void Clear()
    {
        Password = string.Empty;
        _confirmation = string.Empty;
    }
}
