using System.Globalization;
using Archiver.Core.Models;

namespace Archiver.CLI;

/// <summary>
/// T-F221 item 9: a percentage on stderr while a long operation runs, rewritten in place — only when
/// stderr is a console, so scripts and pipes never see it (the program creates none otherwise).
/// Cleared before any prompt or result line is written.
/// </summary>
public sealed class CliProgress(TextWriter writer) : IProgress<ProgressReport>
{
    private readonly Lock _lock = new();
    private int _shown = -1;

    /// <summary>Null when stderr is redirected: no progress at all.</summary>
    public static CliProgress? ForConsole() => Console.IsErrorRedirected ? null : new CliProgress(Console.Error);

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
