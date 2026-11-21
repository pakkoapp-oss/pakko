namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// T-F561: one tar.exe run writes no more than the entries it extracts declare. libarchive prints 0
/// for an entry whose size comes only after its data (a ZIP written as a stream, inside a gzip), so
/// the compression-ratio check (T-F94) sees nothing; this watches what the run actually writes.
/// The job's own count (<see cref="SandboxJobObject.WrittenBytes"/>) holds files and pipes alike,
/// so the stderr Pakko has read is taken off it; the slack covers stderr still in the pipe.
/// Read every <see cref="Interval"/>, and once more after the run: a run can pass its limit by at
/// most what it writes in one interval before it is stopped.
/// </summary>
internal sealed class JobWriteLimit : IAsyncDisposable
{
    internal const long PipeSlackBytes = 1L << 20;
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    private readonly SandboxJobObject _job;
    private readonly ByteCounter _stdErrRead;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _watch;
    private volatile bool _over;
    private InvalidOperationException? _unreadable;

    private JobWriteLimit(SandboxJobObject job, long declaredBytes, ByteCounter stdErrRead)
    {
        _job = job;
        DeclaredBytes = declaredBytes;
        _stdErrRead = stdErrRead;
        _watch = WatchAsync();
    }

    /// <summary>Starts watching <paramref name="job"/> before its process is launched.</summary>
    public static JobWriteLimit Start(SandboxJobObject job, long declaredBytes, ByteCounter stdErrRead) =>
        new(job, declaredBytes, stdErrRead);

    public long DeclaredBytes { get; }

    /// <summary>True when what the run wrote, less the stderr read, is past the declared bytes and the slack.</summary>
    internal static bool WroteMoreThanDeclared(long written, long stdErrRead, long declaredBytes) =>
        written - stdErrRead > (declaredBytes > long.MaxValue - PipeSlackBytes ? long.MaxValue : declaredBytes + PipeSlackBytes);

    /// <summary>
    /// After the run has ended: whether it wrote more than declared, by the last reading too.
    /// Throws <see cref="InvalidOperationException"/> when the job's count could not be read; the
    /// run was stopped then, never left unwatched.
    /// </summary>
    public async Task<bool> FinishAsync()
    {
        await StopAsync().ConfigureAwait(false);
        if (_unreadable is { } unreadable)
            throw unreadable;
        return _over || Check();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stop.Dispose();
    }

    private async Task StopAsync()
    {
        if (!_stop.IsCancellationRequested)
            await _stop.CancelAsync().ConfigureAwait(false);
        await _watch.ConfigureAwait(false);
    }

    private async Task WatchAsync()
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
            {
                // A refused stop is tried again on the next tick; the job's close kills it at the latest.
                if ((_over || Check()) && _job.Terminate())
                    return;
            }
        }
        catch (OperationCanceledException) { /* the run ended; FinishAsync reads once more */ }
        catch (InvalidOperationException ex)
        {
            _unreadable = ex;
            _ = _job.Terminate();
        }
    }

    private bool Check()
    {
        if (WroteMoreThanDeclared(_job.WrittenBytes(), _stdErrRead.Value, DeclaredBytes))
            _over = true;
        return _over;
    }
}
