namespace Archiver.Core.IO;

/// <summary>
/// T-F128 follow-up: a minimal read-only stream wrapper that reports into a shared
/// <see cref="AggregateProgressTracker"/> instead of computing its own per-instance percent like
/// <see cref="ProgressStream"/> does — used for folder hashing, which only ever reads (never
/// writes), so this deliberately doesn't mirror <see cref="ProgressStream"/>'s Write overrides.
/// </summary>
internal sealed class AggregateProgressStream : Stream
{
    private readonly Stream _inner;
    private readonly AggregateProgressTracker _tracker;
    private readonly string? _currentFile;

    public AggregateProgressStream(Stream inner, AggregateProgressTracker tracker, string? currentFile)
    {
        _inner = inner;
        _tracker = tracker;
        _currentFile = currentFile;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = _inner.Read(buffer, offset, count);
        _tracker.Report(read, _currentFile);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        int read = await _inner.ReadAsync(buffer, offset, count, ct).ConfigureAwait(false);
        _tracker.Report(read, _currentFile);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        int read = await _inner.ReadAsync(buffer, ct).ConfigureAwait(false);
        _tracker.Report(read, _currentFile);
        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
