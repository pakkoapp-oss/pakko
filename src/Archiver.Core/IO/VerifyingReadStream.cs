using System.Globalization;
using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.Core.IO;

/// <summary>
/// Wraps an archive entry's decompressed content: fails with <see cref="InvalidDataException"/>
/// as soon as more than the entry's declared size has been read (T-F231 — the compression-bomb
/// and free-space gates trust declared sizes), at end of stream if less than the declared size was
/// read, and, when an expected CRC-32 is given, at end of stream if the content's CRC-32 differs (T-F246 — .NET does not check it on read). Streams;
/// nothing is buffered.
/// </summary>
internal sealed class VerifyingReadStream(Stream inner, long declaredLength, uint? expectedCrc32) : Stream
{
    private Crc32.Accumulator _accumulator = new();
    private long _totalRead;
    private bool _finished;

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int read = inner.Read(buffer);
        Account(buffer[..read]);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Account(buffer.Span[..read]);
        return read;
    }

    private void Account(ReadOnlySpan<byte> data)
    {
        if (data.Length > 0)
        {
            _totalRead += data.Length;
            if (_totalRead > declaredLength)
                throw new InvalidDataException(
                    $"Content is larger than its declared size ({declaredLength:N0} bytes).");
            if (expectedCrc32 is not null)
                _accumulator.Update(data);
            return;
        }

        if (_finished)
            return;
        _finished = true;
        // An AE-2 entry has no CRC-32 and its real compression method is outside the HMAC, so the
        // declared size is the only check that the decrypted bytes were read the way they were written.
        if (_totalRead < declaredLength)
            throw new InvalidDataException(
                $"Content is smaller than its declared size ({declaredLength:N0} bytes).");
        if (expectedCrc32 is not { } expected)
            return;
        uint computed = _accumulator.Finish();
        if (computed != expected)
            throw CoreMessages.InvalidData(CoreMessages.Text(MessageCode.ContentCrcMismatch,
                expected.ToString("X8", CultureInfo.InvariantCulture), computed.ToString("X8", CultureInfo.InvariantCulture)));
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override void Flush() { /* read-only */ }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }
}
