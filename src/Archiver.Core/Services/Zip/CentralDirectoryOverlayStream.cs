using System.Buffers.Binary;

namespace Archiver.Core.Services.Zip;

/// <summary>
/// T-F529: an archive as one batch of its central directory makes it — the file's own bytes, then
/// some of its directory records, copied as they are, and an end record that declares just them.
/// .NET's <see cref="System.IO.Compression.ZipArchive"/> reads it as a ZIP of those entries only;
/// their local headers and data are the file's own, at the offsets the records give. Once .NET has
/// read the records, <see cref="HideTail"/> leaves the file's own length and bytes, so every check
/// .NET makes against the archive's length when an entry is opened sees what it saw on the file.
/// </summary>
internal sealed class CentralDirectoryOverlayStream : Stream
{
    private const int ClassicEndLength = 22;
    private const int Zip64RecordLength = 56;
    private const int Zip64LocatorLength = 20;

    private readonly Stream _file;
    private readonly long _fileLength;
    private readonly byte[] _tail;
    private bool _tailHidden;
    private long _position;

    /// <param name="file">The archive, seekable; not owned.</param>
    /// <param name="records">The batch's directory records, as the archive holds them.</param>
    /// <param name="recordCount">How many records <paramref name="records"/> holds.</param>
    /// <param name="diskNumber">The disk number .NET read the archive's entries against.</param>
    /// <param name="forceZip64">Test seam: the Zip64 end records even when the classic one fits.</param>
    public CentralDirectoryOverlayStream(Stream file, ReadOnlySpan<byte> records, int recordCount, uint diskNumber, bool forceZip64 = false)
    {
        _file = file;
        _fileLength = file.Length;
        bool zip64 = forceZip64 || diskNumber >= ushort.MaxValue || _fileLength >= uint.MaxValue || recordCount >= ushort.MaxValue;
        _tail = new byte[records.Length + ClassicEndLength + (zip64 ? Zip64RecordLength + Zip64LocatorLength : 0)];
        records.CopyTo(_tail);
        Span<byte> end = _tail.AsSpan(records.Length);
        if (zip64)
        {
            WriteZip64End(end, records.Length, recordCount, diskNumber);
            end = end[(Zip64RecordLength + Zip64LocatorLength)..];
            WriteClassicEnd(end, ushort.MaxValue, ushort.MaxValue, uint.MaxValue, uint.MaxValue);
        }
        else
        {
            WriteClassicEnd(end, (ushort)diskNumber, (ushort)recordCount, (uint)records.Length, (uint)_fileLength);
        }
    }

    private void WriteZip64End(Span<byte> end, long recordsLength, long recordCount, uint diskNumber)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(end, 0x06064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(end[4..], Zip64RecordLength - 12);
        BinaryPrimitives.WriteUInt16LittleEndian(end[12..], 45); // version made by
        BinaryPrimitives.WriteUInt16LittleEndian(end[14..], 45); // version needed
        BinaryPrimitives.WriteUInt32LittleEndian(end[16..], diskNumber);
        BinaryPrimitives.WriteUInt32LittleEndian(end[20..], diskNumber);
        BinaryPrimitives.WriteInt64LittleEndian(end[24..], recordCount);
        BinaryPrimitives.WriteInt64LittleEndian(end[32..], recordCount);
        BinaryPrimitives.WriteInt64LittleEndian(end[40..], recordsLength);
        BinaryPrimitives.WriteInt64LittleEndian(end[48..], _fileLength);

        Span<byte> locator = end[Zip64RecordLength..];
        BinaryPrimitives.WriteUInt32LittleEndian(locator, 0x07064b50);
        BinaryPrimitives.WriteUInt32LittleEndian(locator[4..], diskNumber);
        BinaryPrimitives.WriteInt64LittleEndian(locator[8..], _fileLength + recordsLength);
        BinaryPrimitives.WriteUInt32LittleEndian(locator[16..], 1);
    }

    private static void WriteClassicEnd(Span<byte> end, ushort diskNumber, ushort recordCount, uint recordsLength, uint recordsOffset)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(end, 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(end[4..], diskNumber);
        BinaryPrimitives.WriteUInt16LittleEndian(end[6..], diskNumber);
        BinaryPrimitives.WriteUInt16LittleEndian(end[8..], recordCount);
        BinaryPrimitives.WriteUInt16LittleEndian(end[10..], recordCount);
        BinaryPrimitives.WriteUInt32LittleEndian(end[12..], recordsLength);
        BinaryPrimitives.WriteUInt32LittleEndian(end[16..], recordsOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(end[20..], 0); // no comment
    }

    /// <summary>From now on the stream is the file alone: called once .NET has read the records.</summary>
    public void HideTail() => _tailHidden = true;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _tailHidden ? _fileLength : _fileLength + _tail.Length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_position >= Length || buffer.IsEmpty)
            return 0;
        if (_position < _fileLength)
        {
            int wanted = (int)Math.Min(buffer.Length, _fileLength - _position);
            _file.Seek(_position, SeekOrigin.Begin);
            int read = _file.Read(buffer[..wanted]);
            _position += read;
            return read;
        }
        int tailOffset = (int)(_position - _fileLength);
        int copied = Math.Min(buffer.Length, _tail.Length - tailOffset);
        _tail.AsSpan(tailOffset, copied).CopyTo(buffer);
        _position += copied;
        return copied;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        // As FileStream: a negative position is an error, one past the end is allowed (reads give 0).
        if (target < 0)
            throw new IOException("An attempt was made to move the position before the beginning of the stream.");
        _position = target;
        return target;
    }

    public override void Flush() { /* read-only */ }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
