using System.Text;

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
    /// (a no-op on an empty buffer); Escape cancels (returns null); a key with no printable
    /// character (arrows, function keys, etc. — <see cref="ConsoleKeyInfo.KeyChar"/> is '\0') is
    /// ignored rather than appended. <paramref name="echo"/>, when given, is called once per
    /// accepted keystroke — '*' for an appended character, '\b' for a deleted one — so the caller
    /// can render the mask without this class touching Console output directly.
    /// </summary>
    public static string? Read(Func<ConsoleKeyInfo> readKey, Action<char>? echo = null)
    {
        var buffer = new StringBuilder();

        while (true)
        {
            ConsoleKeyInfo key = readKey();

            if (key.Key == ConsoleKey.Enter)
                return buffer.ToString();

            if (key.Key == ConsoleKey.Escape)
                return null;

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                    echo?.Invoke('\b');
                }
                continue;
            }

            if (key.KeyChar == '\0')
                continue;

            buffer.Append(key.KeyChar);
            echo?.Invoke('*');
        }
    }
}
