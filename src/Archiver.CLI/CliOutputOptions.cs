using System.Globalization;

namespace Archiver.CLI;

/// <summary>Where one kind of output goes: 7-Zip's -bso/-bse/-bsp{0|1|2}.</summary>
public enum CliOutputTarget
{
    Off = 0,
    StdOut = 1,
    StdErr = 2,
}

/// <summary>
/// T-F408: 7-Zip's output switches, which apply to every command (ArchiveCommandLine.cpp:
/// kDisableHeaders "ba", kDisablePercents "bd", kShowTime "bt", kLogLevel "bb", kOutStream "bso",
/// kErrStream "bse", kPercentStream "bsp"). A stream left null was not given.
/// </summary>
public sealed record CliOutputOptions
{
    public uint LogLevel { get; init; }              // -bb[N]: from 1, a "- <name>" line per file x writes
    public bool DisablePercents { get; init; }       // -bd
    public bool ShowTime { get; init; }              // -bt
    public bool DisableHeaders { get; init; }        // -ba: l prints entry rows only
    public CliOutputTarget? OutStream { get; init; }       // -bso: messages and results
    public CliOutputTarget? ErrStream { get; init; }       // -bse: errors and warnings
    public CliOutputTarget? PercentStream { get; init; }   // -bsp: the percentage

    /// <summary>
    /// Where messages and errors go, given the process's own stdout and stderr writers. Under -so
    /// stdout carries the data, so messages default to nowhere there, as in 7-Zip (StdOutMode).
    /// </summary>
    public (TextWriter Messages, TextWriter Errors) Writers(TextWriter stdout, TextWriter stderr, bool stdoutCarriesData)
    {
        CliOutputTarget messages = OutStream ?? (stdoutCarriesData ? CliOutputTarget.Off : CliOutputTarget.StdOut);
        return (Pick(messages), Pick(ErrStream ?? CliOutputTarget.StdErr));

        TextWriter Pick(CliOutputTarget target) => target switch
        {
            CliOutputTarget.StdOut => stdout,
            CliOutputTarget.StdErr => stderr,
            _ => TextWriter.Null,
        };
    }

    /// <summary>
    /// Takes the output switches out of <paramref name="switches"/> (the arguments before "--"),
    /// leaving the rest for the command's own parser. As in 7-Zip, the last of each wins.
    /// </summary>
    public static bool TryTake(ref string[] switches, out CliOutputOptions options, out string? error)
    {
        options = new CliOutputOptions();
        error = null;
        var remaining = new List<string>(switches.Length);
        foreach (string token in switches)
        {
            (CliOutputOptions? taken, error) = Apply(token, options);
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

    // Null options and no error: not an output switch, left for the command's parser to refuse.
    private static (CliOutputOptions? Options, string? Error) Apply(string token, CliOutputOptions options) => token switch
    {
        "-ba" => (options with { DisableHeaders = true }, null),
        "-bd" => (options with { DisablePercents = true }, null),
        "-bt" => (options with { ShowTime = true }, null),
        _ when token.StartsWith("-bb", StringComparison.Ordinal) => ParseLogLevel(token[3..], options),
        _ when token.StartsWith("-bso", StringComparison.Ordinal) => ParseStream(token, options, (o, s) => o with { OutStream = s }),
        _ when token.StartsWith("-bse", StringComparison.Ordinal) => ParseStream(token, options, (o, s) => o with { ErrStream = s }),
        _ when token.StartsWith("-bsp", StringComparison.Ordinal) => ParseStream(token, options, (o, s) => o with { PercentStream = s }),
        _ => (null, null),
    };

    // 7-Zip: an empty postfix is level 1; otherwise StringToUInt32, any value it takes.
    private static (CliOutputOptions? Options, string? Error) ParseLogLevel(string value, CliOutputOptions options)
    {
        if (value.Length == 0)
            return (options with { LogLevel = 1 }, null);
        return value.All(char.IsAsciiDigit) && uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint level)
            ? (options with { LogLevel = level }, null)
            : (null, $"Unsupported switch postfix -bb: '{value}' (expected a number, e.g. -bb1)");
    }

    private static (CliOutputOptions? Options, string? Error) ParseStream(
        string token, CliOutputOptions options, Func<CliOutputOptions, CliOutputTarget, CliOutputOptions> set)
    {
        string name = token[..4];
        return token.Length == 5 && token[4] is >= '0' and <= '2'
            ? (set(options, (CliOutputTarget)(token[4] - '0')), null)
            : (null, $"{name} takes one of 0, 1 or 2 (off, stdout, stderr), e.g. {name}2");
    }
}
