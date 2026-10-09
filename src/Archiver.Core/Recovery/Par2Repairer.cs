using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Archiver.Core.Services;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Recovery;

internal enum Par2RepairStatus
{
    Repaired,

    /// <summary>The file is intact; nothing was written.</summary>
    NothingToRepair,

    NotRepairable,

    /// <summary>See <see cref="Par2VerifyStatus.RepairTooLarge"/>.</summary>
    RepairTooLarge,

    /// <summary>The rebuilt file did not match the set (the recovery data was wrong, or a file
    /// changed during the repair); nothing was kept.</summary>
    VerificationFailed,
}

internal sealed record Par2RepairResult(Par2RepairStatus Status, int RepairedSlices);

/// <summary>
/// Rebuilds a damaged file from its PAR 2.0 set into a new file (T-F275). The damaged file is only
/// read, never written; the output is built in a temporary file next to it, checked slice by slice
/// and by its MD5 against the set, and only then renamed to <c>outputPath</c>, which must not exist.
/// </summary>
internal static class Par2Repairer
{
    private const int CopyBufferLength = 1 << 20;
    private const double VerifyShare = 0.2;

    internal static Par2RepairResult Repair(string targetPath, Par2Set set, string outputPath, Action<double>? progress, CancellationToken cancellationToken)
    {
        Par2Verification verification = Par2Verifier.Verify(targetPath, set, f => progress?.Invoke(f * VerifyShare), cancellationToken);
        switch (verification.Status)
        {
            case Par2VerifyStatus.Intact:
                return new Par2RepairResult(Par2RepairStatus.NothingToRepair, 0);
            case Par2VerifyStatus.NotRepairable:
                return new Par2RepairResult(Par2RepairStatus.NotRepairable, 0);
            case Par2VerifyStatus.RepairTooLarge:
                return new Par2RepairResult(Par2RepairStatus.RepairTooLarge, 0);
        }
        RecoverySolution solution = verification.Solution!;
        int[] missing = verification.DamagedSlices;
        int missingCount = missing.Length;

        // After the verify pass: the copy, the combination (one pass per missing slice), the check.
        double work = 2.0 * set.FileLength + (double)set.Slices.Length * set.SliceSize * missingCount;
        var tracker = new Par2Progress(f => progress?.Invoke(VerifyShare + (1 - VerifyShare) * f), work);

        string temp = ArchiveTempFile.Create(outputPath);
        var handles = new Dictionary<string, SafeFileHandle>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using (SafeFileHandle output = File.OpenHandle(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                SafeFileHandle? source = verification.FileMissing ? null : Open(handles, targetPath);
                long sourceLength = source is null ? 0 : RandomAccess.GetLength(source);
                Copy(source, output, Math.Min(sourceLength, set.FileLength), tracker, cancellationToken);
                RandomAccess.SetLength(output, set.FileLength);
                if (missingCount > 0)
                    Rebuild(set, solution, missing, source, sourceLength, output, handles, tracker, cancellationToken);

                Par2FileHashes check = Par2FileHasher.Hash(output, set.FileLength, set.SliceSize, b => tracker.Add(b), cancellationToken);
                bool matches = BinaryPrimitives.ReadUInt128LittleEndian(check.FileMd5) == set.FileMd5
                    && check.Slices.Zip(set.Slices).All(pair => pair.First == pair.Second);
                if (!matches)
                {
                    output.Dispose();
                    DeleteQuietly(temp);
                    return new Par2RepairResult(Par2RepairStatus.VerificationFailed, 0);
                }
            }
            foreach (SafeFileHandle handle in handles.Values)
                handle.Dispose();
            handles.Clear();
            File.Move(temp, outputPath, overwrite: false);
            return new Par2RepairResult(Par2RepairStatus.Repaired, missingCount);
        }
        catch
        {
            DeleteQuietly(temp);
            throw;
        }
        finally
        {
            foreach (SafeFileHandle handle in handles.Values)
                handle.Dispose();
        }
    }

    // Inputs: the intact slices, then the chosen recovery blocks. Missing slice j is
    // Σ_t inverse[j][t] · block t + Σ_i (Σ_t inverse[j][t] · c_i^e_t) · slice i.
    private static void Rebuild(Par2Set set, RecoverySolution solution, int[] missing, SafeFileHandle? source, long sourceLength,
        SafeFileHandle output, Dictionary<string, SafeFileHandle> handles, Par2Progress tracker, CancellationToken cancellationToken)
    {
        int m = missing.Length;
        var missingSet = new HashSet<int>(missing);
        int[] present = [.. Enumerable.Range(0, set.Slices.Length).Where(i => !missingSet.Contains(i))];
        Par2RecoveryBlock[] blocks = [.. solution.ChosenBlocks.Select(b => set.RecoveryBlocks[b])];
        ushort[] constants = Gf16.InputConstants(set.Slices.Length);

        var presentCoefficients = new ushort[m][];
        for (int j = 0; j < m; j++)
            presentCoefficients[j] = new ushort[present.Length];
        var powers = new ushort[present.Length];
        for (int t = 0; t < m; t++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int i = 0; i < present.Length; i++)
                powers[i] = Gf16.Pow(constants[present[i]], blocks[t].Exponent);
            for (int j = 0; j < m; j++)
                Gf16Region.MulAdd(solution.Inverse[j][t], MemoryMarshal.AsBytes(powers.AsSpan()), MemoryMarshal.AsBytes(presentCoefficients[j].AsSpan()));
        }

        SafeFileHandle[] blockFiles = [.. blocks.Select(b => Open(handles, b.Path))];
        long[] blockFileLengths = [.. blockFiles.Select(RandomAccess.GetLength)];
        Par2SliceCombiner.Combine(set.SliceSize, present.Length + m, m,
            (j, i) => i < present.Length ? presentCoefficients[j][i] : solution.Inverse[j][i - present.Length],
            (i, at, buffer) =>
            {
                if (i < present.Length)
                {
                    Par2FileIo.ReadPadded(source!, Math.Min(sourceLength, set.FileLength), present[i] * set.SliceSize + at, buffer);
                    return;
                }
                int t = i - present.Length;
                Par2FileIo.ReadPadded(blockFiles[t], blockFileLengths[t], blocks[t].DataOffset + at, buffer);
            },
            (j, at, data) =>
            {
                long offset = missing[j] * set.SliceSize + at;
                int length = (int)Math.Clamp(set.FileLength - offset, 0, data.Length);
                if (length > 0)
                    RandomAccess.Write(output, data[..length], offset);
            },
            bytes => tracker.Add((double)bytes * m), cancellationToken);
    }

    private static void Copy(SafeFileHandle? source, SafeFileHandle output, long length, Par2Progress tracker, CancellationToken cancellationToken)
    {
        if (source is null)
            return;
        byte[] buffer = new byte[CopyBufferLength];
        for (long done = 0; done < length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = Par2FileIo.ReadUpTo(source, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - done)), done);
            if (read <= 0)
                break;
            RandomAccess.Write(output, buffer.AsSpan(0, read), done);
            done += read;
            tracker.Add(read);
        }
    }

    private static SafeFileHandle Open(Dictionary<string, SafeFileHandle> handles, string path)
    {
        if (!handles.TryGetValue(path, out SafeFileHandle? handle))
            handles[path] = handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return handle;
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
