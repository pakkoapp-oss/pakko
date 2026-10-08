using System.Buffers;
using System.Security.Cryptography;
using Archiver.Core.IO;
using Archiver.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Services;

/// <summary>Per-file hash result. <see cref="Error"/> is set instead of <see cref="Hash"/> when
/// the file couldn't be read, or when it was skipped (e.g. a folder in a multi-item selection).</summary>
public sealed record HashEntry(string SourcePath, string? Hash, string? Error)
{
    /// <summary>The error as a code a frontend renders in the user's language (T-F209).</summary>
    public CoreText? ErrorText { get; init; }
}

/// <summary>Combined DataSum/NamesSum for a single recursively-hashed folder — see
/// <see cref="FileHashService"/>'s doc comment for what these mean and their NanaZip parity.</summary>
public sealed record FolderHashSummary(string DataSum, string NamesSum, int FileCount, long TotalBytes);

/// <summary>Result of a <see cref="FileHashService.ComputeAsync"/> call.</summary>
public sealed class HashResult
{
    /// <summary>One entry per input path, in the caller's original selection order.</summary>
    public IReadOnlyList<HashEntry> Entries { get; init; } = Array.Empty<HashEntry>();

    /// <summary>Non-null only when the input was exactly one folder (see
    /// <see cref="FileHashService.ComputeAsync"/>).</summary>
    public FolderHashSummary? Folder { get; init; }
}

