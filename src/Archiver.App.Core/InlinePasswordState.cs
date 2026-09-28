using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.App.Core;

/// <summary>What stops an inline encryption password from being used.</summary>
public enum InlinePasswordIssue
{
    /// <summary>The password can be used.</summary>
    None,

    /// <summary>Nothing typed yet.</summary>
    Empty,

    /// <summary>
    /// A character outside printable ASCII (<see cref="EncryptionPasswordRule"/>) was typed into a
    /// field since it was last empty. It never got in, but what is left is not what the user meant.
    /// </summary>
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
/// Core's <see cref="EncryptionPasswordRule"/>. Like a Windows PIN box, a field keeps only allowed
/// characters; a refused one blocks until that field is emptied, because in a Ukrainian layout the
/// ASCII left over in both fields would still match. Holds the text only while the window needs it: the
/// caller clears it after the operation, when encryption is turned off, when the format changes
/// and when the window closes; nothing is persisted or logged.
/// </summary>
public sealed class InlinePasswordState
{
    private readonly Field _password = new();
    private readonly Field _confirmation = new();

    /// <summary>The password as kept (allowed characters only).</summary>
    public string Password => _password.Text;

    /// <summary>Takes the password field's text; returns what the field should show.</summary>
    public string SetPassword(string value) => _password.Set(value);

    /// <summary>Takes the confirmation field's text; returns what the field should show.</summary>
    public string SetConfirmation(string value) => _confirmation.Set(value);

    /// <summary>The first problem: a refused character, then the rule's order, then the confirmation.</summary>
    public InlinePasswordIssue Issue => (_password.Refused || _confirmation.Refused) switch
    {
        true => InlinePasswordIssue.UnsupportedCharacters,
        false => IssueOfKeptText(),
    };

    private InlinePasswordIssue IssueOfKeptText() => EncryptionPasswordRule.Check(Password) switch
    {
        EncryptionPasswordProblem.Empty => InlinePasswordIssue.Empty,
        EncryptionPasswordProblem.UnsupportedCharacters => InlinePasswordIssue.UnsupportedCharacters,
        EncryptionPasswordProblem.TooLong => InlinePasswordIssue.TooLong,
        _ when _confirmation.Text.Length == 0 => InlinePasswordIssue.ConfirmationEmpty,
        _ when !string.Equals(Password, _confirmation.Text, StringComparison.Ordinal) => InlinePasswordIssue.Mismatch,
        _ => InlinePasswordIssue.None,
    };

    /// <summary>True when the password can be used.</summary>
    public bool CanProceed => Issue == InlinePasswordIssue.None;

    /// <summary>True when the issue deserves a visible message (not merely "keep typing").</summary>
    public bool ShowsError => Issue is InlinePasswordIssue.UnsupportedCharacters or InlinePasswordIssue.TooLong
        or InlinePasswordIssue.Mismatch;

    /// <summary>A refused letter suggests a non-English keyboard layout (the Ukrainian-layout trap).</summary>
    public bool LooksLikeWrongKeyboardLayout => _password.RefusedLetter || _confirmation.RefusedLetter;

    /// <summary>
    /// The resource key of the one line shown under the fields, or null while the user is still
    /// typing or the password is fine. The layout hint replaces the characters error it explains.
    /// </summary>
    public string? MessageKey => Issue switch
    {
        InlinePasswordIssue.UnsupportedCharacters when LooksLikeWrongKeyboardLayout => "EncryptPasswordLayoutHint",
        InlinePasswordIssue.UnsupportedCharacters => "EncryptPasswordRefusedCharacter",
        InlinePasswordIssue.TooLong => "EncryptPasswordErrorTooLong",
        InlinePasswordIssue.Mismatch => "EncryptPasswordErrorMismatch",
        _ => null,
    };

    /// <summary>
    /// True when the password is asked for: the box is ticked and the format is ZIP. A tar format
    /// hides the box without unticking it, and tar has no password.
    /// </summary>
    public static bool Applies(bool encryptChecked, ArchiveContainerFormat format) =>
        encryptChecked && format == ArchiveContainerFormat.Zip;

    /// <summary>False while an applicable password is not yet usable.</summary>
    public bool AllowsCompress(bool encryptChecked, ArchiveContainerFormat format) =>
        !Applies(encryptChecked, format) || CanProceed;

    /// <summary>Forgets both fields.</summary>
    public void Clear()
    {
        _password.Reset();
        _confirmation.Reset();
    }

    private sealed class Field
    {
        public string Text { get; private set; } = string.Empty;
        public bool Refused { get; private set; }
        public bool RefusedLetter { get; private set; }

        public void Reset()
        {
            Text = string.Empty;
            Refused = RefusedLetter = false;
        }

        public string Set(string value)
        {
            // The view writes the kept text back into the box, which reports it as a change.
            if (value == Text)
                return Text;
            if (value.Length == 0)
                Refused = RefusedLetter = false;
            foreach (char c in value.Where(c => !EncryptionPasswordRule.IsAllowed(c)))
            {
                Refused = true;
                RefusedLetter |= char.IsLetter(c);
            }
            Text = string.Concat(value.Where(EncryptionPasswordRule.IsAllowed));
            return Text;
        }
    }
}
