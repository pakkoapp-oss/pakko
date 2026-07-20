using System.Buffers;
using System.Security.Cryptography;
using Archiver.Core.IO;
using Archiver.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Services;

/// <summary>Per-file hash result. <see cref="Error"/> is set instead of <see cref="Hash"/> when
/// the file couldn't be read, or when it was skipped (e.g. a folder in a multi-item selection).</summary>
public sealed record HashEntry(string SourcePath, string? Hash, string? Error);

/// <summary>Combined DataSum/NamesSum for a single recursively-hashed folder — see
/// <see cref="FileHashService"/>'s doc comment for what these mean and their NanaZip parity.</summary>
public sealed record FolderHashSummary(string DataSum, string NamesSum, int FileCount, long TotalBytes);

public sealed class HashResult
{
    public IReadOnlyList<HashEntry> Entries { get; init; } = Array.Empty<HashEntry>();

    /// <summary>Non-null only when the input was exactly one folder (see
    /// <see cref="FileHashService.ComputeAsync"/>).</summary>
    public FolderHashSummary? Folder { get; init; }
}

/// <summary>
/// T-F128: computes CRC-32/SHA-256 for the Explorer context menu's "Хеш-суми" submenu. A single
/// folder gets NanaZip-compatible combined DataSum (all file contents) and NamesSum (all file
/// names+paths+contents) values, via <see cref="HashDigestAccumulator"/> — verified bit-for-bit
/// against NanaZip's own <c>HashCalc.cpp</c>. One documented divergence: NanaZip's NamesSum also
/// folds in each *subfolder object's* own contribution using an order-dependent "stale digest"
/// left over from their specific traversal order; this service only sums real files (at any
/// nesting depth), which is fully order-independent and always exactly reproducible.
/// <para>
/// Files are hashed in parallel (<see cref="Parallel.ForEachAsync{TSource}(IEnumerable{TSource},
/// ParallelOptions, Func{TSource, CancellationToken, ValueTask})"/>, up to
/// <see cref="Environment.ProcessorCount"/> at once) — safe specifically because
/// <see cref="HashDigestAccumulator.Add"/> is commutative, so combining DataSum/NamesSum in
/// whatever order files finish hashing produces the exact same result as combining them
/// sequentially (already relied on for the recursion-safety argument above).
/// </para>
/// <para>
/// T-F128 follow-up: a single large CRC-32 file is <em>also</em> hashed in parallel — the
/// across-files parallelism above gives no benefit to a folder containing one huge file (or to a
/// lone large file passed to <see cref="ComputeAsync"/> directly). Files at or above
/// <see cref="ParallelCrc32MinFileBytes"/> are split into independently-hashed chunks and folded
/// back together with <see cref="Crc32.Combine"/> — safe for the same reason cross-file combining
/// is: CRC-32 combining is associative/order-preserving as long as chunks are folded back in their
/// original byte order (unlike DataSum/NamesSum, chunk order here does matter, so chunks are
/// combined sequentially by index after all finish, not as they complete).
/// </para>
/// </summary>
public static class FileHashService
{
    private const int FileStreamBufferSize = 262144;
    private const string FolderSkippedMessage = "Skipped: folder (only supported when a single folder is selected alone)";

    // T-F128 follow-up: below this size, sequential slice-by-8 is already fast enough (a handful
    // of milliseconds) that splitting into chunks and coordinating parallel tasks would cost more
    // than it saves. 4 MiB chunks keep per-chunk overhead low while still giving good load
    // balancing across cores for anything past the threshold.
    private const long ParallelCrc32MinFileBytes = 8 * 1024 * 1024;
    private const long ParallelCrc32ChunkBytes = 4 * 1024 * 1024;

