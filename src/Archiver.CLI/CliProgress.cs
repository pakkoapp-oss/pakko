using System.Globalization;
using Archiver.Core.Models;

namespace Archiver.CLI;

/// <summary>
/// T-F221 item 9: a percentage on stderr while a long operation runs, rewritten in place — only when
/// stderr is a console, so scripts and pipes never see it (the program creates none otherwise).
/// Cleared before any prompt or result line is written.
/// </summary>
public sealed class CliProgress(TextWriter writer) : IProgress<ProgressReport>, IDisposable
{
    private readonly Lock _lock = new();
    private int _shown = -1;
    private TextWriter? _owned;

    /// <summary>
    /// T-F408: the percentage on the stream -bsp names (stderr by default), written to the process's
    /// own handle so -bso/-bse do not move it. Null when -bd or -bsp0 turn it off, when that stream
    /// is not a console, or when it is stdout and stdout carries -so's data.
    /// </summary>
    public static CliProgress? For(CliOutputOptions output, bool stdoutCarriesData)
    {
        if (output.DisablePercents)
            return null;
        // No -bse or -bsp moved anything: Console.Error is still stderr, as before T-F408.
        if (output.ErrStream is null && output.PercentStream is null or CliOutputTarget.StdErr)
            return Console.IsErrorRedirected ? null : new CliProgress(Console.Error);
        Stream? stream = (output.PercentStream ?? CliOutputTarget.StdErr) switch
        {
            CliOutputTarget.StdErr when !Console.IsErrorRedirected => Console.OpenStandardError(),
            CliOutputTarget.StdOut when !Console.IsOutputRedirected && !stdoutCarriesData => Console.OpenStandardOutput(),
            _ => null,
        };
        if (stream is null)
            return null;
        var owned = new StreamWriter(stream) { AutoFlush = true };
        return new CliProgress(owned) { _owned = owned };
    }

    public void Dispose() => _owned?.Dispose();

    public void Report(ProgressReport value)
    {
        int percent = Math.Clamp(value.Percent, 0, 100);
        lock (_lock)
        {
            if (percent == _shown)
                return;
            _shown = percent;
            writer.Write(string.Create(CultureInfo.InvariantCulture, $"\r{percent,3}%"));
        }
    }

    /// <summary>Removes the percentage so the next line starts clean.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            if (_shown < 0)
                return;
            writer.Write("\r    \r");
            _shown = -1;
        }
    }
}
