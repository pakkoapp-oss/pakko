namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// T-F561: one tar.exe run writes no more than the entries it extracts declare. libarchive prints 0
/// for an entry whose size comes only after its data (a ZIP written as a stream, inside a gzip), so
/// the compression-ratio check (T-F94) sees nothing; this watches what the run actually writes.
/// The job's own count (<see cref="SandboxJobObject.WrittenBytes"/>) holds files and pipes alike,
/// so the stderr Pakko has read is taken off it; the slack covers stderr still in the pipe.
/// Read every <see cref="Interval"/> on a thread of its own (a pool thread can wait behind the
/// launcher's blocking pipe reads, T-F425), and once more after the run: a run can pass its limit by
/// what it writes in one interval before it is stopped.
/// </summary>
internal sealed class JobWriteLimit : IDisposable
{
    internal const long PipeSlackBytes = 1L << 20;
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    private readonly SandboxJobObject _job;
    private readonly ByteCounter _stdErrRead;
    private readonly ManualResetEventSlim _stop = new();
    private readonly Thread _watch;
    private volatile bool _over;
    private InvalidOperationException? _unreadable;

    private JobWriteLimit(SandboxJobObject job, long declaredBytes, ByteCounter stdErrRead)
    {
        _job = job;
        DeclaredBytes = declaredBytes;
        _stdErrRead = stdErrRead;
        _watch = new Thread(Watch) { IsBackground = true, Name = "Pakko tar.exe write limit" };
        _watch.Start();
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
    public bool Finish()
    {
        Stop();
        if (_unreadable is { } unreadable)
            throw unreadable;
        return _over || Check();
    }

    public void Dispose()
    {
        Stop();
        _stop.Dispose();
    }

    // The watch only waits on the event between readings, so it ends within one reading of the set.
    private void Stop()
    {
        _stop.Set();
        _watch.Join();
    }

    private void Watch()
    {
        try
        {
            while (!_stop.Wait(Interval))
            {
                // A refused stop is tried again on the next tick; the job's close kills it at the latest.
                if ((_over || Check()) && _job.Terminate())
                    return;
            }
        }
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
