using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Recovery;

internal enum Par2VerifyStatus
{
    /// <summary>Length, every slice and the file MD5 match the set.</summary>
    Intact,

    /// <summary>Damaged, and the available recovery blocks can rebuild it.</summary>
    Repairable,

    /// <summary>Damaged beyond what the available recovery blocks can rebuild, or inconsistent
    /// with the set in a way no slice explains.</summary>
    NotRepairable,

    /// <summary>Repairable in principle, but the repair is beyond
    /// <see cref="Par2Limits.MaxRepairMatrixEntries"/>.</summary>
    RepairTooLarge,
}

/// <summary>The state of a file against its set; <see cref="Solution"/> is set when
/// <see cref="Status"/> is <see cref="Par2VerifyStatus.Repairable"/>.</summary>
internal sealed record Par2Verification(Par2VerifyStatus Status, int[] DamagedSlices, bool FileMissing, long ActualLength, RecoverySolution? Solution);

/// <summary>
/// Checks a file against a PAR 2.0 set (T-F275): each slice at its own position (slice i at
/// i · slice size), so it finds bad sectors and a cut tail; bytes inserted or removed shift every
/// slice after them, which this positional check reports as damage from that point on.
/// </summary>
internal static class Par2Verifier
{
    internal static Par2Verification Verify(string targetPath, Par2Set set, Action<double>? progress, CancellationToken cancellationToken)
    {
        (Par2FileHashes? hashes, long actualLength) = HashTarget(targetPath, set, progress, cancellationToken);

        var damaged = new List<int>();
        for (int i = 0; i < set.Slices.Length; i++)
        {
            if (hashes?.Slices[i] != set.Slices[i])
                damaged.Add(i);
        }
        int[] damagedSlices = [.. damaged];
        bool fileMissing = hashes is null;

        if (damagedSlices.Length == 0)
        {
            if (actualLength == set.FileLength && BinaryPrimitives.ReadUInt128LittleEndian(hashes!.FileMd5) == set.FileMd5)
                return new Par2Verification(Par2VerifyStatus.Intact, damagedSlices, fileMissing, actualLength, null);
            // Every slice matches: bytes past the recorded length are the only fault a repair can fix.
            Par2VerifyStatus status = actualLength > set.FileLength ? Par2VerifyStatus.Repairable : Par2VerifyStatus.NotRepairable;
            return new Par2Verification(status, damagedSlices, fileMissing, actualLength,
                status == Par2VerifyStatus.Repairable ? new RecoverySolution([], []) : null);
        }

        if ((long)damagedSlices.Length * (set.Slices.Length + damagedSlices.Length) > Par2Limits.MaxRepairMatrixEntries)
        {
            Par2VerifyStatus status = damagedSlices.Length <= set.RecoveryBlocks.Count ? Par2VerifyStatus.RepairTooLarge : Par2VerifyStatus.NotRepairable;
            return new Par2Verification(status, damagedSlices, fileMissing, actualLength, null);
        }

        RecoverySolution? solution = RecoveryMatrix.Solve(Gf16.InputConstants(set.Slices.Length), damagedSlices,
            [.. set.RecoveryBlocks.Select(b => b.Exponent)], cancellationToken);
        return new Par2Verification(solution is null ? Par2VerifyStatus.NotRepairable : Par2VerifyStatus.Repairable,
            damagedSlices, fileMissing, actualLength, solution);
    }

    // A missing file is not an error here: every slice of it is damaged.
    private static (Par2FileHashes?, long) HashTarget(string targetPath, Par2Set set, Action<double>? progress, CancellationToken cancellationToken)
    {
        try
        {
            using SafeFileHandle file = File.OpenHandle(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            long actualLength = RandomAccess.GetLength(file);
            var tracker = new Par2Progress(progress, actualLength);
            return (Par2FileHasher.Hash(file, set.FileLength, set.SliceSize, bytes => tracker.Add(bytes), cancellationToken), actualLength);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return (null, 0);
        }
    }
}