/// <summary>
/// T-F128: computes CRC-32/SHA-256 for the Explorer context menu's "Хеш-суми" submenu. A single
/// folder gets NanaZip-compatible combined DataSum (all file contents) and NamesSum (all file
/// names+paths+contents) values, via <see cref="HashDigestAccumulator"/> — the algorithm of
/// 7-Zip's <c>HashCalc.cpp</c>, checked live against the vendored <c>7za h</c> by
/// <c>FolderHashParityTests</c> (T-F225). NamesSum includes one item per directory, the selected
/// folder itself included, hashed with an all-zero digest (7-Zip resets it before every item), so
/// the result does not depend on enumeration order. Remaining difference: symbolic links and
/// junctions are skipped here (T-F251), where 7-Zip follows them.
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

    // T-F128 follow-up: below this size, sequential slice-by-8 is already fast enough (a handful
    // of milliseconds) that splitting into chunks and coordinating parallel tasks would cost more
    // than it saves. 4 MiB chunks keep per-chunk overhead low while still giving good load
    // balancing across cores for anything past the threshold.
    private const long ParallelCrc32MinFileBytes = 8 * 1024 * 1024;
    private const long ParallelCrc32ChunkBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Hashes each of <paramref name="paths"/>. A single-folder selection is routed to the
    /// recursive DataSum/NamesSum path instead (see <see cref="HashResult.Folder"/>); a mixed or
    /// multi-item selection hashes each file independently in parallel.
    /// </summary>
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
                ordered[i] = CoreMessages.HashError(paths[i], CoreMessages.Text(MessageCode.HashFolderSkipped));
            else if (!File.Exists(paths[i]))
                ordered[i] = CoreMessages.HashError(paths[i], CoreMessages.Text(MessageCode.SourceNotFound, paths[i]));
            else
                fileIndices.Add(i);
        }

        var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct };
        await Parallel.ForEachAsync(fileIndices, options, async (i, token) =>
        {
            (byte[]? digest, CoreText? error) = await ComputeFileDigestAsync(paths[i], algorithm, progress, token).ConfigureAwait(false);
            ordered[i] = digest is null
                ? CoreMessages.HashError(paths[i], error!)
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
        Lock sync = new();

        // T-F251: the shared DirectoryWalker — an unreadable folder is one error entry instead of
        // an exception out of this method, and a junction or symlink is skipped (never followed:
        // no loop, no foreign files), the same as archive creation (T-F23).
        // T-F128 follow-up: the walker's FileInfo objects carry each Length from the directory
        // listing itself — no extra per-file stat pass for the total-size sum below, which drives
        // both FolderHashSummary.TotalBytes and the shared AggregateProgressTracker.
        // T-F225: 7-Zip hashes every item under its log path. For "h C:\x\one" that is "one",
        // "one/a.txt", ... (the folder itself is an item); for "h ." or a drive root it is only the
        // contents, "a.txt", ... (checked against 7za 26.02).
        string? rootName = RootLogName(root);
        List<FileInfo> files = CollectFolderItems(root, rootName, algorithm, namesSum, entries, ct);
        long totalBytes = files.Sum(f => f.Length);
        AggregateProgressTracker? tracker = progress is null ? null : new AggregateProgressTracker(totalBytes, progress);

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
                (byte[]? digest, CoreText? error) = await ComputeFileDigestAsync(
                    file.FullName, file.Length, algorithm, wrap, tracker, file.Name, token).ConfigureAwait(false);
                if (digest is null)
                {
                    lock (sync) { entries.Add(CoreMessages.HashError(file.FullName, error!)); }
                    return;
                }

                byte[] namesSumItem = ComputeNamesSumItemDigest(algorithm, isDirectory: false, digest, LogPath(root, rootName, file)!);

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

    // Lists the files to hash; directories go straight into NamesSum, skipped items into entries.
    private static List<FileInfo> CollectFolderItems(
        string root,
        string? rootName,
        HashAlgorithmKind algorithm,
        HashDigestAccumulator namesSum,
        List<HashEntry> entries,
        CancellationToken ct)
    {
        int digestSize = DigestSize(algorithm);
        var files = new List<FileInfo>();
        foreach (WalkEntry entry in DirectoryWalker.Walk(root))
        {
            ct.ThrowIfCancellationRequested();
            switch (entry.Kind)
            {
                case WalkEntryKind.File:
                    files.Add((FileInfo)entry.Info);
                    break;
                case WalkEntryKind.Directory:
                    // A directory's item digest is all zeros: 7-Zip resets it before every item.
                    if (LogPath(root, rootName, entry.Info) is { } directoryLogPath)
                        namesSum.Add(ComputeNamesSumItemDigest(algorithm, isDirectory: true, new byte[digestSize], directoryLogPath));
                    break;
                case WalkEntryKind.ReparsePoint:
                    entries.Add(CoreMessages.HashError(entry.Info.FullName, CoreMessages.Text(MessageCode.HashLinkSkipped)));
                    break;
                case WalkEntryKind.UnreadableDirectory:
                    entries.Add(CoreMessages.HashError(entry.Info.FullName, CoreMessages.FromException(entry.Error!)));
                    break;
            }
        }
        return files;
    }

    private static string? LogPath(string root, string? rootName, FileSystemInfo info)
    {
        string relative = Path.GetRelativePath(root, info.FullName).Replace('\\', '/');
        if (relative != ".")
            return rootName is null ? relative : rootName + "/" + relative;
        return rootName;
    }

    // T-F225: the folder's own name as 7-Zip spells it — the name on disk, not the argument's
    // casing ("h ONE" lists "one\"). Null when the argument names only contents: ".", "..", or a
    // drive root, which 7-Zip hashes without the folder item and without a name prefix.
    private static string? RootLogName(string root)
    {
        string lastSegment = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
        if (lastSegment is "" or "." or "..")
            return null;

        var directory = new DirectoryInfo(root);
        try
        {
            return directory.Parent?.EnumerateDirectories(directory.Name).FirstOrDefault()?.Name ?? directory.Name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return directory.Name; // parent not listable: keep the argument's spelling
        }
    }

    // Mirrors 7-Zip's CHashBundle::Final: Hash(pre[16] ++ itemDigest ++ UTF16LE-bytes-of-logPath), // NOSONAR: prose, not commented-out code (S125 false positive)
    // where pre[0] = 1 for a directory and every other byte is zero.
    private static byte[] ComputeNamesSumItemDigest(HashAlgorithmKind algorithm, bool isDirectory, byte[] itemDigest, string logPath)
    {
        Span<byte> pre = stackalloc byte[16];
        pre[0] = isDirectory ? (byte)1 : (byte)0;
        byte[] pathBytes = new byte[logPath.Length * 2];
        for (int i = 0; i < logPath.Length; i++)
        {
            char c = logPath[i];
            pathBytes[i * 2] = (byte)(c & 0xFF);
            pathBytes[i * 2 + 1] = (byte)((c >> 8) & 0xFF);
        }

        if (algorithm == HashAlgorithmKind.Crc32)
        {
            var acc = new Crc32.Accumulator();
            acc.Update(pre);
            acc.Update(itemDigest);
            acc.Update(pathBytes);
            return LittleEndianBytes(acc.Finish());
        }

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(pre);
        sha.AppendData(itemDigest);
        sha.AppendData(pathBytes);
        return sha.GetHashAndReset();
    }

    // T-F128 follow-up: ComputeAsync's general (non-folder) branch doesn't have a pre-fetched
    // FileInfo per path the way ComputeFolderAsync does (paths come straight from the caller's
    // selection) — this overload stats the file once, builds a per-file progress tracker sized to
    // that file's own length (matching the old per-file ProgressStream's semantics exactly), and
    // delegates to the FileInfo-based overload below.
    private static async Task<(byte[]? Digest, CoreText? Error)> ComputeFileDigestAsync(
        string path, HashAlgorithmKind algorithm, IProgress<ProgressReport>? progress, CancellationToken ct)
    {
        try
        {
            var file = new FileInfo(path);
            AggregateProgressTracker? tracker = progress is null ? null : new AggregateProgressTracker(file.Length, progress);
            Func<FileStream, Stream>? wrap = tracker is null
                ? null
                : fs => new AggregateProgressStream(fs, tracker, file.Name);
            return await ComputeFileDigestAsync(file.FullName, file.Length, algorithm, wrap, tracker, file.Name, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, CoreMessages.FromException(ex));
        }
    }

    // T-F128 follow-up: takes both a stream-wrapping delegate (used for the sequential path —
    // SHA-256 always, and CRC-32 under the parallel-eligibility threshold) and the same tracker
    // directly (used only by the parallel CRC-32 path, which reads via RandomAccess rather than
    // through a Stream at all, so it reports progress straight into the tracker). Callers already
    // have both on hand, so there is no extra cost to passing both through.
    private static async Task<(byte[]? Digest, CoreText? Error)> ComputeFileDigestAsync(
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
            return (null, CoreMessages.FromException(ex));
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
    internal static Task<byte[]> ComputeFileCrc32ParallelAsync(
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

            uint[] chunkCrcs = new uint[chunkCount];
            long[] chunkLengths = new long[chunkCount];

            using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);

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
                        // T-F251: the file shrank after its length was read. Crc32.Combine below
                        // uses the planned chunk length, so a CRC here would be wrong yet look fine.
                        if (read <= 0)
                            throw new CoreTextIOException(CoreMessages.Text(MessageCode.HashFileChanged, path));
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

    // T-F271 follow-up: pooled — a fresh 256 KiB (large-object heap) array per file dominated
    // hashing many small files.
    private static async Task<byte[]> ReadAndDigestAsync(Stream source, HashAlgorithmKind algorithm, CancellationToken ct)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(FileStreamBufferSize);
        try
        {
            Memory<byte> buffer = rented.AsMemory(0, FileStreamBufferSize);
            int read;

            if (algorithm == HashAlgorithmKind.Crc32)
            {
                var acc = new Crc32.Accumulator();
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    acc.Update(buffer.Span[..read]);
                return LittleEndianBytes(acc.Finish());
            }

            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                sha.AppendData(buffer.Span[..read]);
            return sha.GetHashAndReset();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
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
