using System.Text;

namespace Archiver.Core.Services;

/// <summary>
/// T-F418: an entry name as tar.exe's "-t" listing prints it, read back to the name itself.
/// bsdtar writes every name through safe_fprintf (safe_fputs from 3.8.9; tar/util.c, the same
/// bsdtar_expand_char table in 3.3.2, 3.5.2 and 3.8.x): '\' as "\\", BEL BS FF LF CR TAB VT as
/// "\a \b \f \n \r \t \v", and every other byte it finds unprintable as "\" and three octal
/// digits. Those bytes are in the listing's code page, so a run of them is decoded together.
/// Whether a character is escaped is the C runtime's choice (TAB comes out raw on Windows), so
/// only the grammar is read here, never a list of characters.
/// </summary>
internal static class TarListingName
{
    /// <summary>
    /// The name <paramref name="listed"/> stands for, with octal bytes decoded through
    /// <paramref name="encoding"/> (the encoding tar.exe's output was read in); null when the line
    /// holds an escape bsdtar never writes.
    /// </summary>
    public static string? Unescape(string listed, Encoding encoding)
    {
        if (!listed.Contains('\\'))
            return listed;

        var name = new StringBuilder(listed.Length);
        var bytes = new List<byte>();
        int i = 0;
        while (i < listed.Length)
        {
            if (listed[i] != '\\')
            {
                FlushBytes(name, bytes, encoding);
                name.Append(listed[i]);
                i++;
                continue;
            }

            if (i + 1 >= listed.Length)
                return null;
            char next = listed[i + 1];
            if (OctalByte(listed, i + 1) is { } octal)
            {
                bytes.Add(octal);
                i += 4;
                continue;
            }

            char? control = Control(next);
            if (control is null)
                return null;
            FlushBytes(name, bytes, encoding);
            name.Append(control.Value);
            i += 2;
        }
        FlushBytes(name, bytes, encoding);
        return name.ToString();
    }

    private static char? Control(char letter) => letter switch
    {
        '\\' => '\\',
        'a' => '\a',
        'b' => '\b',
        'f' => '\f',
        'n' => '\n',
        'r' => '\r',
        't' => '\t',
        'v' => '\v',
        _ => null,
    };

    // Three octal digits from start, at most 377 (bsdtar prints 0xFF & c).
    private static byte? OctalByte(string text, int start)
    {
        if (start + 3 > text.Length)
            return null;
        int value = 0;
        for (int k = start; k < start + 3; k++)
        {
            char digit = text[k];
            if (digit is < '0' or > '7')
                return null;
            value = (value * 8) + (digit - '0');
        }
        return value <= byte.MaxValue ? (byte)value : null;
    }

    private static void FlushBytes(StringBuilder name, List<byte> bytes, Encoding encoding)
    {
        if (bytes.Count == 0)
            return;
        name.Append(encoding.GetString([.. bytes]));
        bytes.Clear();
    }
}
