namespace Archiver.CLI;

/// <summary>
/// T-F410: one name given by -i, -x, -ai or -ax (or, with <see cref="IsListFile"/>, a list file of
/// names). <see cref="Recursion"/> and <see cref="WildcardMatching"/> are null when the switch did
/// not say, so -r and -spd decide (each switch starts from them, as in 7-Zip).
/// </summary>
public sealed record CliWildcardSwitch(
    string Text, bool Include, bool IsListFile, CliRecursion? Recursion = null, bool? WildcardMatching = null);

/// <summary>A name after the archive name: a mask, or with <see cref="IsListFile"/> a list file ("@name").</summary>
public sealed record CliFilterName(string Text, bool IsListFile);

/// <summary>
/// T-F410: 7-Zip's name and archive filter switches (ArchiveCommandLine.cpp: kInclude "i",
/// kExclude "x", kRecursed "r", kDisableWildcardParsing "spd", kCaseSensitive "ssc",
/// kListfileCharSet "scs", kArInclude "ai", kArExclude "ax", kNoArName "an"). Taken out before the
/// command's parser, like the output switches; only x, l and t use them.
/// </summary>
public sealed record CliNameSwitches
{
    public IReadOnlyList<CliWildcardSwitch> Names { get; init; } = [];     // -i, -x, in order
    public IReadOnlyList<CliWildcardSwitch> Archives { get; init; } = [];  // -ai, -ax, in order
    public CliRecursion Recursion { get; init; }                           // -r, -r-, -r0
    public bool WildcardMatching { get; init; } = true;                     // -spd turns it off
    public bool CaseSensitive { get; init; }                                // -ssc; -ssc- off
    public string? ListFileCharset { get; init; }                           // -scs{UTF-8|UTF-16LE|UTF-16BE|WIN|DOS}
    public bool NoArchiveName { get; init; }                                // -an
    public bool ExcludeFolders { get; init; }                               // -xtd
    public bool ExcludeFiles { get; init; }                                 // -xtf

    /// <summary>Whether any switch that filters names inside an archive was given.</summary>
    public bool FiltersNames => Names.Count > 0 || ExcludeFolders || ExcludeFiles || Recursion != CliRecursion.None;

    /// <summary>Whether any switch that picks archives on disk was given.</summary>
    public bool PicksArchives => Archives.Count > 0 || NoArchiveName;

    /// <summary>
    /// Takes these switches out of <paramref name="switches"/> (the arguments before "--"), leaving
    /// the rest for the command's own parser. The last -r, -spd, -ssc, -scs wins, as in 7-Zip.
    /// </summary>
    public static bool TryTake(ref string[] switches, out CliNameSwitches options, out string? error)
    {
        options = new CliNameSwitches();
        error = null;
        var remaining = new List<string>(switches.Length);
        foreach (string token in switches)
        {
            (CliNameSwitches? taken, error) = Apply(token, options);
            if (error is not null)
                return false;
            if (taken is null)
                remaining.Add(token);
            else
                options = taken;
        }

        switches = [.. remaining];
        return true;
    }

