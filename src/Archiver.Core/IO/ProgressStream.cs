namespace Archiver.Core.IO;

/// <summary>
/// A read/write stream wrapper that counts bytes transferred and reports
/// progress as a percentage of a known total.
/// </summary>
internal sealed class ProgressStream : Stream
{
    private readonly Stream _inner;
    private readonly long _totalBytes;
    private readonly IProgress<int> _progress;
    private long _bytesTransferred;
    private int _lastReported;

    public ProgressStream(Stream inner, long totalBytes, IProgress<int> progress)
        : this(inner, totalBytes, startOffset: 0, progress) { }

    public ProgressStream(Stream inner, long totalBytes, long startOffset, IProgress<int> progress)
    {
        _inner = inner;
        _totalBytes = totalBytes;
        _bytesTransferred = startOffset;
        _progress = progress;
        _lastReported = startOffset > 0 ? (int)Math.Min(100, startOffset * 100L / totalBytes) : -1;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = _inner.Read(buffer, offset, count);
        Report(read);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        int read = await _inner.ReadAsync(buffer, offset, count, ct).ConfigureAwait(false);
        Report(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        int read = await _inner.ReadAsync(buffer, ct).ConfigureAwait(false);
        Report(read);
        return read;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);
        Report(count);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        await _inner.WriteAsync(buffer, offset, count, ct).ConfigureAwait(false);
        Report(count);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        await _inner.WriteAsync(buffer, ct).ConfigureAwait(false);
        Report(buffer.Length);
    }

    private void Report(int bytes)
    {
        if (bytes <= 0 || _totalBytes <= 0) return;
        _bytesTransferred += bytes;
        int pct = (int)Math.Min(100, _bytesTransferred * 100L / _totalBytes);
        if (pct != _lastReported)
        {
            _lastReported = pct;
            _progress.Report(pct);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
