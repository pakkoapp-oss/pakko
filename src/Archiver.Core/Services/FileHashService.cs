using System.Security.Cryptography;
using Archiver.Core.IO;
using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>Per-file hash result. <see cref="Error"/> is set instead of <see cref="Hash"/> when
/// the file couldn't be read, or when it was skipped (e.g. a folder in a multi-item selection).</summary>
public sealed record HashEntry(string SourcePath, string? Hash, string? Error);

/// <summary>Combined DataSum/NamesSum for a single recursively-hashed folder — see
/// <see cref="FileHashService"/>'s doc comment for what these mean and their NanaZip parity.</summary>
public sealed record FolderHashSummary(string DataSum, string NamesSum, int FileCount);

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
/// </summary>
public static class FileHashService
{
    private const int FileStreamBufferSize = 262144;
    private const string FolderSkippedMessage = "Skipped: folder (only supported when a single folder is selected alone)";

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

        // DataSum/NamesSum contributions are computed outside the lock (pure CPU work on
        // already-read bytes, no shared state) — only the final Accumulator.Add calls and list
        // mutation are serialized, keeping lock contention minimal under parallel hashing.
        var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct };
        await Parallel.ForEachAsync(
            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories),
            options,
            async (filePath, token) =>
            {
                var (digest, error) = await ComputeFileDigestAsync(filePath, algorithm, progress, token).ConfigureAwait(false);
                if (digest is null)
                {
                    lock (sync) { entries.Add(new HashEntry(filePath, null, error)); }
                    return;
                }

                var relativePath = Path.GetRelativePath(root, filePath).Replace('\\', '/');
                var namesSumItem = ComputeNamesSumItemDigest(algorithm, digest, relativePath);

                lock (sync)
                {
                    entries.Add(new HashEntry(filePath, FormatDigest(algorithm, digest), null));
                    fileCount++;
                    dataSum.Add(digest);
                    namesSum.Add(namesSumItem);
                }
            }).ConfigureAwait(false);

        return new HashResult
        {
            Entries = entries,
            Folder = new FolderHashSummary(dataSum.ToDisplayString(), namesSum.ToDisplayString(), fileCount)
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

    private static async Task<(byte[]? Digest, string? Error)> ComputeFileDigestAsync(
        string path, HashAlgorithmKind algorithm, IProgress<ProgressReport>? progress, CancellationToken ct)
    {
        try
        {
            var length = new FileInfo(path).Length;
            using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: FileStreamBufferSize, useAsync: false);
            Stream source = progress is null
                ? fileStream
                : new ProgressStream(fileStream, length, startOffset: 0, progress, currentFile: Path.GetFileName(path));

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
