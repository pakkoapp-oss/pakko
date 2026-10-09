using System.Security.Cryptography;
using Archiver.Core.IO;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Recovery;

/// <summary>What PAR2 records about a file: its MD5, the MD5 of its first 16 KiB, and each slice's
/// checksum. A slice that starts at or past the end of a shorter file has none.</summary>
internal sealed record Par2FileHashes(byte[] FileMd5, byte[] Md5First16k, Par2SliceChecksum?[] Slices);

/// <summary>
/// One sequential read of a file, hashed the way PAR 2.0 defines it (T-F275): the file MD5 and the
/// 16 KiB MD5 over the raw bytes, each slice's MD5 and CRC-32 over the slice zero-padded to the
/// slice size. The slices cover the first <c>declaredLength</c> bytes — the length the set
/// records, which a damaged file may exceed or fall short of.
/// </summary>
internal static class Par2FileHasher
{
    private const int BufferLength = 1 << 20;

    internal static Par2FileHashes Hash(SafeFileHandle file, long declaredLength, long sliceSize, Action<long>? progress, CancellationToken cancellationToken)
    {
        long actualLength = RandomAccess.GetLength(file);
        var slices = new Par2SliceChecksum?[SliceCount(declaredLength, sliceSize)];
        using IncrementalHash fileHash = Par2Md5.Create();
        using IncrementalHash firstHash = Par2Md5.Create();
        using IncrementalHash sliceHash = Par2Md5.Create();
        var crc = new Crc32.Accumulator();
        byte[] buffer = new byte[BufferLength];
        int slice = 0;
        long sliceFilled = 0;

        long position = 0;
        while (position < actualLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = RandomAccess.Read(file, buffer, position);
            if (read <= 0)
                break;
            ReadOnlySpan<byte> chunk = buffer.AsSpan(0, read);
            fileHash.AppendData(chunk);
            if (position < Par2Packets.First16kLength)
                firstHash.AppendData(chunk[..(int)Math.Min(read, Par2Packets.First16kLength - position)]);

            long sliceDataEnd = Math.Min(position + read, declaredLength);
            int offset = 0;
            while (position + offset < sliceDataEnd)
            {
                int take = (int)Math.Min(sliceDataEnd - position - offset, sliceSize - sliceFilled);
                ReadOnlySpan<byte> part = chunk.Slice(offset, take);
                sliceHash.AppendData(part);
                crc.Update(part);
                sliceFilled += take;
                offset += take;
                if (sliceFilled >= sliceSize)
                {
                    slices[slice++] = Finish(sliceHash, ref crc);
                    sliceFilled = 0;
                }
            }
            position += read;
            progress?.Invoke(read);
        }

        if (sliceFilled > 0)
        {
            Array.Clear(buffer);
            for (long zeros = sliceSize - sliceFilled; zeros > 0; zeros -= Math.Min(zeros, buffer.Length))
            {
                ReadOnlySpan<byte> padding = buffer.AsSpan(0, (int)Math.Min(zeros, buffer.Length));
                sliceHash.AppendData(padding);
                crc.Update(padding);
            }
            slices[slice] = Finish(sliceHash, ref crc);
        }

        return new Par2FileHashes(fileHash.GetHashAndReset(), firstHash.GetHashAndReset(), slices);
    }

    /// <summary>ceil(length / sliceSize), without the overflow of length + sliceSize - 1.</summary>
    internal static long SliceCount(long length, long sliceSize) =>
        length / sliceSize + (length % sliceSize == 0 ? 0 : 1);

    private static Par2SliceChecksum Finish(IncrementalHash sliceHash, ref Crc32.Accumulator crc)
    {
        var checksum = new Par2SliceChecksum(System.Buffers.Binary.BinaryPrimitives.ReadUInt128LittleEndian(sliceHash.GetHashAndReset()), crc.Finish());
        crc = new Crc32.Accumulator();
        return checksum;
    }
}
