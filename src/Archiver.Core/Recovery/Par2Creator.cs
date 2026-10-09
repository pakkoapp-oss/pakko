using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Archiver.Core.Services;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Recovery;

/// <summary>Slice size and counts of a set to create.</summary>
internal readonly record struct Par2Parameters(long SliceSize, int SliceCount, int RecoveryCount);

/// <summary>The files a create wrote.</summary>
internal sealed record Par2CreateResult(string IndexPath, string VolumePath, Par2Parameters Parameters);

/// <summary>
/// Writes a PAR 2.0 set protecting one file (T-F275): <c>&lt;file&gt;.par2</c> holding the
/// critical packets, and <c>&lt;file&gt;.vol0+R.par2</c> holding R recovery blocks with exponents
/// 0..R-1 between two copies of the critical packets — the names par2cmdline gives
/// <c>par2 c -n1 &lt;file&gt;.par2 &lt;file&gt;</c>. The file is opened read-shared for the whole
/// run, so it cannot change between the hashing pass and the recovery pass.
/// </summary>
internal static class Par2Creator
{
    private const int RecoveryHeaderLength = Par2Packets.HeaderLength + Par2Packets.RecoveryPrefixLength;
    private const int CopyBufferLength = 1 << 20;

    /// <summary>par2cmdline's default shape: about 2000 slices, slice size a multiple of 4,
    /// recovery blocks <paramref name="percent"/> of the slice count rounded up. Null when there is
    /// nothing to protect or the file is beyond <see cref="Par2Limits"/>.</summary>
    internal static Par2Parameters? ChooseParameters(long fileLength, int percent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(percent, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        if (fileLength <= 0)
            return null;
        long sliceSize = Math.Max(4, (Par2FileHasher.SliceCount(fileLength, Par2Limits.TargetInputSlices) + 3) & ~3L);
        sliceSize = Math.Min(sliceSize, Par2Limits.MaxSliceSize);
        long sliceCount = Par2FileHasher.SliceCount(fileLength, sliceSize);
        if (sliceCount > Par2Limits.MaxInputSlices)
            return null;
        int recoveryCount = (int)Math.Max(1, Par2FileHasher.SliceCount(sliceCount * percent, 100));
        return new Par2Parameters(sliceSize, (int)sliceCount, recoveryCount);
    }

    /// <summary>par2cmdline's volume name: both numbers zero-padded to the width of the largest
    /// exponent or count among the set's files (the index file's exponent is R).</summary>
    internal static string VolumePath(string filePath, int recoveryCount)
    {
        int width = recoveryCount.ToString(CultureInfo.InvariantCulture).Length;
        string first = 0.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
        return filePath + ".vol" + first + "+" + recoveryCount.ToString(CultureInfo.InvariantCulture) + ".par2";
    }

    internal static string IndexPath(string filePath) => filePath + ".par2";

    internal static Par2CreateResult Create(string filePath, Par2Parameters parameters, Action<double>? progress, CancellationToken cancellationToken)
    {
        if (parameters.SliceSize <= 0 || parameters.SliceSize % 4 != 0 || parameters.SliceSize > Par2Limits.MaxSliceSize)
            throw new ArgumentOutOfRangeException(nameof(parameters), "Slice size must be a positive multiple of 4 within the reader's limit.");
        ArgumentOutOfRangeException.ThrowIfLessThan(parameters.RecoveryCount, 1);

        string indexPath = IndexPath(filePath);
        string volumePath = VolumePath(filePath, parameters.RecoveryCount);
        string volumeTemp = ArchiveTempFile.Create(volumePath);
        string indexTemp = ArchiveTempFile.Create(indexPath);
        try
        {
            using (SafeFileHandle source = File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                long length = RandomAccess.GetLength(source);
                long sliceCount = Par2FileHasher.SliceCount(length, parameters.SliceSize);
                if (length <= 0 || sliceCount > Par2Limits.MaxInputSlices || sliceCount != parameters.SliceCount)
                    throw new ArgumentOutOfRangeException(nameof(parameters), "The parameters do not fit the file.");

                var tracker = new Par2Progress(progress, (double)length * (1 + parameters.RecoveryCount));
                Par2FileHashes hashes = Par2FileHasher.Hash(source, length, parameters.SliceSize, bytes => tracker.Add(bytes), cancellationToken);
                byte[] name = Encoding.UTF8.GetBytes(Path.GetFileName(filePath));
                byte[] fileId = Par2Packets.FileId(hashes.Md5First16k, length, name);
                byte[] mainBody = Par2Packets.MainBody(parameters.SliceSize, fileId);
                byte[] setId = Par2Md5.Hash(mainBody);
                byte[][] critical =
                [
                    Par2Packets.Build(setId, Par2Packets.MainType, mainBody),
                    Par2Packets.Build(setId, Par2Packets.FileDescType, Par2Packets.FileDescBody(fileId, hashes.FileMd5, hashes.Md5First16k, length, name)),
                    Par2Packets.Build(setId, Par2Packets.IfscType, Par2Packets.IfscBody(fileId, hashes.Slices.Select(s => s!.Value).ToArray())),
                ];
                byte[] creator = Par2Packets.Build(setId, Par2Packets.CreatorType, Par2Packets.CreatorBody());

                WriteVolume(volumeTemp, source, length, setId, critical, creator, parameters, tracker, cancellationToken);
                WritePackets(indexTemp, [.. critical, creator]);
            }
            File.Move(volumeTemp, volumePath, overwrite: true);
            File.Move(indexTemp, indexPath, overwrite: true);
            return new Par2CreateResult(indexPath, volumePath, parameters);
        }
        catch
        {
            DeleteQuietly(volumeTemp);
            DeleteQuietly(indexTemp);
            throw;
        }
    }

    private static void WriteVolume(string path, SafeFileHandle source, long length, byte[] setId, byte[][] critical, byte[] creator,
        Par2Parameters parameters, Par2Progress tracker, CancellationToken cancellationToken)
    {
        long criticalLength = critical.Sum(p => (long)p.Length);
        long packetLength = RecoveryHeaderLength + parameters.SliceSize;
        long recoveryStart = criticalLength;
        using SafeFileHandle volume = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

        long offset = WriteAll(volume, critical, 0);
        Span<byte> header = stackalloc byte[RecoveryHeaderLength];
        for (int e = 0; e < parameters.RecoveryCount; e++)
        {
            Par2Packets.WriteHeader(header, setId, Par2Packets.RecoveryType, Par2Packets.RecoveryPrefixLength + parameters.SliceSize);
            BinaryPrimitives.WriteUInt32LittleEndian(header[Par2Packets.HeaderLength..], (uint)e);
            RandomAccess.Write(volume, header, offset);
            offset += packetLength;
        }
        RandomAccess.SetLength(volume, offset);
        offset = WriteAll(volume, critical, offset);
        WriteAll(volume, [creator], offset);

        ushort[] constants = Gf16.InputConstants(parameters.SliceCount);
        Par2SliceCombiner.Combine(parameters.SliceSize, parameters.SliceCount, parameters.RecoveryCount,
            (o, i) => Gf16.Pow(constants[i], (uint)o),
            (i, at, buffer) => Par2FileIo.ReadPadded(source, length, i * parameters.SliceSize + at, buffer),
            (o, at, data) => RandomAccess.Write(volume, data, recoveryStart + o * packetLength + RecoveryHeaderLength + at),
            bytes => tracker.Add(bytes * parameters.RecoveryCount), cancellationToken);

        byte[] buffer = new byte[CopyBufferLength];
        for (int e = 0; e < parameters.RecoveryCount; e++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long packetStart = recoveryStart + e * packetLength;
            byte[] md5 = Par2FileIo.HashRange(volume, packetStart + Par2Packets.SetIdOffset, packetLength - Par2Packets.SetIdOffset, buffer);
            RandomAccess.Write(volume, md5, packetStart + Par2Packets.HashOffset);
        }
    }

    private static void WritePackets(string path, byte[][] packets)
    {
        using SafeFileHandle file = File.OpenHandle(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        WriteAll(file, packets, 0);
    }

    private static long WriteAll(SafeFileHandle file, byte[][] packets, long offset)
    {
        foreach (byte[] packet in packets)
        {
            RandomAccess.Write(file, packet, offset);
            offset += packet.Length;
        }
        return offset;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort: a temporary file left behind is swept by the next run in this folder
        }
    }
}