    public static async Task<HashResult> ComputeAsync(
        IReadOnlyList<string> paths,
        HashAlgorithmKind algorithm,
        IProgress<ProgressReport>? progress,
        CancellationToken ct)
    {
        if (paths.Count == 1 && Directory.Exists(paths[0]))
            return await ComputeFolderAsync(paths[0], algorithm, progress, ct).ConfigureAwait(false);

        // Directory checks stay sequential (cheap, and there are usually only a handful of
        // paths here) — only the real files go through the parallel hashing pool. Writing into
        // a pre-sized array by original index (rather than a lock-guarded list) preserves the
        // caller's selection order with no synchronization needed: each slot is touched by
        // exactly one parallel iteration.
        var ordered = new HashEntry?[paths.Count];
        var fileIndices = new List<int>();
        for (int i = 0; i < paths.Count; i++)
        {
            if (Directory.Exists(paths[i]))
                ordered[i] = new HashEntry(paths[i], null, FolderSkippedMessage);
            else
                fileIndices.Add(i);
        }

        var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct };
        await Parallel.ForEachAsync(fileIndices, options, async (i, token) =>
        {
            var (digest, error) = await ComputeFileDigestAsync(paths[i], algorithm, progress, token).ConfigureAwait(false);
            ordered[i] = digest is null
                ? new HashEntry(paths[i], null, error)
                : new HashEntry(paths[i], FormatDigest(algorithm, digest), null);
        }).ConfigureAwait(false);

