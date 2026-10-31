using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Unicode;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// T-F305: reads an uncompressed tar's header blocks to tell whether every name is valid UTF-8
/// (7-Zip's rule for a header with no charset). Only chooses between two sandboxed tar.exe readings
/// in <see cref="TarSandboxScope.ListAsync"/>; nothing is extracted here, and the security pre-scan
/// still runs on tar.exe's own listing.
/// </summary>
internal static class TarHeaderNames
{
    internal delegate int ReadAt(Span<byte> buffer, long offset);

    private const int BlockSize = 512;
    private const int MaxLongNameBytes = 64 * 1024;

    /// <summary>
    /// True when every name is valid UTF-8, false when one is not, null when the file is not an
    /// uncompressed POSIX/GNU tar or a header is malformed.
    /// </summary>
    internal static bool? AreAllUtf8(SafeFileHandle archive, CancellationToken cancellationToken)
        => AreAllUtf8((buffer, offset) => RandomAccess.Read(archive, buffer, offset), cancellationToken);

    /// <inheritdoc cref="AreAllUtf8(SafeFileHandle, CancellationToken)"/>
    internal static bool? AreAllUtf8(ReadAt read, CancellationToken cancellationToken)
    {
        var walk = new NameWalk(read);
        return Walk(read, walk.Visit, cancellationToken) == true ? walk.AllUtf8 : null;
    }

    /// <summary>
    /// T-F441: every entry's name as its headers store it, in archive order, decoded as tar.exe
    /// reads it: under hdrcharset=UTF-8 when <paramref name="utf8Names"/>, otherwise in
    /// <paramref name="legacy"/> (the user locale's OEM page); a pax "path" is UTF-8 either way.
    /// Only a veto on tar.exe's own listing (<see cref="TarNames.Judge"/>), never a name Pakko
    /// uses. Null when the walk cannot decide: not an uncompressed POSIX/GNU tar, a malformed or
    /// oversized record, sparse or another entry type libarchive may list differently, a global pax
    /// header that names a path or a charset, bytes that do not decode.
    /// </summary>
    internal static IReadOnlyList<string>? ReadNames(ReadAt read, bool utf8Names, Encoding legacy, CancellationToken cancellationToken)
    {
        var list = new NameList(read, utf8Names, legacy);
        return Walk(read, list.Visit, cancellationToken) == true ? list.Names : null;
    }

    /// <inheritdoc cref="ReadNames(ReadAt, bool, Encoding, CancellationToken)"/>
    internal static IReadOnlyList<string>? ReadNames(SafeFileHandle archive, bool utf8Names, Encoding legacy, CancellationToken cancellationToken)
        => ReadNames((buffer, offset) => RandomAccess.Read(archive, buffer, offset), utf8Names, legacy, cancellationToken);

