namespace Archiver.CLI;

/// <summary>
/// T-F411: switch names as 7-Zip reads them (CPP/Common/CommandLineParser.cpp at 26.02,
/// CParser::ParseString): the longest name in kSwitchForms that prefixes the token, compared
/// without case for ASCII letters only, and a switch that is not Multi given twice is
/// "Multiple instances for switch", exit 7. The name is written back in lower case so every
/// parser after this one compares it as before; what follows the name (a postfix letter, a value,
/// a password, a path) stays as typed.
/// </summary>
public static class CliSwitchNames
{
    // kSwitchForms of ArchiveCommandLine.cpp (26.02), without the leading '-'; "rr" is Pakko's own
    // (WinRAR's recovery-record spelling, T-F275). Multi: the switch may be given more than once.
    private static readonly (string Name, bool Multi)[] Forms =
    [
        ("?", false), ("h", false), ("-help", false),
        ("ba", false), ("bd", false), ("bt", false), ("bb", false),
        ("bso", false), ("bse", false), ("bsp", false),
        ("y", false), ("ad", false), ("ao", false),
        ("t", false), ("stx", true), ("m", true), ("o", false), ("w", false),
        ("i", true), ("x", true), ("ai", true), ("ax", true), ("an", false),
        ("u", true), ("v", true), ("r", false),
        ("stm", false), ("sfx", false), ("seml", false), ("scrc", true), ("shd", false), ("smemx", false),
        ("si", false), ("so", false),
        ("slp", false), ("scs", false), ("scc", false), ("slt", false), ("slf", false), ("slsl", false), ("slmu", false),
        ("ssp", false), ("ssw", false), ("sse", false), ("ssc", false), ("sa", false),
        ("spm", false), ("spd", false), ("spe", false), ("spf", false), ("spo", false),
        ("snh", false), ("snld", false), ("snl", false), ("sni", false),
        ("snoi", false), ("snon", false),
        ("snz", false), ("sns", false), ("snr", false), ("snc", false),
        ("snt", false),
        ("sdel", false), ("stl", false),
        ("p", false),
        ("rr", false),
    ];

    /// <summary>
    /// Writes each switch's name in lower case and refuses a second instance of a switch that is
    /// not Multi. A token whose name is not in the table is left as typed: the command parsers
    /// report it as unknown, as before.
    /// </summary>
    public static bool TryCanonicalize(string[] switchPart, out string[] canonical, out string? error)
    {
        canonical = new string[switchPart.Length];
        error = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < switchPart.Length; i++)
        {
            string token = switchPart[i];
            canonical[i] = token;
            if (token.Length < 2 || token[0] != '-' || FindForm(token) is not { } form)
                continue;

            (string name, bool multi) = form;
            canonical[i] = "-" + name + token[(1 + name.Length)..];
            if (!multi && !seen.Add(name))
            {
                error = $"Multiple instances for switch: {(name == "p" ? "-p" : token)}";
                return false;
            }
        }
        return true;
    }

    /// <summary>The ASCII letters A-Z of <paramref name="text"/> in lower case; every other
    /// character as it is, so a non-ASCII letter never matches an ASCII one.</summary>
    public static string ToLowerAscii(string text) =>
        string.Create(text.Length, text, static (span, source) =>
        {
            for (int i = 0; i < span.Length; i++)
                span[i] = source[i] is >= 'A' and <= 'Z' ? (char)(source[i] + ('a' - 'A')) : source[i];
        });

    // The longest form name that prefixes the token after its '-'.
    private static (string Name, bool Multi)? FindForm(string token)
    {
        (string Name, bool Multi)? best = null;
        foreach ((string name, bool multi) in Forms)
        {
            bool longer = best is null || name.Length > best.Value.Name.Length;
            if (longer && token.Length - 1 >= name.Length && PrefixedNoCaseAscii(token, 1, name))
                best = (name, multi);
        }
        return best;
    }

    private static bool PrefixedNoCaseAscii(string token, int start, string lowerName)
    {
        for (int i = 0; i < lowerName.Length; i++)
        {
            char c = token[start + i];
            char lower = c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
            if (lower != lowerName[i])
                return false;
        }
        return true;
    }
}
