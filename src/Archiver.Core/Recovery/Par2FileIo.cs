using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Recovery;

/// <summary>Positional reads the PAR2 code shares (T-F275).</summary>
internal static class Par2FileIo
{
    /// <summary>Reads <paramref name="buffer"/>.Length bytes at <paramref name="offset"/>; the part
    /// at or past <paramref name="fileLength"/> is zero, as PAR2 pads the last slice.</summary>
    internal static void ReadPadded(SafeFileHandle file, long fileLength, long offset, Span<byte> buffer)
    {
        int valid = (int)Math.Clamp(fileLength - offset, 0, buffer.Length);
        int filled = ReadUpTo(file, buffer[..valid], offset);
        buffer[filled..].Clear();
    }

    /// <summary>Reads until <paramref name="buffer"/> is full or the file ends; returns the count.</summary>
    internal static int ReadUpTo(SafeFileHandle file, Span<byte> buffer, long offset)
    {
        int filled = 0;
        while (filled < buffer.Length)
        {
            int read = RandomAccess.Read(file, buffer[filled..], offset + filled);
            if (read <= 0)
                break;
            filled += read;
        }
        return filled;
    }

    /// <summary>MD5 of <paramref name="length"/> bytes at <paramref name="offset"/>; throws
    /// <see cref="EndOfStreamException"/> when the file is shorter.</summary>
    internal static byte[] HashRange(SafeFileHandle file, long offset, long length, byte[] buffer)
    {
        using IncrementalHash hash = Par2Md5.Create();
        for (long done = 0; done < length;)
        {
            int want = (int)Math.Min(buffer.Length, length - done);
            int read = ReadUpTo(file, buffer.AsSpan(0, want), offset + done);
            if (read < want)
                throw new EndOfStreamException();
            hash.AppendData(buffer.AsSpan(0, read));
            done += read;
        }
        return hash.GetHashAndReset();
    }
}
