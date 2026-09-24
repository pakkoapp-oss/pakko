using System.Text;

namespace Archiver.CLI;

/// <summary>
/// Key-by-key line input shared by both interactive prompts (T-F191 password, T-F160 conflict).
/// The caller reads with <c>Console.TreatControlCAsInput = true</c>, so Ctrl+C arrives here as a
/// key and cancels — the reason this exists: a blocked <c>Console.ReadLine</c> never returns after
/// a Ctrl+C that a CancelKeyPress handler cancelled (confirmed on device, T-F160). Reads through an
/// injected key source so the editing logic is unit-testable.
/// </summary>
public static class CliLineInput
{
    /// <summary>
    /// Enter submits; Escape or Ctrl+C cancels (returns null); Backspace deletes one character (a
    /// no-op on an empty buffer); a key with no printable character is ignored. <paramref name="echo"/>
    /// receives each accepted keystroke — the character itself, or '*' when <paramref name="mask"/>
    /// is set — and '\b' for a deletion.
    /// </summary>
    public static string? Read(Func<ConsoleKeyInfo> readKey, Action<char>? echo, bool mask)
    {
        var buffer = new StringBuilder();

        while (true)
        {
            ConsoleKeyInfo key = readKey();

            if (key.Key == ConsoleKey.Enter)
                return buffer.ToString();

            if (key.Key == ConsoleKey.Escape || IsCtrlC(key))
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

            if (key.KeyChar == '\0' || char.IsControl(key.KeyChar))
                continue;

            buffer.Append(key.KeyChar);
            echo?.Invoke(mask ? '*' : key.KeyChar);
        }
    }

    private static bool IsCtrlC(ConsoleKeyInfo key) =>
        key.KeyChar == '\x03' || (key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0);
}