    /// <summary>
    /// The names of a gzip-compressed tar, as <see cref="ReadNames(ReadAt, bool, Encoding, CancellationToken)"/>;
    /// null too when the stream is not valid gzip or the tar inside is longer than
    /// <paramref name="maxDecompressedBytes"/>.
    /// </summary>
    internal static IReadOnlyList<string>? ReadNamesInGzip(
        Stream compressed, long maxDecompressedBytes, bool utf8Names, Encoding legacy, CancellationToken cancellationToken)
    {
        try
        {
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress, leaveOpen: true);
            var forward = new ForwardReader(gzip, maxDecompressedBytes, cancellationToken);
            IReadOnlyList<string>? names = ReadNames(forward.ReadAt, utf8Names, legacy, cancellationToken);
            return forward.LimitExceeded ? null : names;
        }
        catch (InvalidDataException)
        {
            return null; // not a gzip stream, or a damaged one
        }
    }

    // True when the walk reached the archive's end; null when a visit could not decide, a header is
    // malformed, or the file holds no header at all.
    private static bool? Walk(ReadAt read, Func<byte[], bool, long, long, bool> visit, CancellationToken cancellationToken)
    {
        byte[] header = new byte[BlockSize];
        long offset = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int n = ReadFully(read, header, offset);
            if (n == 0 || (n == BlockSize && header.AsSpan().IndexOfAnyExcept((byte)0) < 0))
                return offset == 0 ? null : true;
            if (n < BlockSize)
                return null;
            long? size = ParseHeader(header, out bool gnu);
            if (size is null || !visit(header, gnu, size.Value, offset + BlockSize))
                return null;
            offset += BlockSize + (size.Value + BlockSize - 1) / BlockSize * BlockSize;
        }
    }

    /// <summary>
    /// T-F310: the same answer for a gzip-compressed tar. Null too when the stream is not valid
    /// gzip, or the tar inside is longer than <paramref name="maxDecompressedBytes"/>.
    /// </summary>
    internal static bool? AreAllUtf8InGzip(Stream compressed, long maxDecompressedBytes, CancellationToken cancellationToken)
    {
        try
        {
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress, leaveOpen: true);
            var forward = new ForwardReader(gzip, maxDecompressedBytes, cancellationToken);
            bool? allUtf8 = AreAllUtf8(forward.ReadAt, cancellationToken);
            return forward.LimitExceeded ? null : allUtf8;
        }
        catch (InvalidDataException)
        {
            return null; // not a gzip stream, or a damaged one
        }
    }

    // The walk only moves forward (a header, its long-name content, the next header), so a
    // decompressing stream serves it: the bytes between two reads are decompressed and dropped.
    // No content is kept, so memory stays flat however far the archive expands; the limit bounds time.
    // (ReadNames keeps the names themselves: no more than tar.exe's listing already held in memory.)
    private sealed class ForwardReader(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        private readonly byte[] _discard = new byte[64 * 1024];
        private long _position;

        public bool LimitExceeded { get; private set; }

        public int ReadAt(Span<byte> buffer, long offset)
        {
            while (_position < offset)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int skipped = ReadWithinLimit(_discard.AsSpan(0, (int)Math.Min(_discard.Length, offset - _position)));
                if (skipped == 0)
                    return 0;
            }
            return _position == offset ? ReadWithinLimit(buffer) : 0;
        }

        private int ReadWithinLimit(Span<byte> buffer)
        {
            if (_position >= maxBytes)
            {
                LimitExceeded = true;
                return 0;
            }
            int n = stream.Read(buffer[..(int)Math.Min(buffer.Length, maxBytes - _position)]);
            _position += n;
            return n;
        }
    }

    // The entry's content size, or null when the block is not a valid POSIX/GNU tar header.
    private static long? ParseHeader(byte[] header, out bool gnu)
    {
        gnu = header.AsSpan(257, 8).SequenceEqual("ustar  \0"u8);
        bool posix = header.AsSpan(257, 6).SequenceEqual("ustar\0"u8);
        if ((!gnu && !posix) || !ChecksumMatches(header) || (header[124] & 0x80) != 0)
            return null;
        return ParseOctal(header.AsSpan(124, 12));
    }

    private sealed class NameWalk(ReadAt read)
    {
        private bool _nextNameReplaced;

        public bool AllUtf8 { get; private set; } = true;

        // False when the walk cannot decide and the caller must answer null.
        public bool Visit(byte[] header, bool gnu, long size, long contentOffset)
        {
            switch (header[156])
            {
                case (byte)'S':
                    return false; // GNU sparse: extension blocks follow; left to libarchive's wording
                case (byte)'L':
                    if (size > MaxLongNameBytes)
                        return false;
                    byte[] longName = new byte[size];
                    if (ReadFully(read, longName, contentOffset) < longName.Length)
                        return false;
                    AllUtf8 &= IsUtf8(longName);
                    _nextNameReplaced = true;
                    return true;
                case (byte)'x':
                    _nextNameReplaced = true; // pax "path" is UTF-8 by the standard
                    return true;
                case (byte)'g' or (byte)'K':
                    return true;
                default:
                    if (!_nextNameReplaced)
                        AllUtf8 &= IsUtf8(header.AsSpan(0, 100)) && (gnu || IsUtf8(header.AsSpan(345, 155)));
                    _nextNameReplaced = false;
                    return true;
            }
        }

        private static bool IsUtf8(ReadOnlySpan<byte> field)
        {
            int end = field.IndexOf((byte)0);
            return Utf8.IsValid(end < 0 ? field : field[..end]);
        }
    }

    // libarchive's order for an entry's name: a pax "path" record, else a GNU "L" long name, else
    // the header's own (POSIX: prefix "/" name; GNU magic keeps other fields where the prefix is).
    private sealed class NameList(ReadAt read, bool utf8Names, Encoding legacy)
    {
        private string? _longName;
        private string? _paxPath;

        public List<string> Names { get; } = [];

        // False when the walk cannot decide.
        public bool Visit(byte[] header, bool gnu, long size, long contentOffset)
        {
            switch (header[156])
            {
                case (byte)'L':
                    byte[]? longName = ReadContent(size, contentOffset);
                    _longName = longName is null ? null : DecodeHeaderBytes(UpToNul(longName));
                    return _longName is not null;
                case (byte)'x':
                    return ReadPax(size, contentOffset, global: false);
                case (byte)'g':
                    return ReadPax(size, contentOffset, global: true);
                case (byte)'K':
                    return true; // a long link target, not a name
                case 0 or (byte)'0' or (byte)'1' or (byte)'2' or (byte)'3' or (byte)'4' or (byte)'5' or (byte)'6' or (byte)'7':
                    // Both on one entry: libarchive 3.5.2 lets the outer one win, later versions may
                    // not, and a forger chooses the order - so no answer.
                    if (_paxPath is not null && _longName is not null)
                        return false;
                    string? name = _paxPath ?? _longName ?? DecodeHeaderBytes(HeaderName(header, gnu));
                    _paxPath = null;
                    _longName = null;
                    if (name is null || IsRootedOrClimbs(name))
                        return false;
                    Names.Add(name);
                    return true;
                default:
                    return false; // sparse, a GNU dump directory, a volume label...: left to tar.exe
            }
        }

        private static byte[] HeaderName(byte[] header, bool gnu)
        {
            ReadOnlySpan<byte> name = UpToNul(header.AsSpan(0, 100));
            ReadOnlySpan<byte> prefix = gnu ? [] : UpToNul(header.AsSpan(345, 155));
            // libarchive adds the separator only when the prefix does not already end with one.
            return prefix.IsEmpty ? name.ToArray()
                : prefix[^1] == (byte)'/' ? [.. prefix, .. name]
                : [.. prefix, (byte)'/', .. name];
        }

        // bsdtar prints such a name with its leading '/', drive or "../" taken off (edit_pathname, no
        // -P), so it would never equal the header's: the headers give no answer for this archive.
        private static bool IsRootedOrClimbs(string name) =>
            name.StartsWith('/') || name.StartsWith('\\') || (name.Length >= 2 && name[1] == ':')
            || name.Split('/', '\\').Contains("..");

        // Records are "<length> <key>=<value>\n", the length counting the whole record.
        private bool ReadPax(long size, long contentOffset, bool global)
        {
            byte[]? content = ReadContent(size, contentOffset);
            if (content is null)
                return false;

            byte[]? path = null;
            bool binary = false;
            int i = 0;
            while (i < content.Length)
            {
                int space = content.AsSpan(i).IndexOf((byte)' ');
                if (space <= 0
                    || !int.TryParse(content.AsSpan(i, space), NumberStyles.None, CultureInfo.InvariantCulture, out int length)
                    || length <= space + 2 || length > content.Length - i || content[i + length - 1] != (byte)'\n')
                    return false;
                ReadOnlySpan<byte> record = content.AsSpan(i + space + 1, length - space - 2);
                int equals = record.IndexOf((byte)'=');
                if (equals <= 0)
                    return false;
                ReadOnlySpan<byte> key = record[..equals];
                ReadOnlySpan<byte> value = record[(equals + 1)..];
                if (key.SequenceEqual("path"u8))
                    path = value.ToArray();
                else if (key.SequenceEqual("hdrcharset"u8))
                    binary = value.SequenceEqual("BINARY"u8);
                else if (key.StartsWith("GNU.sparse"u8) || key.SequenceEqual("size"u8))
                    return false; // the content is not where the header's own size field puts the next header
                i += length;
            }

            if (global)
                return path is null && !binary;
            if (path is not null)
            {
                _paxPath = binary ? DecodeHeaderBytes(path) : DecodeUtf8(path);
                return _paxPath is not null;
            }
            return true;
        }

        private byte[]? ReadContent(long size, long contentOffset)
        {
            if (size > MaxLongNameBytes)
                return null;
            byte[] content = new byte[size];
            return ReadFully(read, content, contentOffset) < content.Length ? null : content;
        }

        private string? DecodeHeaderBytes(ReadOnlySpan<byte> bytes) => utf8Names ? DecodeUtf8(bytes) : legacy.GetString(bytes);

        private static string? DecodeUtf8(ReadOnlySpan<byte> bytes) => Utf8.IsValid(bytes) ? Encoding.UTF8.GetString(bytes) : null;

        private static ReadOnlySpan<byte> UpToNul(ReadOnlySpan<byte> field)
        {
            int end = field.IndexOf((byte)0);
            return end < 0 ? field : field[..end];
        }
    }

    private static int ReadFully(ReadAt read, Span<byte> buffer, long offset)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = read(buffer[total..], offset + total);
            if (n <= 0)
                break;
            total += n;
        }
        return total;
    }

    // Stored octal, over the header with the checksum field as spaces; old tars summed signed bytes.
    private static bool ChecksumMatches(byte[] header)
    {
        long? stored = ParseOctal(header.AsSpan(148, 8));
        if (stored is null)
            return false;
        long unsignedSum = 8 * ' ';
        long signedSum = 8 * ' ';
        for (int i = 0; i < BlockSize; i++)
        {
            if (i >= 148 && i < 156)
                continue;
            unsignedSum += header[i];
            signedSum += (sbyte)header[i];
        }
        return stored == unsignedSum || stored == signedSum;
    }

    // Leading spaces, one or more octal digits, then the end of the field, a NUL or a space.
    private static long? ParseOctal(ReadOnlySpan<byte> field)
    {
        int i = 0;
        while (i < field.Length && field[i] == (byte)' ')
            i++;
        long value = 0;
        int digits = 0;
        for (; i < field.Length && field[i] >= (byte)'0' && field[i] <= (byte)'7'; i++, digits++)
            value = value * 8 + (field[i] - '0');
        if (digits == 0 || (i < field.Length && field[i] != 0 && field[i] != (byte)' '))
            return null;
        return value;
    }
}