        return new HashResult { Entries = ordered! };
    }

    private static async Task<HashResult> ComputeFolderAsync(
        string root,
        HashAlgorithmKind algorithm,
        IProgress<ProgressReport>? progress,
        CancellationToken ct)
    {
        int digestSize = DigestSize(algorithm);
        var dataSum = new HashDigestAccumulator(digestSize);
        var namesSum = new HashDigestAccumulator(digestSize);
        var entries = new List<HashEntry>();
        int fileCount = 0;
        var sync = new object();

        // T-F128 follow-up: DirectoryInfo.EnumerateFiles (not Directory.EnumerateFiles, which only
        // returns paths) gives every file's Length for free from the same directory-listing
        // syscall Windows already performs — no extra per-file stat pass needed for the total-size
        // sum below (mirrors T-F35's "merge redundant directory walks" fix for the same reason).
        // That sum drives both FolderHashSummary.TotalBytes and the shared AggregateProgressTracker
        // below, which fixes the progress bug where each file's own completion (resetting to 0%
        // per file) was reported instead of the whole folder's aggregate progress.
        var files = new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
        long totalBytes = files.Sum(f => f.Length);
        var tracker = progress is null ? null : new AggregateProgressTracker(totalBytes, progress);

        // DataSum/NamesSum contributions are computed outside the lock (pure CPU work on
        // already-read bytes, no shared state) — only the final Accumulator.Add calls and list
        // mutation are serialized, keeping lock contention minimal under parallel hashing.
        var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct };
        await Parallel.ForEachAsync(
            files,
            options,
            async (file, token) =>
            {
                Func<FileStream, Stream>? wrap = tracker is null
                    ? null
                    : fs => new AggregateProgressStream(fs, tracker, file.Name);
                var (digest, error) = await ComputeFileDigestAsync(
                    file.FullName, file.Length, algorithm, wrap, tracker, file.Name, token).ConfigureAwait(false);
                if (digest is null)
                {
                    lock (sync) { entries.Add(new HashEntry(file.FullName, null, error)); }
                    return;
                }

                var relativePath = Path.GetRelativePath(root, file.FullName).Replace('\\', '/');
                var namesSumItem = ComputeNamesSumItemDigest(algorithm, digest, relativePath);

                lock (sync)
                {
                    entries.Add(new HashEntry(file.FullName, FormatDigest(algorithm, digest), null));
                    fileCount++;
                    dataSum.Add(digest);
                    namesSum.Add(namesSumItem);
                }
            }).ConfigureAwait(false);

        return new HashResult
        {
            Entries = entries,
            Folder = new FolderHashSummary(dataSum.ToDisplayString(), namesSum.ToDisplayString(), fileCount, totalBytes)
        };
    }

    // Mirrors NanaZip's CHashBundle::Final: Hash(pre[16 zero bytes] ++ fileDigest ++
    // UTF16LE-bytes-of-relativePath). "pre" stays all-zero here since this is only ever called
    // for real files (isDir=false in their code never sets pre[0]) — see this class's doc comment
    // for the documented subfolder-object omission.
    private static byte[] ComputeNamesSumItemDigest(HashAlgorithmKind algorithm, byte[] fileDigest, string relativePath)
    {
        Span<byte> pre = stackalloc byte[16];
        var pathBytes = new byte[relativePath.Length * 2];
        for (int i = 0; i < relativePath.Length; i++)
        {
            char c = relativePath[i];
            pathBytes[i * 2] = (byte)(c & 0xFF);
            pathBytes[i * 2 + 1] = (byte)((c >> 8) & 0xFF);
        }

        if (algorithm == HashAlgorithmKind.Crc32)
        {
            var acc = new Crc32.Accumulator();
            acc.Update(pre);
            acc.Update(fileDigest);
            acc.Update(pathBytes);
            return LittleEndianBytes(acc.Finish());
        }

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(pre);
        sha.AppendData(fileDigest);
        sha.AppendData(pathBytes);
        return sha.GetHashAndReset();
    }

    // T-F128 follow-up: ComputeAsync's general (non-folder) branch doesn't have a pre-fetched
    // FileInfo per path the way ComputeFolderAsync does (paths come straight from the caller's
    // selection) — this overload stats the file once, builds a per-file progress tracker sized to
    // that file's own length (matching the old per-file ProgressStream's semantics exactly), and
    // delegates to the FileInfo-based overload below.
    private static async Task<(byte[]? Digest, string? Error)> ComputeFileDigestAsync(
        string path, HashAlgorithmKind algorithm, IProgress<ProgressReport>? progress, CancellationToken ct)
    {
        try
        {
            var file = new FileInfo(path);
            var tracker = progress is null ? null : new AggregateProgressTracker(file.Length, progress);
            Func<FileStream, Stream>? wrap = tracker is null
                ? null
                : fs => new AggregateProgressStream(fs, tracker, file.Name);
            return await ComputeFileDigestAsync(file.FullName, file.Length, algorithm, wrap, tracker, file.Name, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, ex.Message);
        }
    }

    // T-F128 follow-up: takes both a stream-wrapping delegate (used for the sequential path —
    // SHA-256 always, and CRC-32 under the parallel-eligibility threshold) and the same tracker
    // directly (used only by the parallel CRC-32 path, which reads via RandomAccess rather than
    // through a Stream at all, so it reports progress straight into the tracker). Callers already
    // have both on hand, so there is no extra cost to passing both through.
    private static async Task<(byte[]? Digest, string? Error)> ComputeFileDigestAsync(
        string path, long length, HashAlgorithmKind algorithm,
        Func<FileStream, Stream>? wrapForProgress, AggregateProgressTracker? tracker, string currentFileName,
        CancellationToken ct)
    {
        try
        {
            if (algorithm == HashAlgorithmKind.Crc32 && length >= ParallelCrc32MinFileBytes)
            {
                byte[] parallelDigest = await ComputeFileCrc32ParallelAsync(path, length, tracker, currentFileName, ct)
                    .ConfigureAwait(false);
                return (parallelDigest, null);
            }

            using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: FileStreamBufferSize, useAsync: false);
            Stream source = wrapForProgress?.Invoke(fileStream) ?? fileStream;

            byte[] digest = await ReadAndDigestAsync(source, algorithm, ct).ConfigureAwait(false);
            return (digest, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// T-F128 follow-up: hashes one large file's CRC-32 in parallel — splits it into fixed-size
    /// chunks, hashes each chunk independently (fresh <see cref="Crc32.Accumulator"/> per chunk,
    /// positioned reads via <see cref="RandomAccess"/> so no per-chunk <see cref="FileStream"/> is
    /// needed), then folds the per-chunk CRCs back together <em>in original byte order</em> with
    /// <see cref="Crc32.Combine"/>. Unlike DataSum/NamesSum's cross-file combining (order doesn't
    /// matter, addition is commutative), chunk order here absolutely matters — CRC-32 isn't
    /// commutative — so chunks are combined sequentially by index after every chunk task
    /// completes, never as each one finishes.
    /// </summary>
    private static Task<byte[]> ComputeFileCrc32ParallelAsync(
        string path, long length, AggregateProgressTracker? tracker, string currentFileName, CancellationToken ct)
    {
        // T-F128 follow-up: EnsureThreadPoolWarm before the parallel section — measured directly
        // (not assumed) that without it, elapsed time swung wildly run-to-run (0.95x-2.9x of 7za's
        // own time for the same 300 MB file) purely from .NET's default ThreadPool ramp-up policy
        // (new worker threads are injected gradually, roughly one per ~500 ms under demand, unless
        // the pool already has enough). Bumping the minimum thread count once removes that
        // ramp-up latency, which is what actually made this "stable, with margin" as asked for.
        EnsureThreadPoolWarm();

        // Runs on a dedicated pool thread via Task.Run, then fans out via a synchronous
        // Parallel.For (not Parallel.ForAsync/async RandomAccess.ReadAsync) — plain synchronous
        // RandomAccess.Read per chunk avoids async-state-machine/completion-port scheduling
        // entirely, matching this project's own established "useAsync: false is faster on local
        // disks" FileStream convention (CLAUDE.md) for the same underlying reason.
        return Task.Run(() =>
        {
            int chunkCount = (int)Math.Min(
                Environment.ProcessorCount,
                (length + ParallelCrc32ChunkBytes - 1) / ParallelCrc32ChunkBytes);
            long baseChunkSize = length / chunkCount;

            var chunkCrcs = new uint[chunkCount];
            var chunkLengths = new long[chunkCount];

            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            var options = new ParallelOptions { MaxDegreeOfParallelism = chunkCount, CancellationToken = ct };
            Parallel.For(0, chunkCount, options, i =>
            {
                long start = i * baseChunkSize;
                long end = i == chunkCount - 1 ? length : start + baseChunkSize; // last chunk absorbs the remainder
                long chunkLength = end - start;
                chunkLengths[i] = chunkLength;

                var acc = new Crc32.Accumulator();
                byte[] buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(FileStreamBufferSize, chunkLength));
                try
                {
                    long offset = start;
                    long remaining = chunkLength;
                    while (remaining > 0)
                    {
                        int toRead = (int)Math.Min(buffer.Length, remaining);
                        int read = RandomAccess.Read(handle, buffer.AsSpan(0, toRead), offset);
                        if (read <= 0) break; // shouldn't happen for a file that isn't shrinking mid-read
                        acc.Update(buffer.AsSpan(0, read));
                        tracker?.Report(read, currentFileName);
                        offset += read;
                        remaining -= read;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
                chunkCrcs[i] = acc.Finish();
            });

            uint combined = chunkCrcs[0];
            for (int i = 1; i < chunkCount; i++)
                combined = Crc32.Combine(combined, chunkCrcs[i], chunkLengths[i]);

            return LittleEndianBytes(combined);
        }, ct);
    }

    private static int _threadPoolWarmed;

    private static void EnsureThreadPoolWarm()
    {
        if (Interlocked.Exchange(ref _threadPoolWarmed, 1) != 0) return;
        ThreadPool.GetMinThreads(out _, out int minIoc);
        ThreadPool.SetMinThreads(Environment.ProcessorCount, minIoc);
    }

    /// <summary>
    /// T-F09 follow-up (`pakko h -si`): hashes an arbitrary stream directly — a genuine single-pass
    /// pipeline, unlike <c>x</c>/<c>t</c>/<c>l</c>'s <c>-si</c> (which must stage stdin to a real
    /// seekable temp file first, since ZIP central-directory reads and tar.exe's pre-scan can't
    /// operate on a raw pipe). CRC-32/SHA-256 need no seeking, so no staging is needed here either —
    /// bytes are read once, straight from <paramref name="source"/>, with no intermediate file.
    /// </summary>
    public static async Task<string> ComputeStreamDigestAsync(
        Stream source, HashAlgorithmKind algorithm, CancellationToken ct)
    {
        byte[] digest = await ReadAndDigestAsync(source, algorithm, ct).ConfigureAwait(false);
        return FormatDigest(algorithm, digest);
    }

    private static async Task<byte[]> ReadAndDigestAsync(Stream source, HashAlgorithmKind algorithm, CancellationToken ct)
    {
        var buffer = new byte[FileStreamBufferSize];
        int read;

        if (algorithm == HashAlgorithmKind.Crc32)
        {
            var acc = new Crc32.Accumulator();
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                acc.Update(buffer.AsSpan(0, read));
            return LittleEndianBytes(acc.Finish());
        }

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            sha.AppendData(buffer.AsSpan(0, read));
        return sha.GetHashAndReset();
    }

    private static string FormatDigest(HashAlgorithmKind algorithm, byte[] digest) =>
        algorithm == HashAlgorithmKind.Crc32
            ? BitConverter.ToUInt32(digest).ToString("X8")
            : Convert.ToHexString(digest).ToLowerInvariant();

    private static int DigestSize(HashAlgorithmKind algorithm) =>
        algorithm == HashAlgorithmKind.Crc32 ? 4 : 32;

    // .NET's BitConverter is little-endian on every platform Pakko ships for (Windows x64/ARM64),
    // matching NanaZip's own internal little-endian CRC-32 digest byte layout that
    // HashDigestAccumulator's arithmetic assumes.
    private static byte[] LittleEndianBytes(uint value) => BitConverter.GetBytes(value);
}
