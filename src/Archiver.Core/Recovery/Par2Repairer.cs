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
        return Repair(targetPath, set, verification, outputPath, f => progress?.Invoke(VerifyShare + (1 - VerifyShare) * f), cancellationToken);
    }

    /// <summary>The same with <paramref name="verification"/> already done by the caller, so the
    /// file is not read twice. If the file changed since, the final check fails and nothing is kept.</summary>
    internal static Par2RepairResult Repair(
        string targetPath, Par2Set set, Par2Verification verification, string outputPath, Action<double>? progress, CancellationToken cancellationToken)
    {
        switch (verification.Status)
        {
            case Par2VerifyStatus.Intact:
                return new Par2RepairResult(Par2RepairStatus.NothingToRepair, 0);
            case Par2VerifyStatus.NotRepairable:
                return new Par2RepairResult(Par2RepairStatus.NotRepairable, 0);
            case Par2VerifyStatus.RepairTooLarge:
                return new Par2RepairResult(Par2RepairStatus.RepairTooLarge, 0);
        }
        int missingCount = verification.DamagedSlices.Length;

        // After the verify pass: the copy, the combination (one pass per missing slice), the check.
        double work = 2.0 * set.FileLength + (double)set.Slices.Length * set.SliceSize * missingCount;
        var tracker = new Par2Progress(progress, work);

        string temp = ArchiveTempFile.Create(outputPath);
        try
        {
            bool matches;
            using (var files = new HandleCache())
            using (SafeFileHandle output = File.OpenHandle(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                SafeFileHandle? source = verification.FileMissing ? null : files.Open(targetPath);
                var io = new RepairFiles(source, source is null ? 0 : RandomAccess.GetLength(source), output, files);
                Copy(io.Source, output, Math.Min(io.SourceLength, set.FileLength), tracker, cancellationToken);
                RandomAccess.SetLength(output, set.FileLength);
                if (missingCount > 0)
                    Rebuild(set, verification, io, tracker, cancellationToken);

                Par2FileHashes check = Par2FileHasher.Hash(output, set.FileLength, set.SliceSize, b => tracker.Add(b), cancellationToken);
                matches = BinaryPrimitives.ReadUInt128LittleEndian(check.FileMd5) == set.FileMd5
                    && check.Slices.Zip(set.Slices).All(pair => pair.First == pair.Second);
            }
            if (!matches)
            {
                DeleteQuietly(temp);
                return new Par2RepairResult(Par2RepairStatus.VerificationFailed, 0);
            }
            File.Move(temp, outputPath, overwrite: false);
            return new Par2RepairResult(Par2RepairStatus.Repaired, missingCount);
        }
        catch
        {
            DeleteQuietly(temp);
            throw;
        }
    }

    // Inputs: the intact slices, then the chosen recovery blocks. Missing slice j is
    // Σ_t inverse[j][t] · block t + Σ_i (Σ_t inverse[j][t] · c_i^e_t) · slice i.
    private static void Rebuild(Par2Set set, Par2Verification verification, RepairFiles io, Par2Progress tracker, CancellationToken cancellationToken)
    {
        RecoverySolution solution = verification.Solution!;
        int[] missing = verification.DamagedSlices;
        int m = missing.Length;
        var missingSet = new HashSet<int>(missing);
        int[] present = [.. Enumerable.Range(0, set.Slices.Length).Where(i => !missingSet.Contains(i))];
        Par2RecoveryBlock[] blocks = [.. solution.ChosenBlocks.Select(b => set.RecoveryBlocks[b])];
        ushort[][] presentCoefficients = PresentCoefficients(set, solution, present, blocks, cancellationToken);

        SafeFileHandle[] blockFiles = [.. blocks.Select(b => io.Files.Open(b.Path))];
        long[] blockFileLengths = [.. blockFiles.Select(RandomAccess.GetLength)];
        long sourceEnd = Math.Min(io.SourceLength, set.FileLength);
        Par2SliceCombiner.Combine(new Par2CombineShape(set.SliceSize, present.Length + m, m),
            (j, i) => i < present.Length ? presentCoefficients[j][i] : solution.Inverse[j][i - present.Length],
            (i, at, buffer) =>
            {
                if (i < present.Length)
                {
                    Par2FileIo.ReadPadded(io.Source!, sourceEnd, present[i] * set.SliceSize + at, buffer);
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
                    RandomAccess.Write(io.Output, data[..length], offset);
            },
            bytes => tracker.Add((double)bytes * m), cancellationToken);
    }

    // presentCoefficients[j][i] = Σ_t inverse[j][t] · c_present[i] ^ e_t, built a row of powers at a time.
    private static ushort[][] PresentCoefficients(Par2Set set, RecoverySolution solution, int[] present, Par2RecoveryBlock[] blocks, CancellationToken cancellationToken)
    {
        int m = blocks.Length;
        ushort[] constants = Gf16.InputConstants(set.Slices.Length);
        ushort[][] coefficients = new ushort[m][];
        for (int j = 0; j < m; j++)
            coefficients[j] = new ushort[present.Length];
        ushort[] powers = new ushort[present.Length];
        for (int t = 0; t < m; t++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int i = 0; i < present.Length; i++)
                powers[i] = Gf16.Pow(constants[present[i]], blocks[t].Exponent);
            for (int j = 0; j < m; j++)
                Gf16Region.MulAdd(solution.Inverse[j][t], MemoryMarshal.AsBytes(powers.AsSpan()), MemoryMarshal.AsBytes(coefficients[j].AsSpan()));
        }
        return coefficients;
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

    private sealed record RepairFiles(SafeFileHandle? Source, long SourceLength, SafeFileHandle Output, HandleCache Files);

    /// <summary>The input files of a repair, each opened once, read-shared, closed together.</summary>
    private sealed class HandleCache : IDisposable
    {
        private readonly Dictionary<string, SafeFileHandle> _handles = new(StringComparer.OrdinalIgnoreCase);

        internal SafeFileHandle Open(string path)
        {
            if (!_handles.TryGetValue(path, out SafeFileHandle? handle))
                _handles[path] = handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return handle;
        }

        public void Dispose()
        {
            foreach (SafeFileHandle handle in _handles.Values)
                handle.Dispose();
        }
    }
}
