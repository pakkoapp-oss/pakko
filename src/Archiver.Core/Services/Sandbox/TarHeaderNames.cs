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
        long offset = 0;
        bool nextNameReplaced = false;
        bool allUtf8 = true;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int n = ReadFully(read, header, offset);
            if (n == 0)
                return offset == 0 ? null : allUtf8;
            if (n < BlockSize)
                return null;
            if (header.AsSpan().IndexOfAnyExcept((byte)0) < 0)
                return offset == 0 ? null : allUtf8;

            bool gnu = header.AsSpan(257, 8).SequenceEqual("ustar  \0"u8);
            bool posix = header.AsSpan(257, 6).SequenceEqual("ustar\0"u8);
            if ((!gnu && !posix) || !ChecksumMatches(header) || (header[124] & 0x80) != 0)
                return null;
            long? size = ParseOctal(header.AsSpan(124, 12));
            if (size is null)
                return null;

            byte type = header[156];
            long contentOffset = offset + BlockSize;
            switch (type)
            {
                case (byte)'S':
                    return null; // GNU sparse: extension blocks follow; left to libarchive's wording
                case (byte)'L':
                    if (size > MaxLongNameBytes)
                        return null;
                    byte[] longName = new byte[size.Value];
                    if (ReadFully(read, longName, contentOffset) < longName.Length)
                        return null;
                    allUtf8 &= IsUtf8(longName);
                    nextNameReplaced = true;
                    break;
                case (byte)'x':
                    nextNameReplaced = true; // pax "path" is UTF-8 by the standard
                    break;
                case (byte)'g' or (byte)'K':
                    break;
                default:
                    if (!nextNameReplaced)
                        allUtf8 &= IsUtf8(header.AsSpan(0, 100)) && (gnu || IsUtf8(header.AsSpan(345, 155)));
                    nextNameReplaced = false;
                    break;
            }

            offset = contentOffset + (size.Value + BlockSize - 1) / BlockSize * BlockSize;
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
