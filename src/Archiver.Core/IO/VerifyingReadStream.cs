using System.Globalization;
using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.Core.IO;

/// <summary>
/// Wraps an archive entry's decompressed content: fails with <see cref="InvalidDataException"/>
/// as soon as more than the entry's declared size has been read (T-F231 — the compression-bomb
/// and free-space gates trust declared sizes), at end of stream if less than the declared size was
/// read, and, when an expected CRC-32 is given, at end of stream if the content's CRC-32 differs (T-F246).
/// Streams; nothing is buffered. Since .NET 11, <c>ZipArchiveEntry.Open()</c> checks CRC-32 and the
/// declared size itself and throws first (T-F492): this stream turns those throws into Core's codes,
/// so the verdict's text has one owner.
/// </summary>
internal sealed class VerifyingReadStream(Stream inner, long declaredLength, uint? expectedCrc32) : Stream
{
    private Crc32.Accumulator _accumulator = new();
    private long _totalRead;
    private bool _finished;

    // T-F492: the runtime's own texts (System.IO.Compression's SR.CrcMismatch and
    // SR.UnexpectedStreamLength, .NET 11). VerifyingReadStreamTests pins them against ZipArchive.
    internal const string BclCrcMismatchText = "The CRC32 checksum of the extracted data does not match the expected value from the archive.";
    internal const string BclLengthMismatchText = "The decompressed data length does not match the expected value from the archive.";

    /// <summary>
    /// Opens a plain (not Pakko-decrypted) entry for reading, checked against its declared size and
    /// CRC-32. Since .NET 11, <c>Open()</c> reads the entry's encryption itself and throws
    /// <see cref="NotSupportedException"/> on a header it cannot use (T-F492, found by the fuzzer);
    /// that becomes <see cref="MessageCode.ContentDamaged"/>, an <see cref="InvalidDataException"/>
    /// every per-entry catch already handles.
    /// </summary>
    internal static VerifyingReadStream OpenEntry(ZipArchiveEntry entry)
    {
        Stream inner;
        try
        {
            inner = entry.Open();
        }
        catch (NotSupportedException ex)
        {
            throw CoreMessages.InvalidData(CoreMessages.Text(MessageCode.ContentDamaged), ex);
        }
        return new VerifyingReadStream(inner, entry.Length, entry.Crc32);
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int read;
        try
        {
            read = inner.Read(buffer);
        }
        catch (InvalidDataException ex) when (IsDecompressorFailure(ex))
        {
            throw Translate(ex, buffer);
        }
        Account(buffer[..read]);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read;
        try
        {
            read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException ex) when (IsDecompressorFailure(ex))
        {
            throw Translate(ex, buffer.Span);
        }
        Account(buffer.Span[..read]);
        return read;
    }

    // T-F382: .NET's text for a broken Deflate stream ("unsupported compression method") is English
    // and wrong about the cause. An error Core wrote below this stream keeps its own code.
    private static bool IsDecompressorFailure(InvalidDataException exception) =>
        CoreMessages.Detail(exception).Code == MessageCode.None;

    // .NET 11 checks the CRC-32 only on the read that brings its count to exactly the declared size,
    // so the bytes that read wrote into the buffer are the rest up to it. Both callers wrap
    // entry.Open() with that entry's own Length and Crc32, the values .NET checks against.
    private InvalidDataException Translate(InvalidDataException ex, ReadOnlySpan<byte> buffer)
    {
        if (ex.Message == BclLengthMismatchText)
            return CoreMessages.InvalidData(CoreMessages.Text(MessageCode.ContentLargerThanDeclared,
                declaredLength.ToString("N0", CultureInfo.CurrentCulture)), ex);
        long rest = declaredLength - _totalRead;
        if (ex.Message != BclCrcMismatchText || expectedCrc32 is not { } expected || rest < 0 || rest > buffer.Length)
            return CoreMessages.InvalidData(CoreMessages.Text(MessageCode.ContentDamaged), ex);
        _accumulator.Update(buffer[..(int)rest]);
        uint computed = _accumulator.Finish();
        return CoreMessages.InvalidData(CoreMessages.Text(MessageCode.ContentCrcMismatch,
            expected.ToString("X8", CultureInfo.InvariantCulture), computed.ToString("X8", CultureInfo.InvariantCulture)), ex);
    }

    private void Account(ReadOnlySpan<byte> data)
    {
        if (data.Length > 0)
        {
            _totalRead += data.Length;
            if (_totalRead > declaredLength)
                throw CoreMessages.InvalidData(CoreMessages.Text(MessageCode.ContentLargerThanDeclared,
                    declaredLength.ToString("N0", CultureInfo.CurrentCulture)));
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
            throw CoreMessages.InvalidData(CoreMessages.Text(MessageCode.ContentSmallerThanDeclared,
                declaredLength.ToString("N0", CultureInfo.CurrentCulture)));
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
