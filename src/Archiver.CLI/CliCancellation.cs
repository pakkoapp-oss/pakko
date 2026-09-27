namespace Archiver.CLI;

/// <summary>
/// T-F244 item 4: Ctrl+C for one pakko command. The first Ctrl+C cancels <see cref="Token"/> so
/// the command stops cleanly (staging folders removed, exit code 255) instead of the process
/// being killed mid-copy; a second one lets Windows end the process, since a read blocked on a
/// stalled stdin pipe never looks at the token.
/// </summary>
public sealed class CliCancellation : IDisposable
{
    private readonly CancellationTokenSource _source = new();
    private readonly ConsoleCancelEventHandler? _handler;

    private CliCancellation(bool listenToConsole)
    {
        if (!listenToConsole)
            return;
        _handler = (_, e) => e.Cancel = HandleInterrupt();
        Console.CancelKeyPress += _handler;
    }

    /// <summary>Starts listening for Ctrl+C until disposed.</summary>
    public static CliCancellation ListenToConsole() => new(listenToConsole: true);

    /// <summary>For tests: a scope not attached to the console.</summary>
    public static CliCancellation Detached() => new(listenToConsole: false);

    /// <summary>Cancelled by the first Ctrl+C, or by <see cref="Source"/> (e.g. the conflict
    /// prompt's Quit).</summary>
    public CancellationToken Token => _source.Token;

    /// <summary>The underlying source, for callers that cancel on their own (Quit at a prompt).</summary>
    public CancellationTokenSource Source => _source;

    /// <summary>Handles one Ctrl+C: returns true (keep running) the first time and cancels the
    /// token; false once already cancelled, so the process ends.</summary>
    public bool HandleInterrupt()
    {
        if (_source.IsCancellationRequested)
            return false;
        _source.Cancel();
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_handler is not null)
            Console.CancelKeyPress -= _handler;
        _source.Dispose();
    }
}
