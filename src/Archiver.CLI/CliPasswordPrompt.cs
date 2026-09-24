using Archiver.Core.Services;

namespace Archiver.CLI;

/// <summary>
/// Masked password input for Archiver.CLI's interactive terminal prompt (T-F191). Reads via an
/// injected key source (<c>Console.ReadKey(intercept: true)</c> in real use) rather than calling
/// Console directly, so the editing logic is unit-testable in-process — the Subprocess/ test
/// layer (T-F09) always redirects stdin for the built exe, making this loop otherwise unreachable
/// from an end-to-end test. Mirrors CliArgumentParser's own "extract the testable logic out of
/// Main" shape (see CLAUDE.md's console-frontend-testing hard constraint).
/// </summary>
public static class CliPasswordPrompt
{
    /// <summary>
    /// Reads a masked password one key at a time. Enter submits; Backspace deletes one character
    /// (a no-op on an empty buffer); Escape or Ctrl+C cancels (returns null); a key with no printable
    /// character (arrows, function keys, etc. — <see cref="ConsoleKeyInfo.KeyChar"/> is '\0') is
    /// ignored rather than appended. <paramref name="echo"/>, when given, is called once per
    /// accepted keystroke — '*' for an appended character, '\b' for a deleted one — so the caller
    /// can render the mask without this class touching Console output directly.
    /// </summary>
    public static string? Read(Func<ConsoleKeyInfo> readKey, Action<char>? echo = null) =>
        CliLineInput.Read(readKey, echo, mask: true);

    /// <summary>
    /// Outcome of <see cref="ReadNewPassword"/>: exactly one of <see cref="Password"/> (accepted),
    /// <see cref="Error"/> (refused — a mismatch or <see cref="EncryptionPasswordRule"/>), or neither
    /// (<see cref="Cancelled"/> by Escape/Ctrl+C).
    /// </summary>
    public sealed record NewPasswordResult(string? Password, string? Error)
    {
        /// <summary>True when the user pressed Escape or Ctrl+C at either prompt.</summary>
        public bool Cancelled => Password is null && Error is null;
    }

    /// <summary>
    /// T-F193: asks for a new encryption password twice, like 7z's bare <c>-p</c> on 'a'. The first
    /// entry is checked against <see cref="EncryptionPasswordRule"/> before the second is asked for.
    /// Never re-asks after a refusal — the caller reports the error and stops, as 7z does.
    /// <paramref name="write"/> receives the prompt text and line breaks.
    /// </summary>
    public static NewPasswordResult ReadNewPassword(
        Func<ConsoleKeyInfo> readKey, Action<string> write, Action<char>? echo = null)
    {
        write("Enter password (will not be echoed): ");
        string? first = Read(readKey, echo);
        write(Environment.NewLine);
        if (first is null)
            return new NewPasswordResult(null, null);

        string? problem = DescribeEncryptProblem(EncryptionPasswordRule.Check(first));
        if (problem is not null)
            return new NewPasswordResult(null, problem);

        write("Reenter password: ");
        string? second = Read(readKey, echo);
        write(Environment.NewLine);
        if (second is null)
            return new NewPasswordResult(null, null);

        return second == first
            ? new NewPasswordResult(first, null)
            : new NewPasswordResult(null, "the passwords do not match");
    }

    /// <summary>English text for an <see cref="EncryptionPasswordRule.Check"/> refusal; null for <see cref="EncryptionPasswordProblem.None"/>.</summary>
    public static string? DescribeEncryptProblem(EncryptionPasswordProblem problem) => problem switch
    {
        EncryptionPasswordProblem.None => null,
        EncryptionPasswordProblem.Empty => "the password is empty",
        EncryptionPasswordProblem.UnsupportedCharacters =>
            "the password may contain only English letters, digits, spaces and ASCII punctuation — "
            + "7-Zip cannot open an archive protected by other characters",
        EncryptionPasswordProblem.TooLong =>
            $"the password is longer than {EncryptionPasswordRule.MaxLength} characters, the most 7-Zip accepts for an AES-encrypted ZIP",
        _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, null),
    };
}
