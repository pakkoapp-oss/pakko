namespace Archiver.Core.Services.Zip.Decryption;

/// <summary>
/// T-F193: a read-only window [start, start + length) over a seekable archive stream. Keeps its
/// own position and seeks the source before every read, so two windows (or other code) sharing one
/// FileStream never disturb each other's position. The source is disposed only when this window
/// owns it — callers that pair many entries with one raw archive stream pass ownsSource: false.
/// </summary>
internal sealed class EntryRegionStream(Stream source, long start, long length, bool ownsSource) : Stream
{
    private long _position;

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        long remaining = length - _position;
        if (remaining <= 0 || buffer.Length == 0)
            return 0;

        int toRead = (int)Math.Min(buffer.Length, remaining);
        source.Seek(start + _position, SeekOrigin.Begin);
        int read = source.Read(buffer[..toRead]);
        if (read == 0)
            throw new EndOfStreamException("ZIP entry data ends before its declared size.");
        _position += read;
        return read;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }
    public override void Flush() { /* read-only */ }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && ownsSource)
            source.Dispose();
        base.Dispose(disposing);
    }
}