    // Null options and no error: not one of these switches. "-rr" (recovery data) and "-ao" stay
    // with 'a' and 'x'; only the exact spellings below are taken.
    private static (CliNameSwitches? Options, string? Error) Apply(string token, CliNameSwitches options) => token switch
    {
        "-r" => (options with { Recursion = CliRecursion.Recursive }, null),
        "-r-" => (options with { Recursion = CliRecursion.None }, null),
        "-r0" => (options with { Recursion = CliRecursion.WildcardOnly }, null),
        "-spd" => (options with { WildcardMatching = false }, null),
        "-ssc" => (options with { CaseSensitive = true }, null),
        "-ssc-" => (options with { CaseSensitive = false }, null),
        "-an" => (options with { NoArchiveName = true }, null),
        _ when token.Equals("-xtd", StringComparison.OrdinalIgnoreCase) => (options with { ExcludeFolders = true }, null),
        _ when token.Equals("-xtf", StringComparison.OrdinalIgnoreCase) => (options with { ExcludeFiles = true }, null),
        _ when token.StartsWith("-scs", StringComparison.Ordinal) => ParseListFileCharset(token[4..], options),
        _ when token.StartsWith("-ai", StringComparison.Ordinal) => Add(token, 3, include: true, archive: true, options),
        _ when token.StartsWith("-ax", StringComparison.Ordinal) => Add(token, 3, include: false, archive: true, options),
        _ when token.StartsWith("-i", StringComparison.Ordinal) => Add(token, 2, include: true, archive: false, options),
        _ when token.StartsWith("-x", StringComparison.Ordinal) => Add(token, 2, include: false, archive: false, options),
        _ => (null, null),
    };

    private static readonly string[] ListFileCharsets = ["UTF-8", "UTF-16LE", "UTF-16BE", "WIN", "DOS"];

    private static (CliNameSwitches? Options, string? Error) ParseListFileCharset(string name, CliNameSwitches options) =>
        Array.Find(ListFileCharsets, c => c.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } charset
            ? (options with { ListFileCharset = charset }, null)
            : (null, $"-scs takes the charset of a list file: UTF-8, UTF-16LE, UTF-16BE, WIN or DOS (e.g. -scsUTF-8), not '{name}'");

    private static (CliNameSwitches? Options, string? Error) Add(
        string token, int switchLength, bool include, bool archive, CliNameSwitches options)
    {
        (CliWildcardSwitch? parsed, string? error) = ParseWildcardSwitch(token, switchLength, include);
        if (parsed is null)
            return (null, error);
        return archive
            ? (options with { Archives = [.. options.Archives, parsed] }, null)
            : (options with { Names = [.. options.Names, parsed] }, null);
    }

    // ArchiveCommandLine.cpp, AddSwitchWildcardsToCensor: modifiers r[-|0] and w[-], each at most
    // once, then '!' and a name or '@' and a list file. 7-Zip's m (mark mode) and # (a memory map
    // another program shares) have no use here and are refused.
    private static (CliWildcardSwitch? Switch, string? Error) ParseWildcardSwitch(string token, int switchLength, bool include)
    {
        string name = token[..switchLength];
        CliRecursion? recursion = null;
        bool? wildcardMatching = null;
        int pos = switchLength;
        while (pos < token.Length)
        {
            char c = char.ToLowerInvariant(token[pos]);
            if (c == 'r' && recursion is null)
            {
                pos++;
                (recursion, pos) = pos < token.Length && token[pos] is '-' or '0'
                    ? (token[pos] == '-' ? CliRecursion.None : CliRecursion.WildcardOnly, pos + 1)
                    : (CliRecursion.Recursive, pos);
            }
            else if (c == 'w' && wildcardMatching is null)
            {
                pos++;
                (wildcardMatching, pos) = pos < token.Length && token[pos] == '-' ? (false, pos + 1) : (true, pos);
            }
            else
            {
                break;
            }
        }

        if (pos >= token.Length - 1)
            return (null, $"{name} needs '!' and a name or '@' and a list file, e.g. {name}r!*.tmp (got '{token}')");
        string tail = token[(pos + 1)..];
        return token[pos] switch
        {
            '!' => (new CliWildcardSwitch(tail, include, IsListFile: false, recursion, wildcardMatching), null),
            '@' => (new CliWildcardSwitch(tail, include, IsListFile: true, recursion, wildcardMatching), null),
            'm' or 'M' => (null, $"not supported by Pakko: {name} mark mode 'm' (strict file names); see 'pakko --help'"),
            _ => (null, $"Incorrect wildcard type marker in '{token}': {name} takes r, r-, r0, w, w- and then '!' or '@'"),
        };
    }
}
