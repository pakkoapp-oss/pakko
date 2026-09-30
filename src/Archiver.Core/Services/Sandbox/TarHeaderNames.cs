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
        byte[] header = new byte[BlockSize];
        var walk = new NameWalk(read);
        long offset = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int n = ReadFully(read, header, offset);
            if (n == 0 || (n == BlockSize && header.AsSpan().IndexOfAnyExcept((byte)0) < 0))
                return offset == 0 ? null : walk.AllUtf8;
            if (n < BlockSize)
                return null;
            long? size = ParseHeader(header, out bool gnu);
            if (size is null || !walk.Visit(header, gnu, size.Value, offset + BlockSize))
                return null;
            offset += BlockSize + (size.Value + BlockSize - 1) / BlockSize * BlockSize;
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

    private static bool IsUtf8(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        return Utf8.IsValid(end < 0 ? field : field[..end]);
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
